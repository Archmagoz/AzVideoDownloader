namespace AzVideoDownloader.Services.Core
{
    /// <summary>
    /// Plain snapshot of the ffmpeg-related checkbox/combo state, so the
    /// argument-building logic doesn't need to touch any UI control directly.
    /// </summary>
    public sealed class FfmpegOptions
    {
        /// <summary>Strip the video stream and keep audio only.</summary>
        public bool AudioOnly { get; set; }

        /// <summary>Mux pre-downloaded audio and video streams without re-encoding.</summary>
        public bool MergeAudioVideo { get; set; }

        /// <summary>Embed a thumbnail image as an attached picture stream.</summary>
        public bool EmbedThumbnail { get; set; }

        /// <summary>Copy all metadata from the first input to the output.</summary>
        public bool EmbedMetadata { get; set; }

        /// <summary>Embed subtitle streams into the output container.</summary>
        public bool EmbedSubtitles { get; set; }

        /// <summary>Whether the output container extension differs from the source.</summary>
        public bool ChangeExtension { get; set; }

        /// <summary>
        /// Desired output extension when <see cref="ChangeExtension"/> is true.
        /// Only meaningful in that case.
        /// </summary>
        public string TargetExtension { get; set; } = "mp4";

        /// <summary>
        /// The container extension of the source file (e.g. "webm", "mkv", "mp4"),
        /// used whenever <see cref="ChangeExtension"/> is false. Required because the
        /// output container is NOT necessarily mp4 just because the user didn't ask
        /// to change it — an unmodified webm/mkv source stays webm/mkv.
        /// </summary>
        public string SourceExtension { get; set; } = "mp4";

        /// <summary>
        /// Resolves the effective output container extension, taking
        /// <see cref="ChangeExtension"/> into account. This is the single
        /// source of truth other logic (e.g. subtitle codec selection)
        /// should use instead of assuming a fixed value.
        /// </summary>
        public string EffectiveExtension =>
            ChangeExtension ? TargetExtension : SourceExtension;
    }

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
            if (options.EmbedSubtitles)
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