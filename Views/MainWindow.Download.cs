using System.Windows;
using System.Windows.Controls;

using YoutubeDLSharp;

using AzVideoDownloader.Helpers;
using AzVideoDownloader.Models;
using AzVideoDownloader.Services.Fetch;

using static AzVideoDownloader.Helpers.UserNotification;

namespace AzVideoDownloader
{
    /// <summary>
    /// Download workflow: starting, cancelling, UI state transitions, progress reporting
    /// and the options that shape the output. Shares state and helpers (popups, timestamp
    /// parsing) with the other MainWindow parts.
    /// </summary>
    public partial class MainWindow
    {
        // Tag value that switches DownloadButton to its red "CANCELAR" state (see MainWindow.xaml).
        private const string DownloadingButtonState = "Downloading";

        // Container extensions available for video and audio-only downloads.
        private static readonly string[] VideoContainerExtensions = YtDlpVideoFormats.UiSelectableLabels;
        private static readonly string[] AudioContainerExtensions = YtDlpAudioFormats.UiSelectableLabels;

        // Cancels the running download. Null when no download is in progress.
        private CancellationTokenSource? _downloadCts;

        #region Download Action

        /// <summary>
        /// Starts a download, or cancels the running one when the button is in its "cancel" state.
        /// </summary>
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadCts is not null)
            {
                ProgressPercentText.Text = "Cancelando...";
                _downloadCts.Cancel();
                return;
            }

            await StartDownloadAsync();
        }

        private async Task StartDownloadAsync()
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

            using var cts = new CancellationTokenSource();
            _downloadCts = cts;

            try
            {
                SetDownloadingState(true);

                DownloadProgressBar.Value = 0;
                ProgressPercentText.Text = "0%";

                var result = await _videoDownloadService.DownloadAsync(
                    InputLink.Text.Trim(),
                    OutputDir.Text,
                    selectedVideo,
                    selectedAudio,
                    options,
                    new Progress<DownloadProgress>(OnDownloadProgress),
                    cts.Token);

                if (!result.Success)
                {
                    // Killing the process may surface as a failed result instead of an exception.
                    if (cts.IsCancellationRequested)
                    {
                        ProgressPercentText.Text = "Cancelado";
                        return;
                    }

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
                _downloadCts = null;
                SetDownloadingState(false);
            }
        }

        /// <summary>
        /// Single entry point for the UI state transition between idle and downloading.
        /// Keeping every download-related UI change here guarantees that all controls
        /// are locked and restored together, even when the download fails or is cancelled.
        /// </summary>
        private void SetDownloadingState(bool isDownloading)
        {
            // Switches DownloadButton between "BAIXAR" and the red "CANCELAR" (see MainWindow.xaml).
            DownloadButton.Tag = isDownloading ? DownloadingButtonState : null;

            // The button is always clickable: it either starts or cancels a download.
            DownloadButton.IsEnabled = true;

            // Lock the inputs that are read when the download starts, so the UI cannot
            // diverge from the running job. Containers are disabled instead of individual
            // controls because IsEnabled is inherited: controls that are disabled on their
            // own (e.g. the video list in audio-only mode) keep their state when unlocked.
            LinkInputPanel.IsEnabled = !isDownloading;
            OutputDirPanel.IsEnabled = !isDownloading;
            FormatSelectionCard.IsEnabled = !isDownloading;
            OptionsPanel.IsEnabled = !isDownloading;
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
            // Ignore late updates (e.g. an "Error" state caused by the killed process)
            // so they do not overwrite the "Cancelando..." / "Cancelado" label.
            if (_downloadCts is { IsCancellationRequested: true })
                return;

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
                items.FirstOrDefault(item => ((string)item.Content).EqualsIgnoreCase(preferredDefault))
                ?? items.FirstOrDefault();
        }

        #endregion
    }
}