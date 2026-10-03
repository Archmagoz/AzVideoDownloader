using YoutubeDLSharp;

using AzVideoDownloader.Models;
using AzVideoDownloader.Services.Core;

namespace AzVideoDownloader.Services.Fetch
{
    /// <summary>
    /// Fetches video metadata and converts yt-dlp format data into
    /// application-specific models without depending on UI components.
    /// </summary>
    public sealed class GetVideoinfo(YoutubeDL ytdl)
    {
        private readonly YoutubeDL _ytdl = ytdl;

        #region Public API

        /// <summary>
        /// Fetches metadata for the specified URL.
        /// Returns <see langword="null"/> when yt-dlp fails to resolve the URL.
        /// Cancellation may surface either as an <see cref="OperationCanceledException"/>
        /// or as a <see langword="null"/> result, so callers should check their own token
        /// after awaiting.
        /// </summary>
        public async Task<VideoInfoResult?> FetchAsync(
            string url,
            CancellationToken ct)
        {
            var result = await _ytdl.RunVideoDataFetch(
                url,
                ct: ct,
                overrideOptions: ToolManagerService.CreateOverrideOptions());

            if (!result.Success)
                return null;

            var info = result.Data;

            return new VideoInfoResult
            {
                // Same placeholder as the VideoInfoResult.Title default ("no title").
                Title = info.Title ?? "—",
                DurationSeconds = info.Duration,
                ThumbnailUrl = info.Thumbnail,
                VideoFormats = BuildVideoFormats(info.Formats),
                AudioFormats = BuildAudioFormats(info.Formats)
            };
        }

        #endregion

        #region Format Mapping

        // yt-dlp reports "none" for a missing codec and the value is null when the codec
        // is unknown. Both cases are treated as "no stream of that kind" in the filters below.

        /// <summary>
        /// Converts video-capable yt-dlp formats into display models,
        /// ordered by descending video resolution. Includes combined video+audio
        /// formats; formats with unknown height are listed last.
        /// </summary>
        private static List<GetAVFormatList> BuildVideoFormats(
            IEnumerable<YoutubeDLSharp.Metadata.FormatData> formats)
        {
            return [.. formats
                .Where(f => f.VideoCodec != "none" && f.VideoCodec != null)
                .OrderByDescending(f => f.Height ?? 0)
                .Select(GetAVFormatList.ForVideo)];
        }

        /// <summary>
        /// Converts audio-only yt-dlp formats (no video stream) into display models,
        /// ordered by descending audio bitrate.
        /// </summary>
        private static List<GetAVFormatList> BuildAudioFormats(
            IEnumerable<YoutubeDLSharp.Metadata.FormatData> formats)
        {
            return [.. formats
                .Where(f => f.AudioCodec != "none" && f.AudioCodec != null
                         && (f.VideoCodec == "none" || f.VideoCodec == null))
                .OrderByDescending(f => f.AudioBitrate ?? 0)
                .Select(GetAVFormatList.ForAudio)];
        }

        #endregion
    }
}