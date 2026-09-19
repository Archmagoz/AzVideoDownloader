using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using Microsoft.Win32;
using YoutubeDLSharp;

using AzVideoDownloader.Helpers;
using AzVideoDownloader.Models;
using AzVideoDownloader.Services.Core;
using AzVideoDownloader.Services.Fetch;

using static AzVideoDownloader.Services.Theming.ThemeManager;

namespace AzVideoDownloader
{
    /// <summary>
    /// Main application window. All UI event handlers are wired in MainWindow.xaml.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Constants

        private const string AppTitle = "Az Video Downloader";

        // Maximum number of output directories retained in history.
        private const int MaxRecentOutputDirectories = 5;

        // Number of digits in the fixed HH:mm:ss timestamp representation.
        private const int TimestampDigitCount = 6;

        // Delay before fetching video information after the link input changes.
        private static readonly TimeSpan LinkDebounceDelay = TimeSpan.FromMilliseconds(700);

        // Container extensions available for video and audio-only downloads.
        private static readonly string[] VideoContainerExtensions = YtDlpVideoFormats.UiSelectableLabels;
        private static readonly string[] AudioContainerExtensions = YtDlpAudioFormats.UiSelectableLabels;

        #endregion

        #region Fields

        // Initialized with null! because the constructor may return early
        // when the bundled tools cannot be extracted (the app shuts down).
        private readonly YoutubeDL _ytdl = null!;
        private readonly GetVideoinfo _videoInfoService = null!;
        private readonly VideoDownloadService _videoDownloadService = null!;

        // Prevents a metadata fetch from being triggered on every keystroke.
        private readonly DebouncedTriggerHelper _linkDebounce = null!;

        // Cancels a metadata request when a newer request supersedes it.
        private CancellationTokenSource? _fetchCts;

