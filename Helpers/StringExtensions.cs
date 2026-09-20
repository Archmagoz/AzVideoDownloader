namespace AzVideoDownloader.Helpers
{
    /// <summary>
    /// String helpers shared across the application.
    /// </summary>
    internal static class StringExtensions
    {
        /// <summary>
        /// Compares two strings using ordinal, case-insensitive rules.
        /// Safe to call on a null receiver: two null values are considered equal.
        /// </summary>
        public static bool EqualsIgnoreCase(this string? left, string? right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}