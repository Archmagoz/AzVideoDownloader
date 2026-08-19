using AzVideoDownloader.Services.Models;
using YoutubeDLSharp;

namespace AzVideoDownloader.Services.Fetch
{
    /// <summary>
    /// Wraps yt-dlp metadata fetching. Contains no UI code - it can be
    /// unit tested or reused (e.g. in a batch/playlist feature) without a
    /// WPF window attached.
    /// </summary>
    public sealed class GetVideoinfo
    {
        private readonly YoutubeDL _ytdl;

        public GetVideoinfo(YoutubeDL ytdl)
        {
            _ytdl = ytdl;
        }

        /// <summary>
        /// Fetches metadata for <paramref name="url"/>. Returns null if
        /// yt-dlp did not resolve it successfully (invalid link, unsupported
        /// site, etc). Throws <see cref="System.OperationCanceledException"/>
        /// if <paramref name="ct"/> is cancelled - callers already handle
        /// that for debounce/supersede logic, so we don't swallow it here.
        /// </summary>
        public async Task<VideoInfoResultModel?> FetchAsync(string url, CancellationToken ct)
        {
            var result = await _ytdl.RunVideoDataFetch(url, ct: ct);

            if (!result.Success)
                return null;

            var info = result.Data;

            var videoFormats = info.Formats
                .Where(f => f.VideoCodec != "none" && f.VideoCodec != null)
                .OrderByDescending(f => f.Height ?? 0)
                .Select(GetAVFormatList.ForVideo)
                .ToList();

            var audioFormats = info.Formats
                .Where(f => f.AudioCodec != "none" && f.AudioCodec != null
                         && (f.VideoCodec == "none" || f.VideoCodec == null))
                .Select(GetAVFormatList.ForAudio)
                .ToList();

            return new VideoInfoResultModel
            {
                Title = info.Title ?? "—",
                DurationSeconds = info.Duration,
                ThumbnailUrl = info.Thumbnail,
                VideoFormats = videoFormats,
                AudioFormats = audioFormats
            };
        }
    }
}