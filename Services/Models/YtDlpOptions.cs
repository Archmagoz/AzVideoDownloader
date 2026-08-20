namespace AzVideoDownloader.Services.Models
{
    /// <summary>
    /// Plain snapshot of the yt-dlp-related UI state (checkboxes, combos,
    /// selected format IDs), consumed by YtDlpArgumentBuilderService.
    /// </summary>
    public sealed class YtDlpOptions
    {
        // --- Mode -------------------------------------------------------

        /// <summary>Extract audio only (yt-dlp -x), instead of downloading video.</summary>
        public bool AudioOnly { get; set; }

        // --- Audio-only path ---------------------------------------------

        /// <summary>
        /// UI-facing audio container/extension, e.g. "mp3", "m4a", "opus",
        /// "wav", "flac", "aac", or "ogg". This is what the "Alterar extensão
        /// de saída" combo shows when AudioOnly is checked. Translate it to
        /// the value yt-dlp's --audio-format actually expects via
        /// <see cref="YtDlpAudioFormats.ToAudioFormatArg"/> before building
        /// arguments — "ogg" is NOT a valid --audio-format value by itself,
        /// it maps to "vorbis" (yt-dlp/ffmpeg produce a .ogg file when asked
        /// for the vorbis codec).
        /// </summary>
        public string AudioFormat { get; set; } = "mp3";

        /// <summary>
        /// Optional --audio-quality value (0 = best VBR .. 10 = worst, or a
        /// fixed bitrate like "192K"). Null/empty means "don't pass the flag,
        /// let yt-dlp use its own default".
        /// </summary>
        public string? AudioQuality { get; set; }

        // --- Video path ----------------------------------------------------

        /// <summary>Selected video format id from VideoFormatListBox (e.g. "137").</summary>
        public string? VideoFormatId { get; set; }

        /// <summary>Selected audio format id from AudioFormatListBox (e.g. "140").</summary>
        public string? AudioFormatId { get; set; }

        /// <summary>Mux the selected video+audio formats into one file.</summary>
        public bool MergeAudioVideo { get; set; }

        /// <summary>Whether the output container extension differs from the source.</summary>
        public bool ChangeExtension { get; set; }

        /// <summary>Desired video container when <see cref="ChangeExtension"/> is true (mp4/mkv/mov/webm).</summary>
        public string TargetContainer { get; set; } = "mp4";

        /// <summary>Source container, used when <see cref="ChangeExtension"/> is false.</summary>
        public string SourceContainer { get; set; } = "mp4";

        /// <summary>Effective video output container, taking ChangeExtension into account.</summary>
        public string EffectiveContainer =>
            ChangeExtension ? TargetContainer : SourceContainer;

        // --- Shared postprocessing flags -----------------------------------

        /// <summary>--embed-thumbnail.</summary>
        public bool EmbedThumbnail { get; set; }

        /// <summary>--embed-metadata.</summary>
        public bool EmbedMetadata { get; set; }

        /// <summary>--write-subs --embed-subs. Ignored when AudioOnly is set.</summary>
        public bool EmbedSubtitles { get; set; }

        /// <summary>--sub-langs value, e.g. "en.*,pt.*". Null/empty means "all".</summary>
        public string? SubtitleLangs { get; set; }
    }

    /// <summary>
    /// Maps the UI-facing audio extension labels to the values yt-dlp's
    /// --audio-format actually accepts, and flags formats that don't
    /// reliably support an embedded cover picture.
    /// </summary>
    public static class YtDlpAudioFormats
    {
        /// <summary>
        /// UI label -> yt-dlp --audio-format value. yt-dlp doesn't have a
        /// literal "ogg" format: asking for the vorbis codec is what
        /// produces a .ogg file, so "ogg" is aliased to "vorbis" here.
        /// </summary>
        private static readonly Dictionary<string, string> UiLabelToYtDlpFormat =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["best"] = "best",
                ["aac"] = "aac",
                ["alac"] = "alac",
                ["flac"] = "flac",
                ["m4a"] = "m4a",
                ["mp3"] = "mp3",
                ["opus"] = "opus",
                ["wav"] = "wav",
                ["ogg"] = "vorbis",
                ["vorbis"] = "vorbis",
            };

        /// <summary>
        /// The list to feed ChangeExtensionComboBox / an audio-format combo
        /// when the UI is in audio-only mode. Order is just a sensible
        /// popularity-based default; reorder freely.
        /// </summary>
        public static readonly string[] UiSelectableLabels =
            { "mp3", "m4a", "opus", "ogg", "flac", "wav", "aac" };

        /// <summary>
        /// Audio formats whose container doesn't reliably support an
        /// embedded cover picture, so --embed-thumbnail should be skipped
        /// for them rather than passed through and left to fail/warn.
        /// </summary>
        private static readonly HashSet<string> ThumbnailIncompatible =
            new(StringComparer.OrdinalIgnoreCase) { "wav" };

        public static string ToAudioFormatArg(string uiLabel)
        {
            if (string.IsNullOrWhiteSpace(uiLabel))
            {
                return "best";
            }

            return UiLabelToYtDlpFormat.TryGetValue(uiLabel, out var mapped)
                ? mapped
                : uiLabel.ToLowerInvariant();
        }

        public static bool SupportsEmbeddedThumbnail(string uiLabel) =>
            !ThumbnailIncompatible.Contains(uiLabel);
    }
}