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

        // Services used by the UI to fetch metadata and execute downloads.
        private readonly YoutubeDL _ytdl = null!;
        private readonly GetVideoinfo _videoInfoService = null!;
        private readonly VideoDownloadService _videoDownloadService = null!;

        // Duration of the current video, used for bitrate estimation.
        private double? _currentVideoDurationSeconds;

        // Delay before fetching video information after link input changes.
        private readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(700);

        // Prevents a metadata fetch from being triggered on every keystroke.
        private readonly DebouncedTriggerHelper _linkDebounce = null!;

        // Cancels a metadata request when a newer request supersedes it.
        private CancellationTokenSource? _fetchCts;

        // Container extensions available for video downloads.
        private static readonly string[] VideoContainerExtensions = YtDlpVideoFormats.UiSelectableLabels;

        // Container extensions available for audio-only downloads.
        private static readonly string[] AudioContainerExtensions = YtDlpAudioFormats.UiSelectableLabels;

        // Maximum number of output directories retained in history.
        private const int MaxRecentOutputDirectories = 5;

        #endregion

        #region Constructor

        public MainWindow()
        {
            ApplySavedTheme();
            InitializeComponent();
            LoadRecentOutputDirectories();

            // Initialize bundled tools before creating the YoutubeDL instance.
            // Keeping this here allows tool extraction failures to be reported to the UI.
            try
            {
                ToolManagerService.EnsureToolsExist();
            }
            catch (FileNotFoundException ex)
            {
                ShowPopupForced(
                    ex.Message,
                    "Az Video Downloader",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

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

            // Fetch metadata after the user pauses link input.
            _linkDebounce = new DebouncedTriggerHelper(DebounceDelay, OnLinkDebounceElapsed);

            // Register UI event handlers.
            InputLink.TextChanged += InputLink_TextChanged;
            VideoFormatListBox.SelectionChanged += VideoFormatListBox_SelectionChanged;

            // Keep video-only controls synchronized with the audio-only state.
            AudioOnlyCheckBox.Checked += AudioOnlyCheckBox_Checked;
            AudioOnlyCheckBox.Unchecked += AudioOnlyCheckBox_Unchecked;

            // Trigger an immediate fetch after pasted text is inserted.
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
            // Pasting occurs before TextBox.Text is updated, so defer the trigger
            // until WPF has applied the pasted value.
            Dispatcher.BeginInvoke(new Action(_linkDebounce.TriggerNow),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void InputLink_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputLink.Text))
            {
                // Clear the UI immediately when the link is empty.
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

        // Fetches video metadata and thumbnail, then updates the UI.

        private async Task FetchVideoInfoAsync(string url)
        {
            // Only the most recent request is allowed to update the UI.
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
                    return;

                if (info is null)
                {
                    ResetToDefaultState();
                    return;
                }

                ApplyVideoInfo(info);

                var thumbnail = await GetVideoThumbnail.LoadAsync(info.ThumbnailUrl);
                if (!cts.Token.IsCancellationRequested)
                {
                    ThumbPreview.Source = thumbnail;
                    ThumbPlaceholderText.Visibility = thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (OperationCanceledException)
            {
                // A newer request has replaced this one.
            }
            catch (Exception)
            {
                if (!cts.Token.IsCancellationRequested)
                {
                    // Treat unresolved links as an empty metadata state.
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

            // Prefer formats compatible with the default output containers
            // to avoid unnecessary transcoding.
            VideoFormatListBox.SelectedItem = SelectPreferredFormat(info.VideoFormats, preferredExtension: "mp4");
            AudioFormatListBox.SelectedItem = SelectPreferredFormat(info.AudioFormats, preferredExtension: "m4a");
        }

        /// <summary>
        /// Selects the first format matching the preferred container, or the first
        /// available format when no match exists.
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
        /// Resets the video information panel to its default state.
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

        // Update format details when the selected video format changes.

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

            // Estimate bitrate from file size and duration.
            VideoBitrateText.Text = (sizeBytes.HasValue && _currentVideoDurationSeconds is > 0)
                ? $"{sizeBytes.Value * 8 / _currentVideoDurationSeconds.Value / 1000:0} kbps (aprox.)"
                : "—";
        }

        #endregion

        #region Yt-Dlp Options

        // Audio-only mode disables video-specific options and uses audio containers.

        private void AudioOnlyCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // These options are not applicable to audio-only downloads.
            MergeAudioVideoCheckBox.IsEnabled = false;
            MergeAudioVideoCheckBox.IsChecked = false;

            EmbedSubtitlesCheckBox.IsEnabled = false;
            EmbedSubtitlesCheckBox.IsChecked = false;

            // Video format selection is not applicable in audio-only mode.
            VideoFormatListBox.IsEnabled = false;

            PopulateExtensionComboBox(AudioContainerExtensions, preferredDefault: "mp3");
        }

        private void AudioOnlyCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // Restore the default video download workflow.
            MergeAudioVideoCheckBox.IsEnabled = true;
            MergeAudioVideoCheckBox.IsChecked = true;

            EmbedSubtitlesCheckBox.IsEnabled = true;

            VideoFormatListBox.IsEnabled = true;

            PopulateExtensionComboBox(VideoContainerExtensions, preferredDefault: "mp4");
        }

        /// <summary>
        /// Populates the output container selector and selects the preferred default.
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

        private void SetOutputDirectory(string directory, bool addToHistory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;

            if (addToHistory)
            {
                AddRecentOutputDirectory(directory);

                // Select after refreshing the ComboBox.
                OutputDir.SelectedItem = directory;
                return;
            }

            OutputDir.SelectedItem = directory;
        }

        /// <summary>
        /// Adds a directory to history and moves it to the most recent position.
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
        /// Loads persisted directories and removes paths that no longer exist.
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

        private static void SaveRecentOutputDirectories(IEnumerable<string> directories)
        {
            Properties.Settings.Default.RecentOutputDirectories =
                string.Join("|", directories);

            Properties.Settings.Default.Save();
        }

        private void PopulateRecentOutputDirectories(IEnumerable<string> directories)
        {
            OutputDir.Items.Clear();

            foreach (var directory in directories)
            {
                OutputDir.Items.Add(directory);
            }
        }

        /// <summary>
        /// Activates the selected directory and moves it to the top of history.
        /// </summary>
        private void OutputDir_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OutputDir.SelectedItem is not string directory)
                return;

            var directories = GetRecentOutputDirectories();

            // Move the selected entry to the top without rebuilding the ComboBox.
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

            // Require an explicit video selection only when formats are available.
            // The download service provides a default selector for empty format lists.
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

            // yt-dlp handles audio extraction and conversion through -x and
            // --audio-format; no manual ffmpeg -vn step is required.
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
                // Preserve the source container when extension conversion is disabled.
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