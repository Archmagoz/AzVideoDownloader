using YoutubeDLSharp.Metadata;

namespace AzVideoDownloader.Services
{
    /// <summary>
    /// Thin display wrapper around <see cref="FormatData"/> so the format
    /// lists can bind a human-readable label without overriding ToString()
    /// on a type we don't own.
    /// </summary>
    public sealed class FormatListItem
    {
        public FormatData Source { get; }
        public string FormatId => Source.FormatId;
        public string Display { get; }

        public FormatListItem(FormatData source, string display)
        {
            Source = source;
            Display = display;
        }

        public static FormatListItem ForVideo(FormatData f)
        {
            // yt-dlp already builds a readable "Format" label (e.g. "137 - 1920x1080 (1080p)"),
            // so we lean on that instead of guessing at less-common property names.
            var fps = f.FrameRate.HasValue ? $" {f.FrameRate:0}fps" : "";
            var ext = f.Extension ?? "?";
            var size = FormatSize(f.FileSize ?? f.ApproximateFileSize);
            return new FormatListItem(f, $"{f.Format}{fps} · {ext} · {size}");
        }

        public static FormatListItem ForAudio(FormatData f)
        {
            var ext = f.Extension ?? "?";
            var size = FormatSize(f.FileSize ?? f.ApproximateFileSize);
            return new FormatListItem(f, $"{f.Format} · {ext} · {size}");
        }

        private static string FormatSize(long? bytes)
        {
            if (bytes is null or 0) return "tamanho desconhecido";
            double mb = bytes.Value / 1024.0 / 1024.0;
            return $"{mb:0.#} MB";
        }
    }
}