using System.IO;
using System.Reflection;

namespace AzVideoDownloader.Services.Core
{
    /// <summary>
    /// Extracts and resolves the bundled yt-dlp/ffmpeg/ffprobe binaries.
    /// </summary>
    public static class ToolManagerService
    {
        private const string ToolDirectoryName = "AzVideoDownloader";

        private static readonly string ToolsDirectory =
            Path.Combine(
                Path.GetTempPath(),
                ToolDirectoryName,
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "default"
            );

        public static string YtDlpPath =>
            Path.Combine(ToolsDirectory, "yt-dlp.exe");

        public static string FfmpegPath =>
            Path.Combine(ToolsDirectory, "ffmpeg.exe");

        public static string FfprobePath =>
            Path.Combine(ToolsDirectory, "ffprobe.exe");

        public static void EnsureToolsExist()
        {
            Directory.CreateDirectory(ToolsDirectory);

            ExtractIfNeeded(
                "AzVideoDownloader.Tools.yt-dlp.exe",
                YtDlpPath);

            ExtractIfNeeded(
                "AzVideoDownloader.Tools.ffmpeg.exe",
                FfmpegPath);

            ExtractIfNeeded(
                "AzVideoDownloader.Tools.ffprobe.exe",
                FfprobePath);
        }

        private static void ExtractIfNeeded(
            string resourceName,
            string destination)
        {
            if (File.Exists(destination))
                return;

            var assembly = Assembly.GetExecutingAssembly();

            using var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream == null)
            {
                throw new FileNotFoundException(
                    $"Recurso embutido não encontrado: {resourceName}");
            }

            using var file = File.Create(destination);

            stream.CopyTo(file);
        }
    }
}