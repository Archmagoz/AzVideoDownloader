using AzVideoDownloader.Services.Fetch;

namespace AzVideoDownloader.Services.Models
{
    /// <summary>
    /// Plain data returned by <see cref="GetVideoInfo"/>, decoupled from
    /// both the raw yt-dlp <c>VideoData</c> shape and any UI controls.
    /// </summary>
    public sealed class VideoInfoResult
    {
        public string Title { get; init; } = "—";
        public double? DurationSeconds { get; init; }
        public string? ThumbnailUrl { get; init; }
        public List<GetAVFormatList> VideoFormats { get; init; } = new();
        public List<GetAVFormatList> AudioFormats { get; init; } = new();
    }
}