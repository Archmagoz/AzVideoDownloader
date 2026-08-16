using YoutubeDLSharp;

namespace AzVideoDownloader.Services
{
    public class VideoDownloadService
    {
        private readonly YoutubeDL _ytdl;

        public VideoDownloadService(YoutubeDL ytdl)
        {
            _ytdl = ytdl;
        }

        /// <summary>
        /// Downloads a video using the selected video/audio formats and
        /// reports download progress to the caller.
        /// </summary>
        public async Task<RunResult<string>> DownloadAsync(
            string url,
            string outputFolder,
            FormatListItem? video,
            FormatListItem? audio,
            FfmpegOptions options,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _ytdl.OutputFolder = outputFolder;

            // Build the yt-dlp format selector from the selected formats.
            string format = BuildFormatSelector(video, audio, options);

            return await _ytdl.RunVideoDownload(
                url,
                format,
                ct: cancellationToken,
                progress: progress);
        }

        /// <summary>
        /// Builds the yt-dlp format selector from the selected formats.
        /// </summary>
        private static string BuildFormatSelector(
            FormatListItem? video,
            FormatListItem? audio,
            FfmpegOptions options)
        {
            if (options.AudioOnly)
                return audio?.Source.FormatId ?? "bestaudio/best";

            if (video is null)
                return "bestvideo+bestaudio/best";

            if (!options.MergeAudioVideo || audio is null)
                return video.Source.FormatId;

            return $"{video.Source.FormatId}+{audio.Source.FormatId}";
        }
    }
}