using AzVideoDownloader.Services.Fetch;

namespace AzVideoDownloader.Models
{
    /// <summary>
    /// Represents video metadata and available formats independently of the UI
    /// and the underlying yt-dlp data model.
    /// </summary>
    public sealed class VideoInfoResult
    {
        #region Metadata

        public string Title { get; init; } = "—";

        public double? DurationSeconds { get; init; }
        public string? ThumbnailUrl { get; init; }

        #endregion

        #region Formats

        public List<GetAVFormatList> VideoFormats { get; init; } = [];
        public List<GetAVFormatList> AudioFormats { get; init; } = [];

        #endregion
    }
}