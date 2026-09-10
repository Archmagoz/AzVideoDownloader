using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

using YoutubeDLSharp;

using AzVideoDownloader.Models;
using AzVideoDownloader.Helpers;

using AzVideoDownloader.Services.Core;
using AzVideoDownloader.Services.Fetch;

using static AzVideoDownloader.Services.Theming.ThemeManager;

namespace AzVideoDownloader
{
    public partial class MainWindow : Window
    {
        #region Fields

        // Services that encapsulate the actual yt-dlp/ffmpeg calls and
        // provide a higher-level API for the UI to consume.
        private readonly YoutubeDL _ytdl = null!;
        private readonly GetVideoinfo _videoInfoService = null!;
        private readonly GetVideoThumbnail _thumbnailService = new();
        private readonly VideoDownloadService _videoDownloadService = null!;

        // Duration (seconds) of the currently loaded video, used to derive
        // an approximate bitrate per selected format.
        private double? _currentVideoDurationSeconds;

        // Time to wait after the user stops typing before fetching video info.
        private readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(700);

        // Debounces link input changes: waits for the user to stop typing before
        // triggering a video info fetch, avoiding a yt-dlp call on every keystroke.
        private readonly DebouncedTriggerHelper _linkDebounce = null!;

        // Cancels a stale in-flight fetch when a newer one supersedes it.
        private CancellationTokenSource? _fetchCts;

        // Container extensions offered by ChangeExtensionComboBox for a
        // regular video download. Kept in sync with the ComboBoxItems
        // declared in MainWindow.xaml so the designer preview matches the
        // runtime default state.
        private static readonly string[] VideoContainerExtensions = YtDlpVideoFormats.UiSelectableLabels;

        // Container extensions offered by ChangeExtensionComboBox once
        // "Somente áudio" is checked. Sourced from YtDlpAudioFormats so this
        // list and the --audio-format mapping (incl. "ogg" -> "vorbis")
        // never drift apart.
        private static readonly string[] AudioContainerExtensions = YtDlpAudioFormats.UiSelectableLabels;

        // Number of recent output directories to keep in the history. The
        // ComboBox is populated with the most recent first, so the oldest
        // entries are dropped when the list exceeds this limit.
        private const int MaxRecentOutputDirectories = 5;

        #endregion

        #region Constructor

