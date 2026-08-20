using AzVideoDownloader.Services.Models;

namespace AzVideoDownloader.Services.Core
{
    /// <summary>
    /// Translates <see cref="FfmpegOptions"/> into the equivalent ffmpeg
    /// CLI arguments.
    ///
    /// IMPORTANT: each ffmpeg flag/value pair is added as SEPARATE list
    /// entries (e.g. "-c", "copy" — not "-c copy"). This matches how
    /// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> expects
    /// arguments and avoids the shell-escaping pitfalls of building a single
    /// concatenated command-line string.
    ///
    /// SCOPE: this service now only covers a raw "mux two already-downloaded
    /// streams" ffmpeg call. Audio extraction, thumbnail embedding, metadata
    /// embedding and subtitle embedding are handled by yt-dlp's own
    /// postprocessors instead — see YtDlpArgumentBuilderService. Keeping
    /// those out of here avoids two code paths fighting to embed the same
    /// thumbnail/metadata/subs and possibly corrupting the output.
    /// </summary>
    public static class FfmpegArgumentBuilderService
    {
        public static List<string> Build(FfmpegOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var args = new List<string>();

            // Stream copy: mux pre-downloaded audio+video without re-encoding.
            if (options.MergeAudioVideo)
            {
                args.Add("-c");
                args.Add("copy");
            }

            // ChangeExtension itself has no direct ffmpeg flag here - the
            // container change happens by setting the output file's
            // extension when building the final output path. If the source
            // codecs aren't compatible with the target container and a real
            // re-encode is needed, that's better delegated to yt-dlp's
            // --recode-video (it already knows the right codec per
            // container), rather than hardcoding "-c:v libx264 -c:a aac"
            // here and re-encoding blindly even when a straight remux would
            // have worked.

            return args;
        }
    }
}