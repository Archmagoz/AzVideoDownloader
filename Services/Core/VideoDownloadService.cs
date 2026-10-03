using YoutubeDLSharp;
using YoutubeDLSharp.Options;

using AzVideoDownloader.Helpers;
using AzVideoDownloader.Models;
using AzVideoDownloader.Services.Fetch;

namespace AzVideoDownloader.Services.Core
{
    /// <summary>
    /// Downloads media using the selected formats and options, and reports
    /// download progress to the caller.
    /// Configures the <see cref="YoutubeDL"/> instance it is given (output folder and file
    /// template) before every run, so that instance must not be used by two downloads at once.
    /// </summary>
    public class VideoDownloadService(YoutubeDL ytdl)
    {
        #region Fields

        // Fallback used when no video format is selected: best MP4 video plus M4A audio,
        // or a single pre-merged MP4 stream when separate streams are not available.
        private const string DefaultFormatSelector =
            "bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]";

        // Used when the user did not rename the file: yt-dlp applies its own
        // file name sanitization to the video title.
        private const string DefaultOutputFileTemplate = "%(title)s.%(ext)s";

        private readonly YoutubeDL _ytdl = ytdl;

        #endregion

        #region Public API

        /// <summary>
        /// Runs a video or audio-only download, depending on <see cref="YtDlpOptions.AudioOnly"/>.
        /// In audio-only mode the <paramref name="video"/> and <paramref name="audio"/>
        /// selections are ignored: yt-dlp picks the source stream and converts it to
        /// <see cref="YtDlpOptions.AudioFormat"/>.
        /// </summary>
        /// <remarks>
        /// Cancelling <paramref name="cancellationToken"/> terminates the yt-dlp process.
        /// This may surface as an <see cref="OperationCanceledException"/> or as an unsuccessful
        /// result, so callers should handle both.
        /// </remarks>
        /// <returns>The yt-dlp run result; on success, its data holds the output file path.</returns>
        public async Task<RunResult<string>> DownloadAsync(
            string url,
            string outputFolder,
            GetAVFormatList? video,
            GetAVFormatList? audio,
            YtDlpOptions options,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // Both settings persist on the shared instance, so they are reapplied on every run.
            _ytdl.OutputFolder = outputFolder;
            _ytdl.OutputFileTemplate = BuildOutputFileTemplate(options);

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

            // Unspecified (default) leaves the merge container to yt-dlp.
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

        #region Output Naming

        /// <summary>
        /// Builds the yt-dlp output template. A user-defined name is sanitized again here
        /// (the operation is idempotent) so the service never trusts the UI layer.
        /// The extension is always left to yt-dlp, so thumbnails, subtitles and
        /// post-processed files keep a consistent base name.
        /// </summary>
        private static string BuildOutputFileTemplate(YtDlpOptions options)
        {
            var fileName = FileNameSanitizer.Sanitize(options.OutputFileName);

            if (fileName.Length == 0)
                return DefaultOutputFileTemplate;

            // "%" starts a yt-dlp template field, so literal percent signs must be escaped.
            return $"{fileName.Replace("%", "%%")}.%(ext)s";
        }

        #endregion

        #region Format Resolution

        /// <summary>
        /// Resolves the video format selector from the selected video and
        /// audio formats.
        ///
        /// When no video format is selected, the default selector is used.
        /// When merging is disabled, only the selected video format is used
        /// (the audio selection is ignored and the result may have no audio).
        /// When merging is enabled, the selected audio format is combined
        /// with the video format when available; otherwise yt-dlp selects
        /// the best available audio stream ("ba").
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

            // Enum member names are PascalCase (e.g. "vorbis" -> "Vorbis").
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
        /// Falls back to the enum default when no matching value exists, which leaves
        /// the merge container to yt-dlp.
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
        /// Includes the application-wide tool configuration, post-processing options,
        /// and any download-range options selected by the user.
        /// </summary>
        private static OptionSet BuildOverrideOptions(YtDlpOptions options)
        {
            var overrideOptions = ToolManagerService.CreateOverrideOptions();

            ConfigurePostProcessingOptions(overrideOptions, options);
            ConfigureDownloadRangeOptions(overrideOptions, options);

            return overrideOptions;
        }

        /// <summary>
        /// Applies thumbnail, metadata, subtitle, and remux options
        /// to the yt-dlp option set.
        /// </summary>
        private static void ConfigurePostProcessingOptions(
            OptionSet overrideOptions,
            YtDlpOptions options)
        {
            // The thumbnail is skipped for audio formats that cannot carry embedded artwork.
            overrideOptions.EmbedThumbnail =
                options.EmbedThumbnail &&
                (!options.AudioOnly ||
                 YtDlpAudioFormats.SupportsEmbeddedThumbnail(options.AudioFormat));

            overrideOptions.EmbedMetadata = options.EmbedMetadata;

            ConfigureSubtitleOptions(overrideOptions, options);
            ConfigureRemuxOptions(overrideOptions, options);
        }

        /// <summary>
        /// Configures the optional time range used to download only a portion
        /// of the source media. Does nothing unless partial download is enabled
        /// and both bounds are set.
        /// </summary>
        private static void ConfigureDownloadRangeOptions(
            OptionSet overrideOptions,
            YtDlpOptions options)
        {
            if (!options.DownloadPartial ||
                !options.DownloadStartSeconds.HasValue ||
                !options.DownloadEndSeconds.HasValue)
            {
                return;
            }

            var start = FormatTimestamp(options.DownloadStartSeconds.Value);
            var end = FormatTimestamp(options.DownloadEndSeconds.Value);

            // The leading "*" makes yt-dlp interpret the value as a time range
            // instead of a chapter name pattern.
            overrideOptions.AddCustomOption<string>(
                "--download-sections",
                $"*{start}-{end}");
        }

        /// <summary>
        /// Converts a duration in seconds to the timestamp format expected by yt-dlp
        /// (<c>mm:ss</c>, or <c>hh:mm:ss</c> from one hour on). Fractional seconds are dropped.
        /// The hours component is not capped at 23, so durations of 24 hours or more are preserved.
        /// </summary>
        private static string FormatTimestamp(double seconds)
        {
            var duration = TimeSpan.FromSeconds(seconds);

            // TimeSpan.Hours is only the 0-23 hours component of the day, so the total
            // must be read from TotalHours to avoid wrapping at 24 hours.
            var totalHours = (int)duration.TotalHours;

            return totalHours >= 1
                ? $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
                : $"{duration.Minutes:00}:{duration.Seconds:00}";
        }

        /// <summary>
        /// Configures subtitle download and embedding for video downloads.
        /// Uses all available languages when no language filter is set.
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
            // With a merge, the output container is already set through the merge format
            // (see DownloadAsync), so a separate remux step would be redundant.
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