        public MainWindow()
        {
            ApplySavedTheme();
            InitializeComponent();
            LoadRecentOutputDirectories();

            // Initialize the bundled tools (yt-dlp, ffmpeg, ffprobe, deno) if they
            // aren't already present in the user's AppData folder. This is
            // done here rather than in the constructor of ToolManagerService
            // so that the MainWindow can show a user-facing error message and
            // exit gracefully if the extraction fails (e.g. antivirus
            // quarantines the binaries).
            try
            {
                ToolManagerService.EnsureToolsExist();
            }
            catch (FileNotFoundException ex)
            {
                ShowPopupForced(ex.Message, "Az Video Downloader",
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

            // Signals callbacks when the user stops typing for a while, so we don't
            // spam yt-dlp with a fetch for every keystroke. The actual fetch
            // is triggered in OnLinkDebounceElapsed, which runs on the UI thread
            // after the debounce delay.
            _linkDebounce = new DebouncedTriggerHelper(DebounceDelay, OnLinkDebounceElapsed);

            // Wire up the events that drive the UI's reactive behavior.
            InputLink.TextChanged += InputLink_TextChanged;
            VideoFormatListBox.SelectionChanged += VideoFormatListBox_SelectionChanged;

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

        #endregion

        #region User Notifications

        /// <summary>
        /// Displays a message box when user popups are enabled in the application settings.
        /// </summary>
        private static void ShowPopup(
            string message,
            string title,
            MessageBoxButton buttons,
            MessageBoxImage image)
        {
            if (!Properties.Settings.Default.ShowPopups)
                return;

            MessageBox.Show(message, title, buttons, image);
        }

        /// <summary>
        /// Displays a message box regardless of the application settings.
        /// </summary>
        private static void ShowPopupForced(
            string message,
            string title,
            MessageBoxButton buttons,
            MessageBoxImage image)
        {
            MessageBox.Show(message, title, buttons, image);
        }

        #endregion

        #region Top Bar Actions

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

        #endregion

        #region Video Info Fetch

        // Orchestrates VideoInfoService + ThumbnailService and pushes
        // the results into the UI controls.

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

        #endregion

        #region Video Format Selection

        // Updates the info panel (fps/resolution/bitrate/size) whenever
        // the user picks a different video format from the list.

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

        #endregion

        #region Yt-Dlp Options

        // "Somente áudio" changes what the other options mean: merging
        // separate streams and embedding subtitles no longer apply, and
        // the output container should be an audio format.

        private void AudioOnlyCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // Merging audio+video streams and embedding subtitles are
            // meaningless once we're extracting audio only - disable both
            // (and clear their checked state) so a stale IsChecked=true
            // can't leak into YtDlpOptions while the controls are hidden
            // from interaction.
            MergeAudioVideoCheckBox.IsEnabled = false;
            MergeAudioVideoCheckBox.IsChecked = false;

            EmbedSubtitlesCheckBox.IsEnabled = false;
            EmbedSubtitlesCheckBox.IsChecked = false;

            // Picking a video format is meaningless once we're only
            // extracting audio - grey the list out so it visually reads as
            // "not applicable" rather than just quietly being ignored at
            // download time.
            VideoFormatListBox.IsEnabled = false;

            PopulateExtensionComboBox(AudioContainerExtensions, preferredDefault: "mp3");
        }

        private void AudioOnlyCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // Restore the default "merge" workflow; subtitles stay
            // unchecked since that was its original default state too.
            MergeAudioVideoCheckBox.IsEnabled = true;
            MergeAudioVideoCheckBox.IsChecked = true;

            EmbedSubtitlesCheckBox.IsEnabled = true;

            VideoFormatListBox.IsEnabled = true;

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

        #endregion

        #region Output Folder

        /// <summary>
        /// Opens the folder browser and sets the selected output directory.
        /// </summary>
        private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Selecionar pasta de saída",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            SetOutputDirectory(dialog.FolderName, addToHistory: true);
        }

        /// <summary>
        /// Sets the active output directory and optionally updates the recent
        /// directory history.
        /// </summary>
        private void SetOutputDirectory(string directory, bool addToHistory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;

            if (addToHistory)
            {
                AddRecentOutputDirectory(directory);

                // Select the directory after the ComboBox has been populated.
                OutputDir.SelectedItem = directory;
                return;
            }

            OutputDir.SelectedItem = directory;
        }

        /// <summary>
        /// Adds a directory to the recent output history, moving existing entries
        /// to the top and keeping the history limited to the configured maximum.
        /// </summary>
        private void AddRecentOutputDirectory(string directory)
        {
            var directories = GetRecentOutputDirectories();

            directories.RemoveAll(path =>
                string.Equals(path, directory, StringComparison.OrdinalIgnoreCase));

            directories.Insert(0, directory);

            if (directories.Count > MaxRecentOutputDirectories)
            {
                directories.RemoveRange(
                    MaxRecentOutputDirectories,
                    directories.Count - MaxRecentOutputDirectories);
            }

            SaveRecentOutputDirectories(directories);
            PopulateRecentOutputDirectories(directories);
        }

        /// <summary>
        /// Loads the persisted recent output directories and removes entries
        /// that no longer exist.
        /// </summary>
        private void LoadRecentOutputDirectories()
        {
            var directories = GetRecentOutputDirectories()
                .Where(Directory.Exists)
                .ToList();

            SaveRecentOutputDirectories(directories);
            PopulateRecentOutputDirectories(directories);

            if (directories.Count > 0)
                OutputDir.SelectedItem = directories[0];
        }

        /// <summary>
        /// Returns the recent output directories stored in application settings.
        /// </summary>
        private static List<string> GetRecentOutputDirectories()
        {
            var stored = Properties.Settings.Default.RecentOutputDirectories;

            if (string.IsNullOrWhiteSpace(stored))
                return [];

            return [.. stored
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRecentOutputDirectories)];
        }

        /// <summary>
        /// Persists the recent output directories in application settings.
        /// Windows paths cannot contain the pipe character, so it is safe
        /// to use it as the separator.
        /// </summary>
        private static void SaveRecentOutputDirectories(IEnumerable<string> directories)
        {
            Properties.Settings.Default.RecentOutputDirectories =
                string.Join("|", directories);

            Properties.Settings.Default.Save();
        }

        /// <summary>
        /// Refreshes the ComboBox items from the supplied directory history.
        /// </summary>
        private void PopulateRecentOutputDirectories(IEnumerable<string> directories)
        {
            OutputDir.Items.Clear();

            foreach (var directory in directories)
            {
                OutputDir.Items.Add(directory);
            }
        }

