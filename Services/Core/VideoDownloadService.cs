using YoutubeDLSharp;
using YoutubeDLSharp.Options;

using AzVideoDownloader.Models;

using AzVideoDownloader.Services.Fetch;

namespace AzVideoDownloader.Services.Core
{
    /// <summary>
    /// Downloads media using the selected formats and options, and reports
    /// download progress to the caller.
    /// </summary>
    public class VideoDownloadService(YoutubeDL ytdl)
    {
        #region Fields

        private const string DefaultFormatSelector =
            "bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]";

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

        #region Format Resolution

        /// <summary>
        /// Resolves the video format selector from the selected video and
        /// audio formats.
        ///
        /// When no video format is selected, the default selector is used.
        /// When merging is disabled, only the selected video format is used.
        /// When merging is enabled, the selected audio format is combined
        /// with the video format when available; otherwise yt-dlp selects
        /// the best available audio stream.
        /// </summary>
        private static string BuildVideoFormatSelector(
            GetAVFormatList? video,
            GetAVFormatList? audio,
            YtDlpOptions options)
        {
            if (video is null)
                return DefaultFormatSelector;

            if (!options.MergeAudioVideo)
                return video.Source.FormatId;

            if (audio is not null)
                return $"{video.Source.FormatId}+{audio.Source.FormatId}";

            return $"{video.Source.FormatId}+ba";
        }

        /// <summary>
        /// Converts a UI audio format label to the corresponding
        /// YoutubeDLSharp <see cref="AudioConversionFormat"/> value.
        /// Falls back to the enum default when no matching value exists.
        /// </summary>
        private static AudioConversionFormat ToAudioConversionFormat(
            string uiLabel)
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
        /// Converts an output container extension to the corresponding
        /// YoutubeDLSharp <see cref="DownloadMergeFormat"/> value.
        /// Falls back to the enum default when no matching value exists.
        /// </summary>
        private static DownloadMergeFormat ToMergeFormat(
            string containerExtension)
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
        /// Builds the yt-dlp options for the current download.
        /// Includes the application-wide tool configuration and the
        /// post-processing options selected by the user.
        /// </summary>
        private static OptionSet BuildOverrideOptions(YtDlpOptions options)
        {
            var overrideOptions = ToolManagerService.CreateOverrideOptions();

            ConfigurePostProcessingOptions(overrideOptions, options);

            return overrideOptions;
        }

        /// <summary>
        /// Applies thumbnail, metadata, subtitle, and container options
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
        /// Configures subtitle download and embedding for video downloads.
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
        /// Configures video remuxing when the requested output container
        /// differs from the source container and no separate audio merge
        /// is being performed.
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