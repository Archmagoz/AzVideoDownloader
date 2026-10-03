using AzVideoDownloader.Services.Fetch;

namespace AzVideoDownloader.Models
{
    /// <summary>
    /// Video metadata and available formats, as consumed by the UI.
    /// The metadata fields are plain values; each format is wrapped in
    /// <see cref="GetAVFormatList"/>, which still exposes the underlying yt-dlp format
    /// through its <c>Source</c> property.
    /// </summary>
    public sealed class VideoInfoResult
    {
        #region Metadata

        // Defaults to the same "—" placeholder the title panel uses for "no title",
        // so a missing title is shown (and treated) as "no video loaded".
        public string Title { get; init; } = "—";

        /// <summary>Null when the duration is unknown.</summary>
        public double? DurationSeconds { get; init; }

        /// <summary>Null when no thumbnail is available.</summary>
        public string? ThumbnailUrl { get; init; }

        #endregion

        #region Formats

        // Empty (never null) when no formats of that kind are available.
        public List<GetAVFormatList> VideoFormats { get; init; } = [];
        public List<GetAVFormatList> AudioFormats { get; init; } = [];

        #endregion
    }
}