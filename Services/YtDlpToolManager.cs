using System;
using System.IO;
using System.Linq;

namespace AzVideoDownloader.Services
{
    /// <summary>
    /// Resolves and validates the paths to the bundled yt-dlp/ffmpeg/ffprobe
    /// binaries shipped alongside the application in the "Tools" folder.
    /// </summary>
    public static class YtDlpToolManager
    {
        private const string ToolsFolderName = "Tools";

        public static string ToolsDirectory { get; } =
            Path.Combine(AppContext.BaseDirectory, ToolsFolderName);

        public static string YtDlpPath { get; } =
            Path.Combine(ToolsDirectory, "yt-dlp.exe");

        public static string FfmpegPath { get; } =
            Path.Combine(ToolsDirectory, "ffmpeg.exe");

        public static string FfprobePath { get; } =
            Path.Combine(ToolsDirectory, "ffprobe.exe");

        /// <summary>
        /// Checks that all required binaries are present. Call this once at
        /// startup so missing tools fail fast with a clear message instead
        /// of surfacing as a cryptic yt-dlp process-launch error later.
        /// </summary>
        public static void EnsureToolsExist()
        {
            var missing = new[] { YtDlpPath, FfmpegPath, FfprobePath }
                .Where(path => !File.Exists(path))
                .ToList();

            if (missing.Count > 0)
            {
                throw new FileNotFoundException(
                    "Ferramentas necessárias não encontradas: " +
                    string.Join(", ", missing.Select(Path.GetFileName)) +
                    $". Verifique se estão em '{ToolsDirectory}'.");
            }
        }
    }
}