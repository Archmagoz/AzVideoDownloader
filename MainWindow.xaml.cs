using Microsoft.Win32; // OpenFolderDialog (available on .NET 8+ / WPF)
using System.IO;
using System.Windows;
using System.Windows.Controls;

using YoutubeDLSharp;

using AzVideoDownloader.Services.Fetch;
using AzVideoDownloader.Services.Core;
using AzVideoDownloader.Services.Models;
using AzVideoDownloader.Services.Helpers;

namespace AzVideoDownloader
{
    public partial class MainWindow : Window
    {
        // ------------------------------------------------------------
        //  SERVICES
        // ------------------------------------------------------------
        private readonly GetVideoinfo _videoInfoService = null!;
        private readonly GetVideoThumbnail _thumbnailService = new();
        private readonly VideoDownloadService _videoDownloadService = null!;
        private readonly DebouncedTriggerHelper _linkDebounce = null!;

        // Cancels a stale in-flight fetch when a newer one supersedes it.
        private CancellationTokenSource? _fetchCts;

        // Duration (seconds) of the currently loaded video, used to derive
        // an approximate bitrate per selected format.
        private double? _currentVideoDurationSeconds;

        // Provides the actual yt-dlp/ffmpeg functionality.
        private readonly YoutubeDL _ytdl = null!;

        // Time to wait after the user stops typing before fetching video info.
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(700);

        // Container extensions offered by ChangeExtensionComboBox for a
        // regular video download. Kept in sync with the ComboBoxItems
        // declared in MainWindow.xaml so the designer preview matches the
        // runtime default state.
        private static readonly string[] VideoContainerExtensions = { "mp4", "mkv", "mov", "webm" };

