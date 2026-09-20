using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using YoutubeDLSharp;

using AzVideoDownloader.Helpers;
using AzVideoDownloader.Services.Core;
using AzVideoDownloader.Services.Fetch;

using static AzVideoDownloader.Services.Theming.ThemeManager;
using static AzVideoDownloader.Helpers.UserNotification;

namespace AzVideoDownloader
{
    /// <summary>
    /// Main application window. All UI event handlers are wired in MainWindow.xaml.
    /// This part holds the shared state, the constructor and the link input handlers.
    /// Feature-specific code lives in MainWindow.Download.cs, MainWindow.VideoInfo.cs,
    /// MainWindow.OutputDirectory.cs and MainWindow.PartialDownload.cs.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Constants

        public const string AppTitle = "Az Video Downloader";

        // Delay before fetching video information after the link input changes.
        private static readonly TimeSpan LinkDebounceDelay = TimeSpan.FromMilliseconds(700);

        #endregion

        #region Fields

        // Initialized with null! because the constructor may return early
        // when the bundled tools cannot be extracted (the app shuts down).
        private readonly YoutubeDL _ytdl = null!;
        private readonly GetVideoinfo _videoInfoService = null!;
        private readonly VideoDownloadService _videoDownloadService = null!;

        // Prevents a metadata fetch from being triggered on every keystroke.
        private readonly DebouncedTriggerHelper _linkDebounce = null!;

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

        #region Top Bar: Settings

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow { Owner = this }.ShowDialog();
        }

        #endregion

        #region Shared Helpers

        // Used by more than one MainWindow part (output directories and download options).
        private static bool EqualsIgnoreCase(string? left, string? right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        #endregion
    }
}