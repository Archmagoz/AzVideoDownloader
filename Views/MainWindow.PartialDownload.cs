using System.Globalization;
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

        // Upper bound of the two-digit hours field. The format has no day component,
        // so the largest representable value is 99:59:59.
        private const int MaxTimestampHours = 99;

        // 99:59:59 expressed in seconds (99 * 3600 + 59 * 60 + 59).
        private static readonly TimeSpan MaxTimestamp = new(MaxTimestampHours, 59, 59);

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
        /// Updates a timestamp TextBox from a TimeSpan value.
        /// Values outside [00:00:00, 99:59:59] are clamped as a whole, so the displayed
        /// timestamp is never a mix of a capped hours field and real minutes/seconds.
        /// </summary>
        private static void SetTimestampTextBoxValue(TextBox textBox, TimeSpan value)
        {
            if (value < TimeSpan.Zero)
                value = TimeSpan.Zero;
            else if (value > MaxTimestamp)
                value = MaxTimestamp;

            // Truncate (not round) to whole seconds so the value never exceeds the real duration.
            var totalSeconds = (int)value.TotalSeconds;
            var hours = totalSeconds / 3600;
            var minutes = totalSeconds % 3600 / 60;
            var seconds = totalSeconds % 60;

            SetTimestampDigits(textBox, $"{hours:00}{minutes:00}{seconds:00}");
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
        /// Parses a fixed-width "HH:mm:ss" timestamp into total seconds.
        /// <para>
        /// <see cref="TimeSpan.TryParse(string, out TimeSpan)"/> is intentionally not used:
        /// it treats the first component as a time-of-day hour and rejects values of 24 or more,
        /// which would make ranges for videos longer than 24 hours impossible to parse.
        /// </para>
        /// </summary>
        /// <returns>
        /// <c>true</c> if the value is well-formed and minutes and seconds are within 0-59.
        /// </returns>
        private static bool TryParseTimestamp(string? value, out double seconds)
        {
            seconds = 0;

            var parts = value?.Split(':');

            if (parts is not { Length: 3 })
                return false;

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var secs))
            {
                return false;
            }

            if (minutes > 59 || secs > 59)
                return false;

            seconds = hours * 3600 + minutes * 60 + secs;
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