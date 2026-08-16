using Microsoft.Win32; // OpenFolderDialog (available on .NET 8+ / WPF)
using System.IO;
using System.Windows;
using System.Windows.Controls;

using YoutubeDLSharp;

using AzVideoDownloader.Services;

namespace AzVideoDownloader
{
    public partial class MainWindow : Window
    {
        // ------------------------------------------------------------
        //  SERVICES
        // ------------------------------------------------------------
        private readonly VideoInfoService _videoInfoService = null!;
        private readonly VideoDownloadService _videoDownloadService = null!;
        private readonly DebouncedTrigger _linkDebounce = null!;
        private readonly ThumbnailService _thumbnailService = new();

        // Cancels a stale in-flight fetch when a newer one supersedes it.
        private CancellationTokenSource? _fetchCts;

        // Duration (seconds) of the currently loaded video, used to derive
        // an approximate bitrate per selected format.
        private double? _currentVideoDurationSeconds;

        // Provides the actual yt-dlp/ffmpeg functionality.
        private readonly YoutubeDL _ytdl = null!;

        // Time to wait after the user stops typing before fetching video info.
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(700);

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                YtDlpToolManager.EnsureToolsExist();
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
                YoutubeDLPath = YtDlpToolManager.YtDlpPath,
                FFmpegPath = YtDlpToolManager.FfmpegPath,
                OutputFolder = OutputDir.Text
            };

            _videoInfoService = new VideoInfoService(_ytdl);
            _videoDownloadService = new VideoDownloadService(_ytdl);

            _linkDebounce = new DebouncedTrigger(DebounceDelay, OnLinkDebounceElapsed);

            VideoFormatListBox.SelectionChanged += VideoFormatListBox_SelectionChanged;
            InputLink.TextChanged += InputLink_TextChanged;

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
            VideoFormatListBox.DisplayMemberPath = nameof(FormatListItem.Display);

            AudioFormatListBox.ItemsSource = info.AudioFormats;
            AudioFormatListBox.DisplayMemberPath = nameof(FormatListItem.Display);

            if (info.VideoFormats.Count > 0) VideoFormatListBox.SelectedIndex = 0;
            if (info.AudioFormats.Count > 0) AudioFormatListBox.SelectedIndex = 0;
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
            if (VideoFormatListBox.SelectedItem is not FormatListItem item)
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

            var selectedVideo = VideoFormatListBox.SelectedItem as FormatListItem;
            var selectedAudio = AudioFormatListBox.SelectedItem as FormatListItem;

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
                TargetExtension = ChangeExtensionComboBox.Text ?? "mp4"
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