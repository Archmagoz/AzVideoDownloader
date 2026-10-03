using System.Text;

namespace AzVideoDownloader.Helpers
{
    /// <summary>
    /// Produces file names that are valid on Windows 10/11 (NTFS).
    /// The result is meant to be used as a base name: an extension is always appended later,
    /// so reserved device names are checked as if an extension were present.
    /// </summary>
    public static class FileNameSanitizer
    {
        /// <summary>
        /// Maximum base-name length. Kept well below the 255-char component limit to leave room
        /// for the extension and the suffixes yt-dlp adds (e.g. ".f137", ".pt-BR.vtt").
        /// </summary>
        public const int MaxLength = 150;

        private const char ReplacementChar = '_';

        private static readonly char[] InvalidChars = "<>:\"/\\|?*".ToCharArray();

        // Includes the superscript variants (COM¹, COM², COM³...) that Windows also reserves.
        private static readonly HashSet<string> ReservedNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³"
            };

        /// <summary>
        /// Returns a Windows-safe base file name, or an empty string when nothing usable remains
        /// (callers should then fall back to their default name).
        /// </summary>
        public static string Sanitize(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var builder = new StringBuilder(name.Length);

            foreach (var c in name)
            {
                var isInvalid = char.IsControl(c) || Array.IndexOf(InvalidChars, c) >= 0;
                builder.Append(isInvalid ? ReplacementChar : c);
            }

            var sanitized = TrimTrailing(builder.ToString().Trim());

            if (sanitized.Length > MaxLength)
            {
                var length = MaxLength;

                // Never cut between the two halves of a surrogate pair (emoji, etc.).
                if (char.IsHighSurrogate(sanitized[length - 1]))
                    length--;

                sanitized = TrimTrailing(sanitized[..length]);
            }

            if (sanitized.Length == 0)
                return string.Empty;

            // Windows reserves the device name even when an extension follows ("CON.mp4"),
            // so only the segment before the first dot is compared.
            var firstSegment = sanitized.Split('.')[0].TrimEnd(' ');

            return ReservedNames.Contains(firstSegment)
                ? ReplacementChar + sanitized
                : sanitized;
        }

        /// <summary>
        /// Windows silently strips trailing dots and spaces, which would change the final name.
        /// </summary>
        private static string TrimTrailing(string value) => value.TrimEnd(' ', '.');
    }
}