namespace AzVideoDownloader.Services.Models
{
    /// <summary>
    /// Plain snapshot of the ffmpeg-related checkbox/combo state, so the
    /// argument-building logic doesn't need to touch any UI control directly.
    ///
    /// Thumbnail/metadata/subtitle embedding and audio extraction now live in
    /// <see cref="YtDlpOptions"/> instead — yt-dlp's own postprocessors handle
    /// those. This class only holds raw merge/container state.
    /// </summary>
    public sealed class FfmpegOptions
    {
        /// <summary>Mux pre-downloaded audio and video streams without re-encoding.</summary>
        public bool MergeAudioVideo { get; set; }

        // --- Container/extension state -------------------------------
        // Grouped together since EffectiveExtension depends on all three.

        /// <summary>Whether the output container extension differs from the source.</summary>
        public bool ChangeExtension { get; set; }

        /// <summary>
        /// The container extension of the source file (e.g. "webm", "mkv", "mp4"),
        /// used whenever <see cref="ChangeExtension"/> is false. Required because the
        /// output container is NOT necessarily mp4 just because the user didn't ask
        /// to change it — an unmodified webm/mkv source stays webm/mkv.
        /// </summary>
        public string SourceExtension { get; set; } = "mp4";

        /// <summary>
        /// Desired output extension when <see cref="ChangeExtension"/> is true.
        /// Only meaningful in that case.
        /// </summary>
        public string TargetExtension { get; set; } = "mp4";

        /// <summary>
        /// Resolves the effective output container extension, taking
        /// <see cref="ChangeExtension"/> into account.
        /// </summary>
        public string EffectiveExtension =>
            ChangeExtension ? TargetExtension : SourceExtension;
    }
}