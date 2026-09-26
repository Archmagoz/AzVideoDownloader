using System.Windows;
using System.Windows.Controls;

using AzVideoDownloader.Models;
using AzVideoDownloader.Services.Fetch;

namespace AzVideoDownloader
{
    /// <summary>
    /// Video metadata workflow: fetching info and thumbnail, applying them to the UI
    /// and showing the details of the selected video format.
    /// </summary>
    public partial class MainWindow
    {
        // Cancels a metadata request when a newer request supersedes it.
        private CancellationTokenSource? _fetchCts;

        // Duration of the current video, used for bitrate estimation and range validation.
        private double? _currentVideoDurationSeconds;

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

            // The button must stay enabled while downloading so the user can still cancel.
            DownloadButton.IsEnabled = !isFetching || _downloadCts is not null;

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
    }
}