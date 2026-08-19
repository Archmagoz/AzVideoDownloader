namespace AzVideoDownloader.Services.Models
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
}