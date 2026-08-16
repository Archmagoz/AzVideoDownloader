namespace AzVideoDownloader.Services
{
    /// <summary>
    /// Plain data returned by <see cref="VideoInfoService"/>, decoupled from
    /// both the raw yt-dlp <c>VideoData</c> shape and any UI controls.
    /// </summary>
    public sealed class VideoInfoResult
    {
        public string Title { get; init; } = "—";
        public double? DurationSeconds { get; init; }
        public string? ThumbnailUrl { get; init; }
        public List<FormatListItem> VideoFormats { get; init; } = new();
        public List<FormatListItem> AudioFormats { get; init; } = new();
    }
}