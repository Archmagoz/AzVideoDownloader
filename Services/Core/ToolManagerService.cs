using System.IO;
using System.Reflection;

using YoutubeDLSharp.Options;

namespace AzVideoDownloader.Services.Core
{
    /// <summary>
    /// Extracts and resolves the bundled yt-dlp/ffmpeg/ffprobe/deno binaries.
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

        /// <summary>
        /// JavaScript runtime yt-dlp uses to solve YouTube's JS challenges
        /// (nsig/PO token deciphering). Passed explicitly via
        /// "--js-runtimes deno:&lt;path&gt;" (see VideoDownloadService) instead of
        /// relying on yt-dlp finding a system-wide Deno install on PATH.
        /// </summary>
        public static string DenoPath =>
            Path.Combine(ToolsDirectory, "deno.exe");

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
        /// Builds the OptionSet override applied to every yt-dlp invocation
        /// (both metadata fetch and download) made through this service -
        /// see GetVideoinfo and VideoDownloadService, since both are affected
        /// by the JS challenge and the player-client selection.
        ///
        /// Two overrides are bundled here:
        ///
        /// 1. "--js-runtimes "deno:{DenoPath}" - pins the bundled Deno
        ///    runtime for YouTube's JS challenge solving (nsig/PO token
        ///    deciphering). Solved during extraction, not just download, so
        ///    both entry points need it.
        ///
        /// 2. "--extractor-args youtube:player-client=web_embedded" -
        ///    widens which YouTube "player clients" yt-dlp queries. By
        ///    default only a small subset of clients is used, and each
        ///    client can return a different subset of dubbed-audio tracks;
        ///    a video with many audio languages may otherwise show only one
        ///    (typically English/original). This is a community-reported
        ///    mitigation, NOT a guaranteed fix - some videos may still be
        ///    missing dub tracks depending on which client YouTube serves
        ///    them from. There is currently no yt-dlp option that guarantees
        ///    every audio-language track is listed.
        ///
        /// YoutubeDLSharp's OptionSet has no typed property for either flag
        /// (both are relatively new/niche), so both are added via
        /// AddCustomOption. The "deno:" prefix is required so yt-dlp knows
        /// which runtime the path belongs to (format: RUNTIME[:PATH]).
        /// </summary>
        public static OptionSet CreateYouTubeOverrideOptions()
        {
            var options = new OptionSet();

            options.AddCustomOption<string>(
                "--js-runtimes",
                $"deno:{DenoPath}");

            options.AddCustomOption<string>(
                "--extractor-args",
                "youtube:player-client=web_embedded");

            return options;
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