using Microsoft.Win32; // OpenFolderDialog (available on .NET 8+ / WPF)
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using YoutubeDLSharp;

using AzVideoDownloader.Services;

namespace AzVideoDownloader
{
    public partial class MainWindow : Window
    {
        // ------------------------------------------------------------
        //  FIELDS
        // ------------------------------------------------------------
        private readonly YoutubeDL _ytdl = null!;

        // Debounces fetches while the user is still typing/editing the link.
        private readonly DispatcherTimer _debounceTimer = new();
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(700);

        // Cancels a stale in-flight fetch when a newer one supersedes it
        // (new paste, new keystroke after debounce, link cleared, etc).
        private CancellationTokenSource? _fetchCts;

        // Duration (seconds) of the currently loaded video, used to derive
        // an approximate bitrate per selected format.
        private double? _currentVideoDurationSeconds;

        private static readonly System.Net.Http.HttpClient _httpClient = new();

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

            _debounceTimer = new DispatcherTimer { Interval = DebounceDelay };
            _debounceTimer.Tick += DebounceTimer_Tick;

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
                // Setting .Text raises TextChanged synchronously, which arms
                // the debounce timer; TriggerImmediateFetch below cancels
                // that and fetches right away instead of waiting.
                InputLink.Text = Clipboard.GetText().Trim();
                TriggerImmediateFetch();
            }
        }

        private void InputLink_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            // This event fires BEFORE the pasted text is actually inserted
            // into the TextBox, so we defer to a lower dispatcher priority
            // to run right after WPF finishes updating InputLink.Text.
            Dispatcher.BeginInvoke(new Action(TriggerImmediateFetch), DispatcherPriority.Background);
        }

        private void InputLink_TextChanged(object sender, TextChangedEventArgs e)
        {
            _debounceTimer.Stop();

            if (string.IsNullOrWhiteSpace(InputLink.Text))
            {
                // Nothing to fetch: cancel any pending work and go back to
                // the empty/placeholder state immediately, no need to wait.
                _fetchCts?.Cancel();
                ResetToDefaultState();
                return;
            }

            _debounceTimer.Start();
        }

        private void DebounceTimer_Tick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            _ = FetchVideoInfoAsync(InputLink.Text.Trim());
        }

        private void TriggerImmediateFetch()
        {
            _debounceTimer.Stop();
            _ = FetchVideoInfoAsync(InputLink.Text.Trim());
        }

        // ------------------------------------------------------------
        //  VIDEO INFO FETCH
        //  Probes the given URL via yt-dlp and populates the video/audio
        //  format lists plus the info panel on the right.
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
                var result = await _ytdl.RunVideoDataFetch(url, ct: cts.Token);

                // A newer fetch started while we were awaiting; discard
                // this result, the newer one owns the UI now.
                if (cts.Token.IsCancellationRequested)
                    return;

                if (!result.Success)
                {
                    ResetToDefaultState();
                    return;
                }

                var info = result.Data;

                _currentVideoDurationSeconds = info.Duration;

                VideoTitleText.Text = info.Title ?? "—";
                VideoDurationText.Text = info.Duration.HasValue
                    ? TimeSpan.FromSeconds(info.Duration.Value).ToString(@"hh\:mm\:ss")
                    : "—";

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

                await LoadThumbnailAsync(info.Thumbnail);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer fetch - nothing to show for this one.
            }
            catch (Exception)
            {
                if (!cts.Token.IsCancellationRequested)
                {
                    // Link didn't resolve to anything yt-dlp understands
                    // (typo, unsupported site, unfinished paste, etc) -
                    // fall back to the default/placeholder state rather
                    // than leaving stale info on screen.
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

        private void SetFetchingState(bool isFetching)
        {
            DownloadButton.IsEnabled = !isFetching;
            ThumbPlaceholderText.Text = isFetching ? "Carregando..." : "Pré-visualização";

            VideoFormatsLoadingIndicator.Visibility = isFetching ? Visibility.Visible : Visibility.Collapsed;
            AudioFormatsLoadingIndicator.Visibility = isFetching ? Visibility.Visible : Visibility.Collapsed;

            if (isFetching)
            {
                // Clear stale results immediately so the user doesn't see
                // the previous video's formats while the new one loads.
                VideoFormatListBox.ItemsSource = null;
                AudioFormatListBox.ItemsSource = null;
            }
        }

        /// <summary>
        /// Clears the whole info panel back to its empty/placeholder state.
        /// Used when the link is cleared, or when a fetch fails to resolve
        /// (invalid URL, unsupported site, network error, etc).
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
            // than a direct property, since we couldn't confirm a stable
            // bitrate property name on FormatData across versions.
            VideoBitrateText.Text = (sizeBytes.HasValue && _currentVideoDurationSeconds is > 0)
                ? $"{sizeBytes.Value * 8 / _currentVideoDurationSeconds.Value / 1000:0} kbps (aprox.)"
                : "—";
        }

        // ------------------------------------------------------------
        //  THUMBNAIL LOADING
        // ------------------------------------------------------------

        private async Task LoadThumbnailAsync(string? thumbnailUrl)
        {
            if (string.IsNullOrWhiteSpace(thumbnailUrl))
            {
                ThumbPreview.Source = null;
                ThumbPlaceholderText.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(thumbnailUrl);

                using var stream = new MemoryStream(bytes);
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();

                ThumbPreview.Source = bitmap;
                ThumbPlaceholderText.Visibility = Visibility.Collapsed;
            }
            catch
            {
                // Thumbnail is a nice-to-have; failing to load it shouldn't
                // interrupt the rest of the info panel.
                ThumbPreview.Source = null;
                ThumbPlaceholderText.Visibility = Visibility.Visible;
            }
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