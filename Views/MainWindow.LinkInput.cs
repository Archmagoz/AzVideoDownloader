using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using AzVideoDownloader.Helpers;

namespace AzVideoDownloader
{
    /// <summary>
    /// Video link input: paste handling and the debounced trigger that starts the metadata fetch.
    /// </summary>
    public partial class MainWindow
    {
        // Delay before fetching video information after the link input changes.
        private static readonly TimeSpan LinkDebounceDelay = TimeSpan.FromMilliseconds(700);

        // Prevents a metadata fetch from being triggered on every keystroke.
        // Assigned in the constructor, which may return early when the bundled tools
        // cannot be extracted (the app shuts down), hence the null! initializer.
        private readonly DebouncedTrigger _linkDebounce = null!;

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
    }
}