namespace AzVideoDownloader.Models
{
    /// <summary>
    /// Represents the yt-dlp options selected through the application UI.
    /// This model is independent of UI controls and is consumed by
    /// <see cref="YtDlpArgumentBuilderService"/> when constructing arguments.
    /// </summary>
    public sealed class YtDlpOptions
    {
        #region Mode
        /// <summary>
        /// Determines whether only audio is extracted instead of downloading
        /// a video stream.
        /// </summary>
        public bool AudioOnly { get; set; }

        #endregion

        #region Download Range

        /// <summary>
        /// Determines whether only a specific time range of the source video
        /// should be downloaded.
        /// </summary>
        public bool DownloadPartial { get; set; }

        /// <summary>
        /// Gets or sets the start time of the requested download range.
        /// The value is expressed in seconds from the beginning of the video.
        /// </summary>
        public double? DownloadStartSeconds { get; set; }

        /// <summary>
        /// Gets or sets the end time of the requested download range.
        /// The value is expressed in seconds from the beginning of the video.
        /// </summary>
        public double? DownloadEndSeconds { get; set; }

        #endregion

        #region Audio-Only Options

        /// <summary>
        /// Gets or sets the output audio extension selected by the user.
        /// The value is converted to the corresponding yt-dlp
        /// <c>--audio-format</c> argument before command construction.
        /// </summary>
        public string AudioFormat { get; set; } = "mp3";

        /// <summary>
        /// Gets or sets the requested audio quality.
        /// Supports yt-dlp quality values such as VBR levels (0-10) or
        /// fixed bitrates such as <c>192K</c>. A null or empty value leaves
        /// audio quality selection to yt-dlp.
        /// </summary>
        public string? AudioQuality { get; set; }

        #endregion

        #region Video Options

        /// <summary>
        /// Gets or sets the selected video format ID.
        /// </summary>
        public string? VideoFormatId { get; set; }

        /// <summary>
        /// Gets or sets the selected audio format ID.
        /// </summary>
        public string? AudioFormatId { get; set; }

        /// <summary>
        /// Determines whether the selected video and audio formats should
        /// be merged into a single output file.
        /// </summary>
        public bool MergeAudioVideo { get; set; }

        /// <summary>
        /// Determines whether the output container should differ from the
        /// source container.
        /// </summary>
        public bool ChangeExtension { get; set; }

        /// <summary>
        /// Gets or sets the requested output container when
        /// <see cref="ChangeExtension"/> is enabled.
        /// </summary>
        public string TargetContainer { get; set; } = "mp4";

        /// <summary>
        /// Gets or sets the source container used when the output container
        /// is not being changed.
        /// </summary>
        public string SourceContainer { get; set; } = "mp4";

        /// <summary>
        /// Gets the container that should be used for the final output.
        /// </summary>
        public string EffectiveContainer =>
            ChangeExtension ? TargetContainer : SourceContainer;

        #endregion

        #region Postprocessing Options

        /// <summary>
        /// Determines whether the thumbnail should be embedded into the
        /// output file when supported by the selected format.
        /// </summary>
        public bool EmbedThumbnail { get; set; }

        /// <summary>
        /// Determines whether metadata should be embedded into the output file.
        /// </summary>
        public bool EmbedMetadata { get; set; }

        /// <summary>
        /// Determines whether subtitles should be downloaded and embedded.
        /// This option is ignored when <see cref="AudioOnly"/> is enabled.
        /// </summary>
        public bool EmbedSubtitles { get; set; }

        /// <summary>
        /// Gets or sets the subtitle language filter passed to yt-dlp.
        /// An empty value allows yt-dlp to select all available subtitle languages.
        /// </summary>
        public string? SubtitleLangs { get; set; }

        #endregion
    }

    /// <summary>
    /// Provides the audio format labels exposed by the UI and converts them
    /// to the corresponding yt-dlp format arguments.
    /// </summary>
    public static class YtDlpAudioFormats
    {
        #region Constants

        private const string OggUiLabel = "ogg";
        private const string OggYtDlpFormat = "vorbis";

        #endregion

        #region Lookup Data

        /// <summary>
        /// Gets the audio format labels available in the audio-only UI.
        /// </summary>
        public static readonly string[] UiSelectableLabels =
            ["mp3", "m4a", "opus", "ogg", "flac", "wav", "aac"];

        /// <summary>
        /// Gets the audio formats that do not reliably support embedded
        /// thumbnail artwork.
        /// </summary>
        private static readonly HashSet<string> ThumbnailIncompatible =
            new(StringComparer.OrdinalIgnoreCase)
            {
            "wav"
            };

        #endregion

        #region Public API

        /// <summary>
        /// Converts a UI audio format label to the value expected by
        /// yt-dlp's <c>--audio-format</c> option.
        /// </summary>
        public static string ToAudioFormatArg(string uiLabel)
        {
            if (string.IsNullOrWhiteSpace(uiLabel))
            {
                return "best";
            }

            return uiLabel.Equals(OggUiLabel, StringComparison.OrdinalIgnoreCase)
                ? OggYtDlpFormat
                : uiLabel.ToLowerInvariant();
        }

        /// <summary>
        /// Determines whether the specified audio format supports reliable
        /// embedded thumbnail artwork.
        /// </summary>
        public static bool SupportsEmbeddedThumbnail(string uiLabel) =>
            !ThumbnailIncompatible.Contains(uiLabel);

        #endregion
    }

    /// <summary>
    /// Provides the video container labels exposed by the UI and validates
    /// container values before they are passed to yt-dlp.
    /// </summary>
    public static class YtDlpVideoFormats
    {
        #region Lookup Data

        /// <summary>
        /// Gets the video container labels available in the video UI.
        /// </summary>
        public static readonly string[] UiSelectableLabels =
            ["mp4", "mkv", "mov", "webm"];

        #endregion

        #region Public API

        /// <summary>
        /// Determines whether the specified container extension is supported
        /// by the application.
        /// </summary>
        public static bool IsValid(string containerExtension) =>
            !string.IsNullOrWhiteSpace(containerExtension)
                && UiSelectableLabels.Contains(
                    containerExtension,
                    StringComparer.OrdinalIgnoreCase);

        #endregion
    }
}