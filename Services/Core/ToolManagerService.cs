using System.IO;
using System.Reflection;

using YoutubeDLSharp.Options;

namespace AzVideoDownloader.Services.Core
{
    /// <summary>
    /// Extracts and resolves the bundled yt-dlp, FFmpeg, FFprobe, and Deno binaries.
    /// </summary>
    public static class ToolManagerService
    {
        #region Fields

        private const string ToolDirectoryName = "AzVideoDownloader";

        private static readonly string ToolsDirectory =
            Path.Combine(
                Path.GetTempPath(),
                ToolDirectoryName,
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "default"
            );

        #endregion

        #region Tool Paths

        public static string YtDlpPath =>
            Path.Combine(ToolsDirectory, "yt-dlp.exe");

        public static string FfmpegPath =>
            Path.Combine(ToolsDirectory, "ffmpeg.exe");

        public static string FfprobePath =>
            Path.Combine(ToolsDirectory, "ffprobe.exe");

        public static string DenoPath =>
            Path.Combine(ToolsDirectory, "deno.exe");

        #endregion

        #region Public API

        /// <summary>
        /// Ensures that all bundled external tools are extracted to the
        /// application-specific temporary directory.
        /// </summary>
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

            ExtractIfNeeded(
                "AzVideoDownloader.Tools.deno.exe",
                DenoPath);
        }

        /// <summary>
        /// Creates the common yt-dlp options used by both metadata extraction
        /// and media downloads.
        ///
        /// The options configure:
        ///
        /// - Deno as the JavaScript runtime used by yt-dlp to solve
        ///   JavaScript-based challenges.
        /// - The YouTube web_embedded player client to improve compatibility
        ///   with videos that expose additional audio tracks through
        ///   different player clients.
        /// - Firefox cookies to allow yt-dlp to reuse the user's authenticated
        ///   browser session when accessing content that requires it.
        ///
        /// These options are added as custom options because YoutubeDLSharp's
        /// OptionSet does not expose strongly typed properties for them.
        /// </summary>
        public static OptionSet CreateOverrideOptions()
        {
            var options = new OptionSet();

            options.AddCustomOption<string>(
                "--js-runtimes",
                $"deno:{DenoPath}");

            options.AddCustomOption<string>(
                "--extractor-args",
                "youtube:player-client=web_embedded");

            options.AddCustomOption<string>(
                "--cookies-from-browser",
                "firefox");

            return options;
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Extracts an embedded resource to the specified destination if the
        /// destination file does not already exist.
        /// </summary>
        private static void ExtractIfNeeded(string resourceName, string destination)
        {
            if (File.Exists(destination))
                return;

            var assembly = Assembly.GetExecutingAssembly();

            using var stream = assembly.GetManifestResourceStream(resourceName) ??
                throw new FileNotFoundException($"Embedded resource not found: {resourceName}");

            using var file = File.Create(destination);

            stream.CopyTo(file);
        }

        #endregion
    }
}