        // Container extensions offered by ChangeExtensionComboBox once
        // "Somente áudio" is checked - video containers don't make sense
        // for an audio-only output.
        private static readonly string[] AudioContainerExtensions = { "mp3", "m4a", "opus", "wav" };

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                ToolManagerService.EnsureToolsExist();
            }
            catch (FileNotFoundException ex)
            {
                MessageBox.Show(ex.Message, "Az Video Downloader",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            _ytdl = new YoutubeDL
            {
                YoutubeDLPath = ToolManagerService.YtDlpPath,
                FFmpegPath = ToolManagerService.FfmpegPath,
                OutputFolder = OutputDir.Text
            };

            _videoInfoService = new GetVideoinfo(_ytdl);
            _videoDownloadService = new VideoDownloadService(_ytdl);

            _linkDebounce = new DebouncedTriggerHelper(DebounceDelay, OnLinkDebounceElapsed);

            VideoFormatListBox.SelectionChanged += VideoFormatListBox_SelectionChanged;
            InputLink.TextChanged += InputLink_TextChanged;

            // Drives the "audio only" cross-control state: disabling
            // merge/subtitles (they don't apply to an audio-only output)
            // and swapping the extension combo between video/audio containers.
            AudioOnlyCheckBox.Checked += AudioOnlyCheckBox_Checked;
            AudioOnlyCheckBox.Unchecked += AudioOnlyCheckBox_Unchecked;

            // Fires for BOTH Ctrl+V and the right-click "Paste" context menu
            // item, since both route through the same WPF paste command.
            // We use it to skip the debounce delay specifically on paste.
            DataObject.AddPastingHandler(InputLink, InputLink_Pasting);
        }

        // ------------------------------------------------------------
        //  TOP BAR ACTIONS
        // ------------------------------------------------------------

        private void PasteLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                InputLink.Text = Clipboard.GetText().Trim();
                _linkDebounce.TriggerNow();
            }
        }

        private void InputLink_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            // This event fires BEFORE the pasted text is actually inserted
            // into the TextBox, so we defer to a lower dispatcher priority
            // to run right after WPF finishes updating InputLink.Text.
            Dispatcher.BeginInvoke(new Action(_linkDebounce.TriggerNow),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void InputLink_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputLink.Text))
            {
                // Nothing to fetch: cancel any pending work and go back to
                // the empty/placeholder state immediately, no need to wait.
                _linkDebounce.Cancel();
                _fetchCts?.Cancel();
                ResetToDefaultState();
                return;
            }

            _linkDebounce.Arm();
        }

        private void OnLinkDebounceElapsed() => _ = FetchVideoInfoAsync(InputLink.Text.Trim());

        // ------------------------------------------------------------
        //  VIDEO INFO FETCH
        //  Orchestrates VideoInfoService + ThumbnailService and pushes
        //  the results into the UI controls.
        // ------------------------------------------------------------

        private async Task FetchVideoInfoAsync(string url)
        {
            // Supersede any fetch still in flight - only the most recent
            // link the user landed on should end up populating the UI.
            _fetchCts?.Cancel();
            _fetchCts?.Dispose();
            var cts = new CancellationTokenSource();
            _fetchCts = cts;

            if (string.IsNullOrWhiteSpace(url))
            {
                ResetToDefaultState();
                return;
            }

            SetFetchingState(true);

            try
            {
                var info = await _videoInfoService.FetchAsync(url, cts.Token);

                if (cts.Token.IsCancellationRequested)
                    return; // superseded by a newer fetch

                if (info is null)
                {
                    ResetToDefaultState();
                    return;
                }

                ApplyVideoInfo(info);

                var thumbnail = await _thumbnailService.LoadAsync(info.ThumbnailUrl);
                if (!cts.Token.IsCancellationRequested)
                {
                    ThumbPreview.Source = thumbnail;
                    ThumbPlaceholderText.Visibility = thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer fetch - nothing to show for this one.
            }
            catch (Exception)
            {
                if (!cts.Token.IsCancellationRequested)
                {
                    // Link didn't resolve to anything yt-dlp understands -
                    // fall back to the default/placeholder state.
                    ResetToDefaultState();
                }
            }
            finally
            {
                if (!cts.Token.IsCancellationRequested)
                {
                    SetFetchingState(false);
                }
            }
        }

        private void ApplyVideoInfo(VideoInfoResult info)
        {
            _currentVideoDurationSeconds = info.DurationSeconds;

            VideoTitleText.Text = info.Title;
            VideoDurationText.Text = info.DurationSeconds.HasValue
                ? TimeSpan.FromSeconds(info.DurationSeconds.Value).ToString(@"hh\:mm\:ss")
                : "—";

            VideoFormatListBox.ItemsSource = info.VideoFormats;
            VideoFormatListBox.DisplayMemberPath = nameof(GetAVFormatList.Display);

            AudioFormatListBox.ItemsSource = info.AudioFormats;
            AudioFormatListBox.DisplayMemberPath = nameof(GetAVFormatList.Display);

            // Default the selection to mp4/m4a-compatible streams rather
            // than blindly picking index 0: the app's default workflow is
            // "merge + change extension to mp4", and starting from a
            // format that already matches that container avoids an
            // unnecessary (slow, lossy) re-encode during download.
            VideoFormatListBox.SelectedItem = SelectPreferredFormat(info.VideoFormats, preferredExtension: "mp4");
            AudioFormatListBox.SelectedItem = SelectPreferredFormat(info.AudioFormats, preferredExtension: "m4a");
        }

        /// <summary>
        /// Picks the first format whose container extension matches
        /// <paramref name="preferredExtension"/>, falling back to the first
        /// available format when no match exists (e.g. a source that only
        /// offers webm). Returns <see langword="null"/> when the list is empty.
        /// </summary>
        private static GetAVFormatList? SelectPreferredFormat(
            IReadOnlyList<GetAVFormatList> formats,
            string preferredExtension)
        {
            if (formats.Count == 0)
            {
                return null;
            }

            return formats.FirstOrDefault(f =>
                       string.Equals(f.Source.Extension, preferredExtension, StringComparison.OrdinalIgnoreCase))
                   ?? formats[0];
        }

        private void SetFetchingState(bool isFetching)
        {
            DownloadButton.IsEnabled = !isFetching;
            ThumbPlaceholderText.Text = isFetching ? "Carregando..." : "Pré-visualização";

            VideoFormatsLoadingIndicator.Visibility = isFetching ? Visibility.Visible : Visibility.Collapsed;
            AudioFormatsLoadingIndicator.Visibility = isFetching ? Visibility.Visible : Visibility.Collapsed;

            if (isFetching)
            {
                VideoFormatListBox.ItemsSource = null;
                AudioFormatListBox.ItemsSource = null;
            }
        }

        /// <summary>
        /// Clears the whole info panel back to its empty/placeholder state.
        /// Used when the link is cleared, or when a fetch fails to resolve.
        /// </summary>
        private void ResetToDefaultState()
        {
            _currentVideoDurationSeconds = null;

            VideoTitleText.Text = "—";
            VideoDurationText.Text = "—";
            VideoFpsText.Text = "—";
            VideoBitrateText.Text = "—";
            VideoResolutionText.Text = "—";
            VideoSizeText.Text = "—";

            VideoFormatListBox.ItemsSource = null;
            AudioFormatListBox.ItemsSource = null;

            ThumbPreview.Source = null;
            ThumbPlaceholderText.Text = "Pré-visualização";
            ThumbPlaceholderText.Visibility = Visibility.Visible;

            VideoFormatsLoadingIndicator.Visibility = Visibility.Collapsed;
            AudioFormatsLoadingIndicator.Visibility = Visibility.Collapsed;

            DownloadButton.IsEnabled = true;
        }

        // ------------------------------------------------------------
        //  VIDEO FORMAT SELECTION
        //  Updates the info panel (fps/resolution/bitrate/size) whenever
        //  the user picks a different video format from the list.
        // ------------------------------------------------------------

        private void VideoFormatListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VideoFormatListBox.SelectedItem is not GetAVFormatList item)
            {
                VideoFpsText.Text = "—";
                VideoResolutionText.Text = "—";
                VideoBitrateText.Text = "—";
                VideoSizeText.Text = "—";
                return;
            }

            var f = item.Source;

            VideoFpsText.Text = f.FrameRate.HasValue ? $"{f.FrameRate:0}" : "—";
            VideoResolutionText.Text = (f.Width.HasValue && f.Height.HasValue)
                ? $"{f.Width}x{f.Height}"
                : "—";

            var sizeBytes = f.FileSize ?? f.ApproximateFileSize;
            VideoSizeText.Text = sizeBytes.HasValue
                ? $"{sizeBytes.Value / 1024.0 / 1024.0:0.#} MB"
                : "—";

            // Approximate bitrate: derived from filesize/duration rather
            // than a direct property (no stable one confirmed on FormatData).
            VideoBitrateText.Text = (sizeBytes.HasValue && _currentVideoDurationSeconds is > 0)
                ? $"{sizeBytes.Value * 8 / _currentVideoDurationSeconds.Value / 1000:0} kbps (aprox.)"
                : "—";
        }

        // ------------------------------------------------------------
        //  FFMPEG OPTIONS
        //  "Somente áudio" changes what the other ffmpeg options mean:
        //  merging separate streams and embedding subtitles no longer
        //  apply, and the output container should be an audio format.
        // ------------------------------------------------------------

        private void AudioOnlyCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // Merging audio+video streams and embedding subtitles are
            // meaningless once we're extracting audio only - disable both
            // (and clear their checked state) so a stale IsChecked=true
            // can't leak into FfmpegOptions while the controls are hidden
            // from interaction.
            MergeAudioVideoCheckBox.IsEnabled = false;
            MergeAudioVideoCheckBox.IsChecked = false;

            EmbedSubtitlesCheckBox.IsEnabled = false;
            EmbedSubtitlesCheckBox.IsChecked = false;

            PopulateExtensionComboBox(AudioContainerExtensions, preferredDefault: "mp3");
        }

        private void AudioOnlyCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // Restore the default "merge" workflow; subtitles stay
            // unchecked since that was its original default state too.
            MergeAudioVideoCheckBox.IsEnabled = true;
            MergeAudioVideoCheckBox.IsChecked = true;

            EmbedSubtitlesCheckBox.IsEnabled = true;

            PopulateExtensionComboBox(VideoContainerExtensions, preferredDefault: "mp4");
        }

        /// <summary>
        /// Replaces <see cref="ChangeExtensionComboBox"/>'s items with
        /// <paramref name="extensions"/> and selects <paramref name="preferredDefault"/>
        /// (falling back to the first entry if it isn't present).
        /// </summary>
        private void PopulateExtensionComboBox(IReadOnlyList<string> extensions, string preferredDefault)
        {
            ChangeExtensionComboBox.Items.Clear();

            foreach (var extension in extensions)
            {
                ChangeExtensionComboBox.Items.Add(new ComboBoxItem { Content = extension });
            }

            var items = ChangeExtensionComboBox.Items.Cast<ComboBoxItem>();
            var defaultItem = items.FirstOrDefault(item =>
                string.Equals((string)item.Content, preferredDefault, StringComparison.OrdinalIgnoreCase));

            ChangeExtensionComboBox.SelectedItem = defaultItem ?? ChangeExtensionComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault();
        }

        // ------------------------------------------------------------
        //  OUTPUT FOLDER
        // ------------------------------------------------------------

        private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Selecionar pasta de saída",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                OutputDir.Text = dialog.FolderName;
            }
        }

        // ------------------------------------------------------------
        //  DOWNLOAD ACTION
        // ------------------------------------------------------------

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputLink.Text))
            {
                MessageBox.Show(
                    "Cole o link do vídeo antes de continuar.",
                    "Az Video Downloader",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (string.IsNullOrWhiteSpace(OutputDir.Text))
            {
                MessageBox.Show(
                    "Selecione a pasta de saída antes de continuar.",
                    "Az Video Downloader",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var selectedVideo = VideoFormatListBox.SelectedItem as GetAVFormatList;
            var selectedAudio = AudioFormatListBox.SelectedItem as GetAVFormatList;

            if (selectedVideo is null && !AudioOnlyCheckBox.IsChecked.GetValueOrDefault())
            {
                MessageBox.Show(
                    "Selecione um formato de vídeo antes de continuar.",
                    "Az Video Downloader",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var options = new FfmpegOptions
            {
                AudioOnly = AudioOnlyCheckBox.IsChecked == true,
                MergeAudioVideo = MergeAudioVideoCheckBox.IsChecked == true,
                EmbedThumbnail = EmbedThumbnailCheckBox.IsChecked == true,
                EmbedMetadata = EmbedMetadataCheckBox.IsChecked == true,
                EmbedSubtitles = EmbedSubtitlesCheckBox.IsChecked == true,
                ChangeExtension = ChangeExtensionCheckBox.IsChecked == true,
                TargetExtension = ChangeExtensionComboBox.Text ?? "mp4",
                // Falls back to the source video's extension so ffmpeg
                // argument logic (e.g. subtitle codec selection) still has
                // the correct effective container when ChangeExtension is off.
                SourceExtension = selectedVideo?.Source.Extension ?? "mp4"
            };

            try
            {
                DownloadButton.IsEnabled = false;

                DownloadProgressBar.Minimum = 0;
                DownloadProgressBar.Maximum = 100;
                DownloadProgressBar.Value = 0;

                ProgressPercentText.Text = "0%";

                var progress = new Progress<DownloadProgress>(downloadProgress =>
                {
                    switch (downloadProgress.State)
                    {
                        case DownloadState.Downloading:
                            var percentage = downloadProgress.Progress * 100.0;

                            DownloadProgressBar.Value = percentage;
                            ProgressPercentText.Text = $"{percentage:0}%";
                            break;

                        case DownloadState.PostProcessing:
                            ProgressPercentText.Text = "Processando...";
                            break;

                        case DownloadState.Success:
                            DownloadProgressBar.Value = 100;
                            ProgressPercentText.Text = "100%";
                            break;

                        case DownloadState.Error:
                            ProgressPercentText.Text = "Erro";
                            break;
                    }
                });

                var result = await _videoDownloadService.DownloadAsync(
                    InputLink.Text.Trim(),
                    OutputDir.Text,
                    selectedVideo,
                    selectedAudio,
                    options,
                    progress);

                if (!result.Success)
                {
                    var error = result.ErrorOutput.Length > 0
                        ? string.Join(Environment.NewLine, result.ErrorOutput)
                        : "O download falhou.";

                    MessageBox.Show(
                        error,
                        "Az Video Downloader",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    return;
                }

                DownloadProgressBar.Value = 100;
                ProgressPercentText.Text = "100%";

                MessageBox.Show(
                    "Download concluído com sucesso.",
                    "Az Video Downloader",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                ProgressPercentText.Text = "Cancelado";
            }
            catch (Exception ex)
            {
                ProgressPercentText.Text = "Erro";

                MessageBox.Show(
                    ex.Message,
                    "Az Video Downloader",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                DownloadButton.IsEnabled = true;
            }
        }
    }
}