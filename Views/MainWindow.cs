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
    /// Main application window. UI event handlers are wired in MainWindow.xaml.
    /// This partial holds the application title, the shared services and the constructor.
    /// Feature-specific code lives in MainWindows.[Feature] e.g. MainWindow.Download.cs.
    /// MainWindow.OutputDirectory.cs, MainWindow.PartialDownload.cs and MainWindow.Download.cs.
    /// </summary>
    public partial class MainWindow : Window
    {
        public const string AppTitle = "Az Video Downloader";

        // Initialized with null! because the constructor returns early (and the app
        // shuts down) when the bundled tools cannot be extracted.
        private readonly GetVideoinfo _videoInfoService = null!;
        private readonly VideoDownloadService _videoDownloadService = null!;

        public MainWindow()
        {
            InitializeComponent();

            ApplySavedTheme();
            LoadRecentOutputDirectories();
            InitializeTitleTracking();

            // The bundled tools (yt-dlp, ffmpeg) must exist before the YoutubeDL instance
            // is created. Doing this here lets extraction failures be reported in the UI.
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

            // Debounced so metadata is fetched only after the user stops typing/pasting.
            _linkDebounce = new DebouncedTrigger(LinkDebounceDelay, OnLinkDebounceElapsed);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow { Owner = this }.ShowDialog();
        }
    }
}