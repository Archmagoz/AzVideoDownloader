using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AzVideoDownloader
{
    /// <summary>
    /// Partial download range input. Timestamps are edited as a fixed six-digit HH:mm:ss
    /// value stored in TextBox.Tag. New digits shift in from the right, like a calculator
    /// or a stopwatch input.
    /// </summary>
    public partial class MainWindow
    {
        // Number of digits in the fixed HH:mm:ss timestamp representation.
        private const int TimestampDigitCount = 6;

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
    }
}