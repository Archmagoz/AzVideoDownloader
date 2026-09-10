using AzVideoDownloader.Services.Fetch;

namespace AzVideoDownloader.Models
{
    /// <summary>
    /// Plain data returned by <see cref="GetVideoInfo"/>, decoupled from
    /// both the raw yt-dlp <c>VideoData</c> shape and any UI controls.
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