using YoutubeDLSharp.Metadata;

namespace AzVideoDownloader.Services.Fetch
{
    /// <summary>
    /// Provides a display-friendly representation of a yt-dlp format while
    /// preserving the original <see cref="FormatData"/> for later processing.
    /// </summary>
    public sealed class GetAVFormatList(FormatData source, string display)
    {
        #region Fields

        /// <summary>
        /// Gets the original yt-dlp format data.
        /// </summary>
        public FormatData Source { get; } = source;

        /// <summary>
        /// Gets the human-readable format label displayed by the UI.
        /// </summary>
        public string Display { get; } = display;

        /// <summary>
        /// Gets the format identifier used by yt-dlp.
        /// </summary>
        public string FormatId => Source.FormatId;

        #endregion

        #region Factory Methods

        /// <summary>
        /// Creates a display model for a video format.
        /// </summary>
        public static GetAVFormatList ForVideo(FormatData format)
        {
            var fps = format.FrameRate.HasValue
                ? $" {format.FrameRate:0}fps"
                : string.Empty;

            var extension = format.Extension ?? "?";
            var size = FormatSize(
                format.FileSize ?? format.ApproximateFileSize);

            return new GetAVFormatList(
                format,
                $"{format.Format}{fps} · {extension} · {size}");
        }

        /// <summary>
        /// Creates a display model for an audio format.
        /// </summary>
        public static GetAVFormatList ForAudio(FormatData format)
        {
            var extension = format.Extension ?? "?";
            var size = FormatSize(
                format.FileSize ?? format.ApproximateFileSize);

            return new GetAVFormatList(
                format,
                $"{format.Format} · {extension} · {size}");
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Formats a byte count as a human-readable megabyte value.
        /// </summary>
        private static string FormatSize(long? bytes)
        {
            if (bytes is null or 0)
                return "tamanho desconhecido";

            var megabytes = bytes.Value / 1024.0 / 1024.0;

            return $"{megabytes:0.#} MB";
        }

        #endregion
    }
}