        // Duration of the current video, used for bitrate estimation and range validation.
        private double? _currentVideoDurationSeconds;

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
                ShowPopupForced(ex.Message, MessageBoxImage.Error);
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
            _linkDebounce = new DebouncedTriggerHelper(LinkDebounceDelay, OnLinkDebounceElapsed);
        }

        #endregion

        #region User Notifications

        /// <summary>
        /// Displays an OK message box when user popups are enabled in the application settings.
        /// </summary>
        private static void ShowPopup(
            string message,
            MessageBoxImage image,
            string title = AppTitle)
        {
            if (!Properties.Settings.Default.ShowPopups)
                return;

            ShowPopupForced(message, image, title);
        }

        /// <summary>
        /// Displays an OK message box regardless of the application settings.
        /// </summary>
        private static void ShowPopupForced(
            string message,
            MessageBoxImage image,
            string title = AppTitle)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, image);
        }

        #endregion

        #region Top Bar: Link Input

        private void PasteLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsText())
                return;

            InputLink.Text = Clipboard.GetText().Trim();
            _linkDebounce.TriggerNow();
        }

        private void InputLink_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            // Pasting occurs before TextBox.Text is updated, so defer the trigger
            // until WPF has applied the pasted value.
            Dispatcher.BeginInvoke(
                new Action(_linkDebounce.TriggerNow),
                DispatcherPriority.Background);
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

        private void OnLinkDebounceElapsed() =>
            _ = FetchVideoInfoAsync(InputLink.Text.Trim());

        #endregion

        #region Top Bar: Output Folder

        private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Selecionar pasta de saída",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
                SelectOutputDirectory(dialog.FolderName);
        }

        /// <summary>
        /// Adds an existing directory to the history and selects it.
        /// </summary>
        private void SelectOutputDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;

            AddRecentOutputDirectory(directory);

            // Select after the ComboBox items have been refreshed.
            OutputDir.SelectedItem = directory;
        }

        /// <summary>
        /// Activates the selected directory and moves it to the top of the history.
        /// </summary>
        private void OutputDir_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OutputDir.SelectedItem is not string directory)
                return;

            var directories = MoveToFront(GetRecentOutputDirectories(), directory);
            SaveRecentOutputDirectories(directories);

            // Rebuild the ComboBox only when the order is out of sync with the history.
            if (OutputDir.Items.Count > 0 &&
                !EqualsIgnoreCase(OutputDir.Items[0] as string, directory))
            {
                PopulateRecentOutputDirectories(directories);
                OutputDir.SelectedItem = directory;
            }
        }

        /// <summary>
        /// Adds a directory to the history, trimmed to the maximum size, and refreshes the ComboBox.
        /// </summary>
        private void AddRecentOutputDirectory(string directory)
        {
            var directories = MoveToFront(GetRecentOutputDirectories(), directory);

            if (directories.Count > MaxRecentOutputDirectories)
                directories.RemoveRange(
                    MaxRecentOutputDirectories,
                    directories.Count - MaxRecentOutputDirectories);

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
            Properties.Settings.Default.RecentOutputDirectories = string.Join("|", directories);
            Properties.Settings.Default.Save();
        }

        private void PopulateRecentOutputDirectories(IEnumerable<string> directories)
        {
            OutputDir.Items.Clear();

            foreach (var directory in directories)
                OutputDir.Items.Add(directory);
        }

        /// <summary>
        /// Removes any existing occurrence of a directory and inserts it at the top of the list.
        /// </summary>
        private static List<string> MoveToFront(List<string> directories, string directory)
        {
            directories.RemoveAll(path => EqualsIgnoreCase(path, directory));
            directories.Insert(0, directory);

            return directories;
        }

        private static bool EqualsIgnoreCase(string? left, string? right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        #endregion

        #region Top Bar: Settings

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow { Owner = this }.ShowDialog();
        }

        #endregion

        #region Video Info Fetch

        /// <summary>
        /// Fetches video metadata and thumbnail, then updates the UI.
        /// Only the most recent request is allowed to update the UI.
        /// </summary>
        private async Task FetchVideoInfoAsync(string url)
        {
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
                    ThumbPlaceholderText.Visibility =
                        thumbnail is null
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                }
            }
            catch (OperationCanceledException)
            {
                // A newer request has replaced this one.
            }
            catch (Exception)
            {
                // Treat unresolved links as an empty metadata state.
                if (!cts.Token.IsCancellationRequested)
                    ResetToDefaultState();
            }
            finally
            {
                if (!cts.Token.IsCancellationRequested)
                    SetFetchingState(false);
            }
        }

        private void ApplyVideoInfo(VideoInfoResult info)
        {
            _currentVideoDurationSeconds = info.DurationSeconds;

            VideoTitleText.Text = info.Title;

            VideoDurationText.Text =
                info.DurationSeconds.HasValue
                    ? TimeSpan.FromSeconds(info.DurationSeconds.Value).ToString(@"hh\:mm\:ss")
                    : "—";

            SetTimestampTextBoxValue(DownloadStartTextBox, TimeSpan.Zero);
            SetTimestampTextBoxValue(
                DownloadEndTextBox,
                info.DurationSeconds.HasValue
                    ? TimeSpan.FromSeconds(info.DurationSeconds.Value)
                    : TimeSpan.Zero);

            VideoFormatListBox.ItemsSource = info.VideoFormats;
            AudioFormatListBox.ItemsSource = info.AudioFormats;

            // Prefer formats compatible with the default output containers
            // to avoid unnecessary transcoding.
            VideoFormatListBox.SelectedItem = SelectPreferredFormat(info.VideoFormats, "mp4");
            AudioFormatListBox.SelectedItem = SelectPreferredFormat(info.AudioFormats, "m4a");
        }

        /// <summary>
        /// Returns the first format matching the preferred container, or the first
        /// available format when no match exists.
        /// </summary>
        private static GetAVFormatList? SelectPreferredFormat(
            IReadOnlyList<GetAVFormatList> formats,
            string preferredExtension)
        {
            if (formats.Count == 0)
                return null;

            return formats.FirstOrDefault(format =>
                       string.Equals(
                           format.Source.Extension,
                           preferredExtension,
                           StringComparison.OrdinalIgnoreCase))
                   ?? formats[0];
        }

        private void SetFetchingState(bool isFetching)
        {
            var loadingVisibility =
                isFetching
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            DownloadButton.IsEnabled = !isFetching;

            ThumbPlaceholderText.Text =
                isFetching
                    ? "Carregando..."
                    : "Pré-visualização";

            VideoFormatsLoadingIndicator.Visibility = loadingVisibility;
            AudioFormatsLoadingIndicator.Visibility = loadingVisibility;

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

            SetTimestampTextBoxValue(DownloadStartTextBox, TimeSpan.Zero);
            SetTimestampTextBoxValue(DownloadEndTextBox, TimeSpan.Zero);

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

        /// <summary>
        /// Updates the format details panel when the selected video format changes.
        /// </summary>
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

            var format = item.Source;

            VideoFpsText.Text =
                format.FrameRate.HasValue
                    ? $"{format.FrameRate:0}"
                    : "—";

            VideoResolutionText.Text =
                format.Width.HasValue && format.Height.HasValue
                    ? $"{format.Width}x{format.Height}"
                    : "—";

            var sizeBytes = format.FileSize ?? format.ApproximateFileSize;

            VideoSizeText.Text =
                sizeBytes.HasValue
                    ? $"{sizeBytes.Value / 1024.0 / 1024.0:0.#} MB"
                    : "—";

            // Estimate bitrate from file size and duration.
            VideoBitrateText.Text =
                sizeBytes.HasValue && _currentVideoDurationSeconds is > 0
                    ? $"{sizeBytes.Value * 8 / _currentVideoDurationSeconds.Value / 1000:0} kbps (aprox.)"
                    : "—";
        }

        #endregion

        #region Download Options

        /// <summary>
        /// Audio-only mode disables video-specific options and switches to audio containers.
        /// </summary>
        private void AudioOnlyCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // These options are not applicable to audio-only downloads.
            MergeAudioVideoCheckBox.IsEnabled = false;
            MergeAudioVideoCheckBox.IsChecked = false;

            EmbedSubtitlesCheckBox.IsEnabled = false;
            EmbedSubtitlesCheckBox.IsChecked = false;

            VideoFormatListBox.IsEnabled = false;

            PopulateExtensionComboBox(AudioContainerExtensions, preferredDefault: "mp3");
        }

        /// <summary>
        /// Restores the default video download workflow.
        /// </summary>
        private void AudioOnlyCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            MergeAudioVideoCheckBox.IsEnabled = true;
            MergeAudioVideoCheckBox.IsChecked = true;

            EmbedSubtitlesCheckBox.IsEnabled = true;

            VideoFormatListBox.IsEnabled = true;

            PopulateExtensionComboBox(VideoContainerExtensions, preferredDefault: "mp4");
        }

        /// <summary>
        /// Populates the output container selector and selects the preferred default,
        /// falling back to the first item when the default is not available.
        /// </summary>
        private void PopulateExtensionComboBox(
            IReadOnlyList<string> extensions,
            string preferredDefault)
        {
            ChangeExtensionComboBox.Items.Clear();

            foreach (var extension in extensions)
                ChangeExtensionComboBox.Items.Add(new ComboBoxItem { Content = extension });

            var items = ChangeExtensionComboBox.Items.Cast<ComboBoxItem>().ToList();

            ChangeExtensionComboBox.SelectedItem =
                items.FirstOrDefault(item => EqualsIgnoreCase((string)item.Content, preferredDefault))
                ?? items.FirstOrDefault();
        }

        #endregion

        #region Partial Download

        // Timestamps are edited as a fixed six-digit HH:mm:ss value stored in TextBox.Tag.
        // New digits shift in from the right, like a calculator or a stopwatch input.

        /// <summary>
        /// Synchronizes the digit state with the displayed text when the control receives focus.
        /// </summary>
        private void DownloadTimestampTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            textBox.Tag = GetTimestampDigits(textBox);
            textBox.CaretIndex = textBox.Text.Length;
        }

        /// <summary>
        /// Shifts typed digits into the timestamp and ignores every other character.
        /// </summary>
        private void DownloadTimestampTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = true;

            if (sender is not TextBox textBox)
                return;

            var digits = new string(e.Text.Where(char.IsDigit).ToArray());

            if (digits.Length > 0)
                SetTimestampDigits(textBox, GetTimestampDigits(textBox) + digits);
        }

        /// <summary>
        /// Handles deletion by shifting the timestamp digits toward zero.
        /// </summary>
        private void DownloadTimestampTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox || e.Key is not (Key.Back or Key.Delete))
                return;

            SetTimestampDigits(textBox, "0" + GetTimestampDigits(textBox)[..(TimestampDigitCount - 1)]);
            e.Handled = true;
        }

        /// <summary>
        /// Replaces the timestamp with the last six digits found in the pasted text.
        /// </summary>
        private void DownloadTimestampTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox textBox ||
                !e.SourceDataObject.GetDataPresent(DataFormats.Text))
            {
                return;
            }

            var value = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            var digits = new string(value.Where(char.IsDigit).ToArray());

            if (digits.Length > 0)
                SetTimestampDigits(textBox, digits);

            // The text is always applied manually; never let WPF paste it as-is.
            e.CancelCommand();
        }

        /// <summary>
        /// Updates a timestamp TextBox from a TimeSpan value (hours are capped at 99).
        /// </summary>
        private static void SetTimestampTextBoxValue(TextBox textBox, TimeSpan value)
        {
            var totalHours = Math.Min((int)value.TotalHours, 99);

            SetTimestampDigits(textBox, $"{totalHours:00}{value.Minutes:00}{value.Seconds:00}");
        }

        /// <summary>
        /// Gets the normalized six-digit representation of a timestamp TextBox.
        /// </summary>
        private static string GetTimestampDigits(TextBox textBox)
        {
            if (textBox.Tag is string digits &&
                digits.Length == TimestampDigitCount &&
                digits.All(char.IsDigit))
            {
                return digits;
            }

            return new string(textBox.Text.Where(char.IsDigit).ToArray())
                .PadLeft(TimestampDigitCount, '0')[^TimestampDigitCount..];
        }

        /// <summary>
        /// Normalizes the input to its last six digits (left-padded with zeros),
        /// stores it in Tag and renders it as HH:mm:ss.
        /// </summary>
        private static void SetTimestampDigits(TextBox textBox, string digits)
        {
            digits = new string(digits.Where(char.IsDigit).TakeLast(TimestampDigitCount).ToArray())
                .PadLeft(TimestampDigitCount, '0');

            textBox.Tag = digits;
            textBox.Text = $"{digits[..2]}:{digits[2..4]}:{digits[4..6]}";
            textBox.CaretIndex = textBox.Text.Length;
        }

        /// <summary>
        /// Parses a user-entered timestamp into seconds.
        /// </summary>
        private static bool TryParseTimestamp(string value, out double seconds)
        {
            seconds = 0;

            if (!TimeSpan.TryParse(value, out var timestamp) || timestamp < TimeSpan.Zero)
                return false;

            seconds = timestamp.TotalSeconds;
            return true;
        }

        /// <summary>
        /// Builds the partial download range from the current UI values.
        /// Returns false when the values are invalid or outside the video duration.
        /// </summary>
        private bool TryGetDownloadRange(out double startSeconds, out double endSeconds)
        {
            endSeconds = 0;

            return TryParseTimestamp(DownloadStartTextBox.Text, out startSeconds)
                && TryParseTimestamp(DownloadEndTextBox.Text, out endSeconds)
                && endSeconds > startSeconds
                && !(_currentVideoDurationSeconds is double duration && endSeconds > duration);
        }

        #endregion

        #region Download Action

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputLink.Text))
            {
                ShowPopup("Cole o link do vídeo antes de continuar.", MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputDir.Text))
            {
                ShowPopup("Selecione a pasta de saída antes de continuar.", MessageBoxImage.Warning);
                return;
            }

            var isAudioOnly = AudioOnlyCheckBox.IsChecked == true;
            var selectedVideo = VideoFormatListBox.SelectedItem as GetAVFormatList;
            var selectedAudio = AudioFormatListBox.SelectedItem as GetAVFormatList;

            // Require an explicit video selection only when formats are available.
            // The download service provides a default selector for empty format lists.
            var hasVideoFormats =
                VideoFormatListBox.ItemsSource is IReadOnlyCollection<GetAVFormatList> { Count: > 0 };

            if (selectedVideo is null && !isAudioOnly && hasVideoFormats)
            {
                ShowPopup("Selecione um formato de vídeo antes de continuar.", MessageBoxImage.Warning);
                return;
            }

            double? startSeconds = null;
            double? endSeconds = null;

            if (DownloadPartialCheckBox.IsChecked == true)
            {
                if (!TryGetDownloadRange(out var start, out var end))
                {
                    ShowPopup(
                        "Informe um intervalo de tempo válido dentro da duração do vídeo.",
                        MessageBoxImage.Warning,
                        title: "Intervalo inválido");
                    return;
                }

                startSeconds = start;
                endSeconds = end;
            }

            var options = BuildDownloadOptions(
                isAudioOnly,
                selectedVideo,
                selectedAudio,
                startSeconds,
                endSeconds);

            try
            {
                DownloadButton.IsEnabled = false;
                DownloadProgressBar.Value = 0;
                ProgressPercentText.Text = "0%";

                var result = await _videoDownloadService.DownloadAsync(
                    InputLink.Text.Trim(),
                    OutputDir.Text,
                    selectedVideo,
                    selectedAudio,
                    options,
                    new Progress<DownloadProgress>(OnDownloadProgress));

                if (!result.Success)
                {
                    var error =
                        result.ErrorOutput.Length > 0
                            ? string.Join(Environment.NewLine, result.ErrorOutput)
                            : "O download falhou.";

                    ShowPopupForced(error, MessageBoxImage.Error);
                    return;
                }

                DownloadProgressBar.Value = 100;
                ProgressPercentText.Text = "100%";

                ShowPopup("Download concluído com sucesso.", MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                ProgressPercentText.Text = "Cancelado";
            }
            catch (Exception ex)
            {
                ProgressPercentText.Text = "Erro";
                ShowPopupForced(ex.Message, MessageBoxImage.Error);
            }
            finally
            {
                DownloadButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Maps the current UI state to yt-dlp options.
        /// Audio extraction and conversion are handled by yt-dlp through -x and
        /// --audio-format; no manual ffmpeg -vn step is required.
        /// </summary>
        private YtDlpOptions BuildDownloadOptions(
            bool isAudioOnly,
            GetAVFormatList? selectedVideo,
            GetAVFormatList? selectedAudio,
            double? startSeconds,
            double? endSeconds) =>
            new()
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
                TargetContainer = isAudioOnly ? "mp4" : (ChangeExtensionComboBox.Text ?? "mp4"),

                // Preserve the source container when extension conversion is disabled.
                SourceContainer = selectedVideo?.Source.Extension ?? "mp4",

                DownloadPartial = DownloadPartialCheckBox.IsChecked == true,
                DownloadStartSeconds = startSeconds,
                DownloadEndSeconds = endSeconds
            };

        /// <summary>
        /// Reflects the yt-dlp download state in the progress bar and label.
        /// </summary>
        private void OnDownloadProgress(DownloadProgress progress)
        {
            switch (progress.State)
            {
                case DownloadState.Downloading:
                    var percentage = progress.Progress * 100.0;
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
        }

        #endregion
    }
}