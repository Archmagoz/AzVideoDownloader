using AzVideoDownloader.Services.Models;

namespace AzVideoDownloader.Services.Core
{
    /// <summary>
    /// Translates <see cref="FfmpegOptions"/> into the equivalent ffmpeg
    /// CLI arguments. See the guide comment in MainWindow.xaml for the mapping
    /// rationale behind each flag.
    ///
    /// IMPORTANT: each ffmpeg flag/value pair is added as SEPARATE list
    /// entries (e.g. "-c", "copy" — not "-c copy"). This matches how
    /// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> expects
    /// arguments and avoids the shell-escaping pitfalls of building a single
    /// concatenated command-line string.
    /// </summary>
    public static class FfmpegArgumentBuilderService
    {
        // Container formats whose subtitle track should use the SRT codec.
        // Every other container (mp4, mov, m4v, ...) requires mov_text,
        // since those containers don't support raw SRT subtitle streams.
        private static readonly HashSet<string> SrtCapableContainers =
            new(StringComparer.OrdinalIgnoreCase) { "mkv", "webm" };

        public static List<string> Build(FfmpegOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var args = new List<string>();

            // --- Stream selection / copy mode -----------------------------
            if (options.AudioOnly)
            {
                // "-vn" ("video: none") drops the video stream and keeps the
                // audio as-is. This is the correct ffmpeg flag for audio-only
                // output; "-x" is a yt-dlp shorthand and is NOT a valid
                // ffmpeg argument, it would be rejected by the ffmpeg process.
                args.Add("-vn");
            }
            else if (options.MergeAudioVideo)
            {
                // Stream copy: mux pre-downloaded audio+video without
                // re-encoding.
                args.Add("-c");
                args.Add("copy");
            }

            // --- Thumbnail embedding ---------------------------------------
            if (options.EmbedThumbnail)
            {
                // Map both the primary input (0) and the thumbnail image
                // input (1), then mark stream 1 as the attached picture.
                args.Add("-map");
                args.Add("0");
                args.Add("-map");
                args.Add("1");
                args.Add("-disposition:v:1");
                args.Add("attached_pic");
            }

            // --- Metadata ----------------------------------------------------
            if (options.EmbedMetadata)
            {
                args.Add("-map_metadata");
                args.Add("0");
            }

            // --- Subtitles ---------------------------------------------------
            // Guarded by !AudioOnly defensively: the UI disables and clears
            // EmbedSubtitlesCheckBox whenever AudioOnly is checked (subtitles
            // don't apply to an audio-only output), but this keeps the
            // builder correct even if it's ever called with a stale/manually
            // constructed FfmpegOptions where both flags are set.
            if (options.EmbedSubtitles && !options.AudioOnly)
            {
                // Use the ACTUAL effective output extension (post
                // ChangeExtension logic), never assume mp4. Assuming mp4 when
                // the source container is e.g. webm/mkv and the user did not
                // request a change would pick the wrong subtitle codec and
                // likely corrupt or fail the mux.
                var targetExt = options.EffectiveExtension;

                args.Add("-c:s");
                args.Add(SrtCapableContainers.Contains(targetExt) ? "srt" : "mov_text");
            }

            // ChangeExtension itself has no direct ffmpeg flag - the
            // container change happens by setting the output file's
            // extension when building the final output path.

            return args;
        }
    }
}