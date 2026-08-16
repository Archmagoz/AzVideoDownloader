namespace AzVideoDownloader.Services
{
    /// <summary>
    /// Plain snapshot of the ffmpeg-related checkbox/combo state, so the
    /// argument-building logic doesn't need to touch any UI control directly.
    /// </summary>
    public sealed class FfmpegOptions
    {
        public bool AudioOnly { get; set; }
        public bool MergeAudioVideo { get; set; }
        public bool EmbedThumbnail { get; set; }
        public bool EmbedMetadata { get; set; }
        public bool EmbedSubtitles { get; set; }
        public bool ChangeExtension { get; set; }
        public string TargetExtension { get; set; } = "mp4";
    }

    /// <summary>
    /// Translates <see cref="FfmpegOptions"/> into the equivalent ffmpeg
    /// CLI flags. See the guide comment in MainWindow.xaml for the mapping
    /// rationale behind each flag.
    /// </summary>
    public static class FfmpegArgumentBuilder
    {
        public static List<string> Build(FfmpegOptions options)
        {
            var args = new List<string>();

            if (options.AudioOnly)
            {
                args.Add("-vn");
            }
            else if (options.MergeAudioVideo)
            {
                args.Add("-c copy");
            }

            if (options.EmbedThumbnail)
            {
                args.Add("-map 0");
                args.Add("-map 1");
                args.Add("-disposition:v:1 attached_pic");
            }

            if (options.EmbedMetadata)
            {
                args.Add("-map_metadata 0");
            }

            if (options.EmbedSubtitles)
            {
                var targetExt = options.ChangeExtension ? options.TargetExtension : "mp4";

                args.Add(targetExt is "mkv" or "webm"
                    ? "-c:s srt"
                    : "-c:s mov_text");
            }

            // ChangeExtension itself has no direct ffmpeg flag - the
            // container change happens by setting the output file's
            // extension when building the final output path.

            return args;
        }
    }
}