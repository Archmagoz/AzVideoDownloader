using AzVideoDownloader.Services.Models;

namespace AzVideoDownloader.Services.Core
{
    /// <summary>
    /// Translates <see cref="YtDlpOptions"/> into the equivalent yt-dlp CLI
    /// arguments. yt-dlp shells out to ffmpeg itself for all of this
    /// (extraction, muxing, remux/recode, thumbnail/metadata/subtitle
    /// embedding), so this replaces most of what FfmpegArgumentBuilderService
    /// used to do by hand.
    ///
    /// IMPORTANT: same convention as FfmpegArgumentBuilderService — each
    /// flag/value pair is a separate list entry for
    /// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>.
    /// </summary>
    public static class YtDlpArgumentBuilderService
    {
        public static List<string> Build(YtDlpOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var args = new List<string>();

            if (options.AudioOnly)
            {
                BuildAudioOnly(options, args);
            }
            else
            {
                BuildVideo(options, args);
            }

            // --- Shared postprocessing flags --------------------------------

            if (options.EmbedThumbnail)
            {
                // Skip for audio containers that don't support an embedded
                // cover picture (e.g. wav) instead of passing a flag that
                // will just warn/fail per-file.
                if (!options.AudioOnly || YtDlpAudioFormats.SupportsEmbeddedThumbnail(options.AudioFormat))
                {
                    args.Add("--embed-thumbnail");
                }
            }

            if (options.EmbedMetadata)
            {
                args.Add("--embed-metadata");
            }

            // Subtitles don't apply to an audio-only extraction.
            if (options.EmbedSubtitles && !options.AudioOnly)
            {
                args.Add("--write-subs");
                args.Add("--embed-subs");
                args.Add("--sub-langs");
                args.Add(string.IsNullOrWhiteSpace(options.SubtitleLangs) ? "all" : options.SubtitleLangs);
            }

            return args;
        }

        private static void BuildAudioOnly(YtDlpOptions options, List<string> args)
        {
            // "-x"/"--extract-audio" is the correct yt-dlp flag; it downloads
            // the best available audio-only format (or extracts audio from a
            // combined stream if that's all the site offers) and hands it to
            // ffmpeg for conversion. This is what was missing before: doing
            // "-vn" by hand on an ffmpeg call assumed a video+audio file had
            // already been downloaded, which isn't guaranteed and skips
            // yt-dlp's own audio-format selection entirely.
            args.Add("-x");

            args.Add("--audio-format");
            args.Add(YtDlpAudioFormats.ToAudioFormatArg(options.AudioFormat));

            if (!string.IsNullOrWhiteSpace(options.AudioQuality))
            {
                args.Add("--audio-quality");
                args.Add(options.AudioQuality);
            }
        }

        private static void BuildVideo(YtDlpOptions options, List<string> args)
        {
            var hasVideo = !string.IsNullOrWhiteSpace(options.VideoFormatId);
            var hasAudio = !string.IsNullOrWhiteSpace(options.AudioFormatId);

            if (options.MergeAudioVideo && hasVideo && hasAudio)
            {
                // e.g. "-f 137+140"
                args.Add("-f");
                args.Add($"{options.VideoFormatId}+{options.AudioFormatId}");

                // Forces the muxed output into a specific container; yt-dlp
                // re-muxes (or re-encodes if truly incompatible) as needed.
                args.Add("--merge-output-format");
                args.Add(options.EffectiveContainer);
            }
            else if (hasVideo)
            {
                args.Add("-f");
                args.Add(options.VideoFormatId!);

                if (options.ChangeExtension)
                {
                    // Try a cheap container swap first; yt-dlp itself only
                    // falls back to a real re-encode if you ask for
                    // --recode-video, so remux is the right default here to
                    // avoid silently re-encoding when a copy would do.
                    args.Add("--remux-video");
                    args.Add(options.TargetContainer);
                }
            }
        }
    }
}