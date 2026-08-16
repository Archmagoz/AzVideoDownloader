using Microsoft.Win32; // OpenFolderDialog (available on .NET 8+ / WPF)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using YoutubeDLSharp;

using AzVideoDownloader.Services;

namespace AzVideoDownloader
{
    public partial class MainWindow : Window
    {
        private readonly YoutubeDL _ytdl;

        // Guards against overlapping fetches (e.g. user hits Enter twice fast)
        private bool _isFetchingVideoInfo;

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

            InputLink.KeyDown += InputLink_KeyDown;
        }

        // ------------------------------------------------------------
        //  TOP BAR ACTIONS
        // ------------------------------------------------------------

        private async void PasteLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                InputLink.Text = Clipboard.GetText().Trim();
                await FetchVideoInfoAsync(InputLink.Text);
            }
        }

        private async void InputLink_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await FetchVideoInfoAsync(InputLink.Text);
            }
        }

        // ------------------------------------------------------------
        //  VIDEO INFO FETCH
        //  Probes the given URL via yt-dlp and populates the video/audio
        //  format lists plus the info panel on the right.
        // ------------------------------------------------------------

        private async System.Threading.Tasks.Task FetchVideoInfoAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || _isFetchingVideoInfo)
                return;

            _isFetchingVideoInfo = true;
            SetFetchingState(true);

            try
            {
                var result = await _ytdl.RunVideoDataFetch(url);

                if (!result.Success)
                {
                    MessageBox.Show(
                        "Não foi possível carregar informações do vídeo:\n" +
                        string.Join("\n", result.ErrorOutput),
                        "Az Video Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var info = result.Data;

                VideoTitleText.Text = info.Title ?? "—";
                VideoDurationText.Text = info.Duration.HasValue
                    ? TimeSpan.FromSeconds(info.Duration.Value).ToString(@"hh\:mm\:ss")
                    : "—";
                VideoFpsText.Text = "—";      // set below, once a video format is selected
                VideoBitrateText.Text = "—";
                VideoResolutionText.Text = "—";
                VideoSizeText.Text = "—";

                var videoFormats = info.Formats
                    .Where(f => f.VideoCodec != "none" && f.VideoCodec != null)
                    .OrderByDescending(f => f.Height ?? 0)
                    .Select(FormatListItem.ForVideo)
                    .ToList();

                var audioFormats = info.Formats
                    .Where(f => f.AudioCodec != "none" && f.AudioCodec != null
                             && (f.VideoCodec == "none" || f.VideoCodec == null))
                    .Select(FormatListItem.ForAudio)
                    .ToList();

                VideoFormatListBox.ItemsSource = videoFormats;
                VideoFormatListBox.DisplayMemberPath = nameof(FormatListItem.Display);

                AudioFormatListBox.ItemsSource = audioFormats;
                AudioFormatListBox.DisplayMemberPath = nameof(FormatListItem.Display);

                if (videoFormats.Count > 0) VideoFormatListBox.SelectedIndex = 0;
                if (audioFormats.Count > 0) AudioFormatListBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro inesperado ao carregar o vídeo:\n{ex.Message}",
                    "Az Video Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isFetchingVideoInfo = false;
                SetFetchingState(false);
            }
        }

        private void SetFetchingState(bool isFetching)
        {
            InputLink.IsEnabled = !isFetching;
            DownloadButton.IsEnabled = !isFetching;
            ThumbPlaceholderText.Text = isFetching ? "Carregando..." : "Pré-visualização";
        }

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

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputLink.Text))
            {
                MessageBox.Show("Cole o link do vídeo antes de continuar.", "Az Video Downloader",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputDir.Text))
            {
                MessageBox.Show("Selecione a pasta de saída antes de continuar.", "Az Video Downloader",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var arguments = BuildFfmpegArguments();

            // TODO: hand off `arguments` (and the selected video/audio format IDs)
            // to the download/transcode service, subscribing to progress events
            // that update DownloadProgressBar.Value and ProgressPercentText.Text.
        }

        // ------------------------------------------------------------
        //  FFMPEG ARGUMENT BUILDER
        //  Translates the checkbox/combo state into ffmpeg CLI flags.
        // ------------------------------------------------------------

        private List<string> BuildFfmpegArguments()
        {
            var args = new List<string>();

            if (AudioOnlyCheckBox.IsChecked == true)
            {
                args.Add("-vn");
            }
            else if (MergeAudioVideoCheckBox.IsChecked == true)
            {
                args.Add("-c copy");
            }

            if (EmbedThumbnailCheckBox.IsChecked == true)
            {
                args.Add("-map 0");
                args.Add("-map 1");
                args.Add("-disposition:v:1 attached_pic");
            }

            if (EmbedMetadataCheckBox.IsChecked == true)
            {
                args.Add("-map_metadata 0");
            }

            if (EmbedSubtitlesCheckBox.IsChecked == true)
            {
                var targetExt = ChangeExtensionCheckBox.IsChecked == true
                    ? (ChangeExtensionComboBox.Text ?? "mp4")
                    : "mp4";

                args.Add(targetExt == "mkv" || targetExt == "webm"
                    ? "-c:s srt"
                    : "-c:s mov_text");
            }

            if (ChangeExtensionCheckBox.IsChecked == true)
            {
                // The actual container change happens by setting the output
                // file's extension to ChangeExtensionComboBox.Text when
                // building the final output path - no direct ffmpeg flag here.
            }

            return args;
        }
    }
}