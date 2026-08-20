namespace AzVideoDownloader.Services.Models
{
    /// <summary>
    /// Plain snapshot of the ffmpeg-related checkbox/combo state, so the
    /// argument-building logic doesn't need to touch any UI control directly.
    ///
    /// NOTE: thumbnail/metadata/subtitle embedding and audio extraction were
    /// moved to <see cref="YtDlpOptions"/> + YtDlpArgumentBuilderService,
    /// since yt-dlp's own postprocessors (--embed-thumbnail, --embed-metadata,
    /// --embed-subs, -x/--audio-format) already call ffmpeg internally and
    /// handle format-compatibility edge cases (e.g. webp thumbnail -> jpg,
    /// mov_text vs srt) that we'd otherwise have to reimplement by hand.
    ///
    /// What's left here is only the state relevant to a raw "merge these two
    /// already-downloaded streams / change the container" ffmpeg invocation.
    /// </summary>
    public sealed class FfmpegOptions
    {
        /// <summary>Mux pre-downloaded audio and video streams without re-encoding.</summary>
        public bool MergeAudioVideo { get; set; }

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
        /// <see cref="ChangeExtension"/> into account.
        /// </summary>
        public string EffectiveExtension =>
            ChangeExtension ? TargetExtension : SourceExtension;
    }
}