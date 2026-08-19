using AzVideoDownloader.Services.Core;
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
            // Same "--js-runtimes deno:<path>" + "--extractor-args
            // youtube:player_client=..." override used for downloads (see
            // ToolManagerService.CreateYouTubeOverrideOptions): the JS
            // challenge (nsig/PO token) is solved during extraction, so the
            // metadata fetch needs it just as much as the download step.
            var result = await _ytdl.RunVideoDataFetch(
                url,
                ct: ct,
                overrideOptions: ToolManagerService.CreateYouTubeOverrideOptions());

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
                // Sort by bitrate (highest first), same principle as the
                // video sort above. Without this, "pick the first m4a" in
                // MainWindow.SelectPreferredFormat could land on whatever
                // low-bitrate stream happened to come first in yt-dlp's raw
                // (unsorted-by-quality) format list.
                .OrderByDescending(f => f.AudioBitrate ?? 0)
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