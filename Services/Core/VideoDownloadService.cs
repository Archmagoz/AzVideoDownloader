using YoutubeDLSharp;
using YoutubeDLSharp.Options;

using AzVideoDownloader.Services.Fetch;
using AzVideoDownloader.Services.Models;

namespace AzVideoDownloader.Services.Core
{
    /// <summary>
    /// Downloads a video using the selected video/audio formats and
    /// reports download progress to the caller.
    ///
    /// CHANGED: this used to build a raw ffmpeg-flavoured format selector
    /// and rely on a manual "-vn" for audio-only. It now delegates to
    /// YoutubeDLSharp's dedicated RunAudioDownload(...) for the "Somente
    /// áudio" path, which is what actually issues yt-dlp's "-x"/
    /// "--audio-format" under the hood - see YtDlpArgumentBuilderService /
    /// YtDlpOptions for the reasoning.
    /// </summary>
    public class VideoDownloadService
    {
        private readonly YoutubeDL _ytdl;

        public VideoDownloadService(YoutubeDL ytdl)
        {
            _ytdl = ytdl;
        }

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

        /// <summary>
        /// Builds the yt-dlp format selector from the selected video/audio
        /// formats. Unrelated to audio-only downloads - those go through
        /// RunAudioDownload above instead.
        /// </summary>
        private static string BuildVideoFormatSelector(
            GetAVFormatList? video,
            GetAVFormatList? audio,
            YtDlpOptions options)
        {
            if (video is null)
                return "bestvideo+bestaudio/best";

            if (!options.MergeAudioVideo || audio is null)
                return video.Source.FormatId;

            return $"{video.Source.FormatId}+{audio.Source.FormatId}";
        }

        /// <summary>
        /// Maps our UI-facing audio format label to YoutubeDLSharp's
        /// AudioConversionFormat enum, which mirrors yt-dlp's own
        /// --audio-format values (best, aac, alac, flac, m4a, mp3, opus,
        /// vorbis, wav). Uses Enum.TryParse instead of a hardcoded switch
        /// so this compiles regardless of exactly which members the
        /// installed YoutubeDLSharp version's enum has - if a given format
        /// name isn't a real member, it just falls back to the enum's
        /// default (index 0) value rather than failing to build.
        ///
        /// "ogg" is aliased to "vorbis" by YtDlpAudioFormats.ToAudioFormatArg
        /// before it gets here, same as in the raw CLI builder, since
        /// yt-dlp/ffmpeg produce a .ogg file when asked for the vorbis codec.
        /// </summary>
        private static AudioConversionFormat ToAudioConversionFormat(string uiLabel)
        {
            var mapped = YtDlpAudioFormats.ToAudioFormatArg(uiLabel);
            var pascalCase = char.ToUpperInvariant(mapped[0]) + mapped[1..];

            return Enum.TryParse<AudioConversionFormat>(pascalCase, ignoreCase: true, out var parsed)
                ? parsed
                : default;
        }

        /// <summary>
        /// Same reasoning as <see cref="ToAudioConversionFormat"/>, but for
        /// the video merge container (mp4/mkv/webm/...).
        /// </summary>
        private static DownloadMergeFormat ToMergeFormat(string containerExtension)
        {
            if (string.IsNullOrWhiteSpace(containerExtension))
                return default;

            var pascalCase = char.ToUpperInvariant(containerExtension[0]) + containerExtension[1..].ToLowerInvariant();

            return Enum.TryParse<DownloadMergeFormat>(pascalCase, ignoreCase: true, out var parsed)
                ? parsed
                : default;
        }

        /// <summary>
        /// Builds the OptionSet passed as overrideOptions, layering the
        /// postprocessing flags (thumbnail/metadata/subs/remux) on top of
        /// whatever ToolManagerService already sets up (cookies, user-agent,
        /// etc).
        ///
        /// NOTE: property names below (EmbedThumbnail, EmbedMetadata,
        /// WriteSubs, EmbedSubs, SubLangs, RemuxVideo) follow YoutubeDLSharp's
        /// documented convention of mirroring yt-dlp's long CLI flag names
        /// in PascalCase (--embed-thumbnail -> EmbedThumbnail, etc). If any
        /// of these don't match your installed package version, IntelliSense
        /// on "overrideOptions." will show the real name to swap in.
        /// </summary>
        private static OptionSet BuildOverrideOptions(YtDlpOptions options)
        {
            var overrideOptions = ToolManagerService.CreateYouTubeOverrideOptions();

            overrideOptions.EmbedThumbnail = options.EmbedThumbnail
                && (!options.AudioOnly || YtDlpAudioFormats.SupportsEmbeddedThumbnail(options.AudioFormat));

            overrideOptions.EmbedMetadata = options.EmbedMetadata;

            if (options.EmbedSubtitles && !options.AudioOnly)
            {
                overrideOptions.WriteSubs = true;
                overrideOptions.EmbedSubs = true;
                overrideOptions.SubLangs = string.IsNullOrWhiteSpace(options.SubtitleLangs)
                    ? "all"
                    : options.SubtitleLangs;
            }

            // Remux-only path: a single video format, container changed,
            // but not merging with a separate audio track (that case is
            // instead handled by mergeFormat in DownloadAsync above).
            if (!options.AudioOnly && options.ChangeExtension && !options.MergeAudioVideo)
            {
                overrideOptions.RemuxVideo = options.TargetContainer;
            }

            return overrideOptions;
        }
    }
}