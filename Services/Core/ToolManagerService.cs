using System.Diagnostics;
using System.IO;
using System.Reflection;

using YoutubeDLSharp.Options;

namespace AzVideoDownloader.Services.Core
{
    /// <summary>
    /// Extracts and resolves the bundled yt-dlp, FFmpeg, FFprobe, and Deno binaries.
    /// FFmpeg, FFprobe, and Deno are embedded in the assembly and extracted on demand
    /// to a per-version directory under the user's temporary folder. yt-dlp is seeded
    /// from the embedded copy into a persistent, unversioned directory under
    /// %LocalAppData% and then self-updated in the background on every startup.
    /// </summary>
    public static class ToolManagerService
    {
        private const string ToolDirectoryName = "AzVideoDownloader";

        // Versioned so that a new build extracts its own copies instead of reusing
        // binaries left behind by an older one. Falls back to "default" when the
        // assembly has no version.
        private static readonly string ToolsDirectory =
            Path.Combine(
                Path.GetTempPath(),
                ToolDirectoryName,
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "default"
            );

        // Deliberately outside the temp folder and not versioned: the self-updated
        // binary must survive temp cleanup and application upgrades. The embedded
        // copy is only used to seed this directory on first run (or to repair it).
        private static readonly string YtDlpDirectory =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ToolDirectoryName,
                "yt-dlp"
            );

        // Guards the creation of the update task so that concurrent callers
        // never start more than one 'yt-dlp -U' process.
        private static readonly object UpdateLock = new();
        private static Task? _ytDlpUpdateTask;

        // Paths are valid only after EnsureToolsExist has completed successfully.

        public static string YtDlpPath =>
            Path.Combine(YtDlpDirectory, "yt-dlp.exe");

        public static string FfmpegPath =>
            Path.Combine(ToolsDirectory, "ffmpeg.exe");

        public static string FfprobePath =>
            Path.Combine(ToolsDirectory, "ffprobe.exe");

        public static string DenoPath =>
            Path.Combine(ToolsDirectory, "deno.exe");

        /// <summary>
        /// Represents the background yt-dlp self-update. Await it before spawning
        /// any yt-dlp process, since the updater replaces yt-dlp.exe on disk.
        /// Returns a completed task if the update has not been started.
        /// </summary>
        public static Task YtDlpUpdateTask =>
            _ytDlpUpdateTask ?? Task.CompletedTask;

        /// <summary>
        /// Ensures that all bundled external tools are extracted to their target
        /// directories. Files that already exist are not overwritten.
        /// </summary>
        /// <exception cref="FileNotFoundException">
        /// An expected embedded resource is missing from the assembly.
        /// </exception>
        public static void EnsureToolsExist()
        {
            Directory.CreateDirectory(ToolsDirectory);
            Directory.CreateDirectory(YtDlpDirectory);

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
        /// Starts 'yt-dlp -U' in the background. The update runs at most once per
        /// process lifetime; subsequent calls return the same task.
        ///
        /// This method never throws: if the update fails (offline, timeout,
        /// blocked by antivirus), the application keeps using the current binary.
        /// </summary>
        public static Task StartYtDlpUpdate()
        {
            lock (UpdateLock)
            {
                return _ytDlpUpdateTask ??= Task.Run(UpdateYtDlpAsync);
            }
        }

        /// <summary>
        /// Creates the common yt-dlp options used by both metadata extraction
        /// and media downloads.
        ///
        /// The options configure:
        ///
        /// - Deno as the JavaScript runtime used by yt-dlp to solve
        ///   JavaScript-based challenges.
        /// - The YouTube web_embedded player client (in addition to the default
        ///   clients) to improve compatibility with videos that expose additional
        ///   audio tracks through different player clients.
        /// - Firefox cookies to allow yt-dlp to reuse the user's authenticated
        ///   browser session when accessing content that requires it. Requires
        ///   Firefox to be installed with a profile.
        /// - A higher number of concurrent fragments to improve download speed.
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
                "youtube:player-client=default,web_embedded");

            options.AddCustomOption<string>(
                "--cookies-from-browser",
                "firefox");

            options.AddCustomOption<int>(
                "--concurrent-fragments",
                8);

            return options;
        }

        /// <summary>
        /// Runs 'yt-dlp -U' and waits for it to exit, bounded by a timeout.
        /// Output is captured for diagnostics only. Any failure is swallowed so
        /// that an update problem never prevents the application from working.
        /// </summary>
        private static async Task UpdateYtDlpAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = YtDlpPath,
                    Arguments = "-U",
                    WorkingDirectory = YtDlpDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);

                if (process is null)
                    return;

                // Both streams must be drained concurrently with the wait,
                // otherwise a full pipe buffer can deadlock the child process.
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();

                // Upper bound so a stalled connection cannot keep the update
                // task (and anything awaiting it) pending indefinitely.
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }

                    Debug.WriteLine("[yt-dlp -U] Timed out.");
                    return;
                }

                Debug.WriteLine(
                    $"[yt-dlp -U] exit={process.ExitCode}\n{await stdout}\n{await stderr}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[yt-dlp -U] Failed: {ex.Message}");
            }
            finally
            {
                // Runs on every path (success, failure, timeout) so the binary
                // is always left in a usable state.
                RestoreIfBroken();
                CleanupOldFiles();
            }
        }

        /// <summary>
        /// Re-extracts the embedded yt-dlp if the executable is missing or
        /// implausibly small after an update attempt (e.g. interrupted download,
        /// antivirus quarantine).
        /// </summary>
        private static void RestoreIfBroken()
        {
            try
            {
                var info = new FileInfo(YtDlpPath);

                // The real binary is tens of MB; anything under 1 MB is a
                // truncated or corrupted file.
                if (info.Exists && info.Length > 1_000_000)
                    return;

                if (info.Exists)
                    info.Delete();

                ExtractIfNeeded(
                    "AzVideoDownloader.Tools.yt-dlp.exe",
                    YtDlpPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[yt-dlp restore] {ex.Message}");
            }
        }

        /// <summary>
        /// Removes leftover files from the yt-dlp updater. It renames the running
        /// executable to *.old (and may leave *.new behind) when swapping binaries.
        /// Best effort: files still in use are skipped and retried on the next run.
        /// </summary>
        private static void CleanupOldFiles()
        {
            foreach (var pattern in new[] { "*.old", "*.new" })
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(YtDlpDirectory, pattern))
                        File.Delete(file);
                }
                catch
                {
                    // Intentionally ignored: cleanup must never fail the update flow.
                }
            }
        }

        /// <summary>
        /// Extracts an embedded resource to the specified destination if the
        /// destination file does not already exist.
        ///
        /// The resource is written to a temporary file first and then moved into
        /// place, so an interrupted extraction can never leave a partially written
        /// executable at the destination.
        /// </summary>
        private static void ExtractIfNeeded(string resourceName, string destination)
        {
            if (File.Exists(destination))
                return;

            var assembly = Assembly.GetExecutingAssembly();

            using var stream = assembly.GetManifestResourceStream(resourceName) ??
                throw new FileNotFoundException($"Embedded resource not found: {resourceName}");

            var tempFile = destination + ".tmp";

            // Scoped so the handle is closed before the move below.
            using (var file = File.Create(tempFile))
            {
                stream.CopyTo(file);
            }

            File.Move(tempFile, destination, overwrite: true);
        }
    }
}