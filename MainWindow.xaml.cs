using Microsoft.Win32; // OpenFolderDialog (available on .NET 8+ / WPF)
using System;
using System.Collections.Generic;
using System.Printing;
using System.Windows;

namespace AzVideoDownloader
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // ------------------------------------------------------------
        //  TOP BAR ACTIONS
        // ------------------------------------------------------------

        private void PasteLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                InputLink.Text = Clipboard.GetText().Trim();
            }
        }

        private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
        {
            // .NET 8+ WPF ships a native folder picker; falls back to
            // System.Windows.Forms.FolderBrowserDialog on older targets.
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
                // Drop the video stream entirely, keep audio only
                args.Add("-vn");
            }
            else if (MergeAudioVideoCheckBox.IsChecked == true)
            {
                // Mux the selected video + audio streams without re-encoding
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
                // Re-encoding flags (e.g. "-c:v libx264 -c:a aac") should be
                // added only if the source codecs are incompatible with the
                // chosen container.
            }

            return args;
        }
    }
}