using YoutubeDLSharp;
using YoutubeDLSharp.Options;

using AzVideoDownloader.Models;

using AzVideoDownloader.Services.Fetch;

namespace AzVideoDownloader.Services.Core
{
    /// <summary>
    /// Downloads videos using the selected video/audio formats and
    /// reports download progress to the caller.
    /// </summary>
    public class VideoDownloadService(YoutubeDL ytdl)
    {
        #region Fields

        private const string DefaultFormatSelector = "bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]";

        private readonly YoutubeDL _ytdl = ytdl;

        #endregion

        #region Public API

        public async Task<RunResult<string>> DownloadAsync(
            string url,
            string outputFolder,
            GetAVFormatList? video,
            GetAVFormatList? audio,
            YtDlpOptions options,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _ytdl.OutputFolder = outputFolder;
            _ytdl.OutputFileTemplate = "%(title)s.%(ext)s";

            var overrideOptions = BuildOverrideOptions(options);

            if (options.AudioOnly)
            {
                var audioFormat = ToAudioConversionFormat(options.AudioFormat);

                return await _ytdl.RunAudioDownload(
                    url,
                    audioFormat,
                    ct: cancellationToken,
                    progress: progress,
                    overrideOptions: overrideOptions);
            }

            var format = BuildVideoFormatSelector(video, audio, options);

            var mergeFormat = options.MergeAudioVideo
                ? ToMergeFormat(options.EffectiveContainer)
                : default;

            return await _ytdl.RunVideoDownload(
                url,
                format,
                mergeFormat: mergeFormat,
                ct: cancellationToken,
                progress: progress,
                overrideOptions: overrideOptions);
        }

        #endregion

        #region Format Selection

        /// <summary>
        /// Builds the yt-dlp format selector from the selected video/audio formats.
        /// When the audio list is unavailable, the selected video is preserved and
        /// yt-dlp is allowed to select the best available audio stream.
        /// </summary>
        private static string BuildVideoFormatSelector(
            GetAVFormatList? video,
            GetAVFormatList? audio,
            YtDlpOptions options)
        {
            // No video was selected. Use the default fallback selector.
            if (video is null)
                return DefaultFormatSelector;

            // Download only the selected video format.
            if (!options.MergeAudioVideo)
                return video.Source.FormatId;

            // Both video and audio formats were explicitly selected.
            if (audio is not null)
                return $"{video.Source.FormatId}+{audio.Source.FormatId}";

            // The audio format list was unavailable or empty.
            // Preserve the selected video and let yt-dlp choose the best audio.
            return $"{video.Source.FormatId}+ba";
        }

        /// <summary>
        /// Maps the UI audio format to YoutubeDLSharp's AudioConversionFormat enum.
        /// Falls back to the enum default when the requested format is not available
        /// in the installed YoutubeDLSharp version.
        /// </summary>
        private static AudioConversionFormat ToAudioConversionFormat(string uiLabel)
        {
            var mapped = YtDlpAudioFormats.ToAudioFormatArg(uiLabel);
            var pascalCase = char.ToUpperInvariant(mapped[0]) + mapped[1..];

            return Enum.TryParse<AudioConversionFormat>(
                pascalCase,
                ignoreCase: true,
                out var parsed)
                ? parsed
                : default;
        }

        /// <summary>
        /// Maps the target container extension to YoutubeDLSharp's
        /// DownloadMergeFormat enum.
        /// </summary>
        private static DownloadMergeFormat ToMergeFormat(string containerExtension)
        {
            if (string.IsNullOrWhiteSpace(containerExtension))
                return default;

            var pascalCase =
                char.ToUpperInvariant(containerExtension[0]) +
                containerExtension[1..].ToLowerInvariant();

            return Enum.TryParse<DownloadMergeFormat>(
                pascalCase,
                ignoreCase: true,
                out var parsed)
                ? parsed
                : default;
        }

        #endregion

        #region Option Building

        /// <summary>
        /// Builds the yt-dlp options used for the current download.
        /// The base override set (js-runtime/extractor-args/cookies - see
        /// ToolManagerService.CreateOverrideOptions) is applied
        /// unconditionally, regardless of site, so download and fetch
        /// (GetVideoinfo) always use the exact same options.
        /// </summary>
        private static OptionSet BuildOverrideOptions(YtDlpOptions options)
        {
            var overrideOptions = ToolManagerService.CreateOverrideOptions();

            ConfigurePostProcessingOptions(overrideOptions, options);

            return overrideOptions;
        }

        /// <summary>
        /// Applies thumbnail, metadata, subtitle and container options
        /// to the yt-dlp option set.
        /// </summary>
        private static void ConfigurePostProcessingOptions(
            OptionSet overrideOptions,
            YtDlpOptions options)
        {
            overrideOptions.EmbedThumbnail =
                options.EmbedThumbnail &&
                (!options.AudioOnly ||
                 YtDlpAudioFormats.SupportsEmbeddedThumbnail(options.AudioFormat));

            overrideOptions.EmbedMetadata = options.EmbedMetadata;

            ConfigureSubtitleOptions(overrideOptions, options);
            ConfigureRemuxOptions(overrideOptions, options);
        }

        /// <summary>
        /// Configures subtitle writing and embedding for video downloads.
        /// </summary>
        private static void ConfigureSubtitleOptions(
            OptionSet overrideOptions,
            YtDlpOptions options)
        {
            if (options.AudioOnly || !options.EmbedSubtitles)
                return;

            overrideOptions.WriteSubs = true;
            overrideOptions.EmbedSubs = true;
            overrideOptions.SubLangs =
                string.IsNullOrWhiteSpace(options.SubtitleLangs)
                    ? "all"
                    : options.SubtitleLangs;
        }

        /// <summary>
        /// Configures remuxing when the output container is changed without
        /// merging a separate audio stream.
        /// </summary>
        private static void ConfigureRemuxOptions(
            OptionSet overrideOptions,
            YtDlpOptions options)
        {
            if (options.AudioOnly ||
                !options.ChangeExtension ||
                options.MergeAudioVideo)
            {
                return;
            }

            overrideOptions.RemuxVideo = options.TargetContainer;
        }

        #endregion
    }
}