        /// <summary>
        /// Handles manual selection from the recent-directory dropdown.
        /// The selected directory becomes the active output directory and is
        /// moved to the top of the history.
        /// </summary>
        private void OutputDir_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OutputDir.SelectedItem is not string directory)
                return;

            var directories = GetRecentOutputDirectories();

            // The selected directory is already part of the history.
            // Move it to the top without rebuilding the ComboBox.
            directories.RemoveAll(path =>
                string.Equals(path, directory, StringComparison.OrdinalIgnoreCase));

            directories.Insert(0, directory);

            SaveRecentOutputDirectories(directories);

            if (OutputDir.Items.Count > 0 &&
                !string.Equals(OutputDir.Items[0] as string, directory, StringComparison.OrdinalIgnoreCase))
            {
                PopulateRecentOutputDirectories(directories);
                OutputDir.SelectedItem = directory;
            }
        }

        #endregion

        #region Settings Window

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow
            {
                Owner = this
            };

            settingsWindow.ShowDialog();
        }

        #endregion

        #region Download Action

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputLink.Text))
            {
                ShowPopup(
                    "Cole o link do vídeo antes de continuar.",
                    "Az Video Downloader",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (string.IsNullOrWhiteSpace(OutputDir.Text))
            {
                ShowPopup(
                    "Selecione a pasta de saída antes de continuar.",
                    "Az Video Downloader",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var isAudioOnly = AudioOnlyCheckBox.IsChecked == true;

            var selectedVideo = VideoFormatListBox.SelectedItem as GetAVFormatList;
            var selectedAudio = AudioFormatListBox.SelectedItem as GetAVFormatList;

            // Only block on "no video format selected" when there actually
            // were formats to choose from. An empty list (site/video with
            // no per-format listing, or a fetch that returned nothing)
            // isn't a user mistake - VideoDownloadService.BuildVideoFormatSelector
            // falls back to a default yt-dlp selector in that case.
            var hasVideoFormatsAvailable = VideoFormatListBox.ItemsSource is IReadOnlyCollection<GetAVFormatList> videoFormats
                && videoFormats.Count > 0;

            if (selectedVideo is null && !isAudioOnly && hasVideoFormatsAvailable)
            {
                ShowPopup(
                    "Selecione um formato de vídeo antes de continuar.",
                    "Az Video Downloader",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            // NOTE: audio-only downloads no longer go through a manual
            // ffmpeg "-vn" call. yt-dlp's own "-x" extracts the best
            // available audio for us (picking a real audio-only stream
            // when the site offers one, instead of assuming a combined
            // video+audio file was already downloaded), and
            // "--audio-format" handles the conversion - see
            // YtDlpArgumentBuilderService. This is also why the previous
            // "somente áudio não funciona" symptom should be gone: before,
            // a video format still had to be selected/downloaded for "-vn"
            // to have anything to strip.
            var options = new YtDlpOptions
            {
                AudioOnly = isAudioOnly,
                AudioFormat = isAudioOnly ? (ChangeExtensionComboBox.Text ?? "mp3") : "mp3",

                VideoFormatId = selectedVideo?.Source.FormatId,
                AudioFormatId = selectedAudio?.Source.FormatId,
                MergeAudioVideo = MergeAudioVideoCheckBox.IsChecked == true,

                EmbedThumbnail = EmbedThumbnailCheckBox.IsChecked == true,
                EmbedMetadata = EmbedMetadataCheckBox.IsChecked == true,
                EmbedSubtitles = EmbedSubtitlesCheckBox.IsChecked == true,

                ChangeExtension = ChangeExtensionCheckBox.IsChecked == true,
                TargetContainer = !isAudioOnly ? (ChangeExtensionComboBox.Text ?? "mp4") : "mp4",
                // Falls back to the source video's extension so the
                // effective container is still correct when ChangeExtension
                // is off (e.g. an unmodified webm source stays webm).
                SourceContainer = selectedVideo?.Source.Extension ?? "mp4"
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

                    ShowPopupForced(
                        error,
                        "Az Video Downloader",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    return;
                }

                DownloadProgressBar.Value = 100;
                ProgressPercentText.Text = "100%";

                ShowPopup(
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

                ShowPopupForced(
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

        #endregion
    }
}