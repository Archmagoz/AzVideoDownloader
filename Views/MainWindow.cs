using System.IO;
using System.Windows;

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
    /// This part holds the application title, the services and the constructor.
    /// Feature-specific code lives in MainWindow.LinkInput.cs, MainWindow.VideoInfo.cs,
    /// MainWindow.OutputDirectory.cs, MainWindow.PartialDownload.cs and MainWindow.Download.cs.
    /// </summary>
    public partial class MainWindow : Window
    {
        public const string AppTitle = "Az Video Downloader";

        // Initialized with null! because the constructor may return early
        // when the bundled tools cannot be extracted (the app shuts down).
        private readonly GetVideoinfo _videoInfoService = null!;
        private readonly VideoDownloadService _videoDownloadService = null!;

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

            var ytdl = new YoutubeDL
            {
                YoutubeDLPath = ToolManagerService.YtDlpPath,
                FFmpegPath = ToolManagerService.FfmpegPath,
                OutputFolder = OutputDir.Text
            };

            _videoInfoService = new GetVideoinfo(ytdl);
            _videoDownloadService = new VideoDownloadService(ytdl);

            // Fetch metadata after the user pauses link input.
            _linkDebounce = new DebouncedTrigger(LinkDebounceDelay, OnLinkDebounceElapsed);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow { Owner = this }.ShowDialog();
        }
    }
}