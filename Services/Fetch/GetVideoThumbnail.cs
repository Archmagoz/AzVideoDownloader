using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace AzVideoDownloader.Services.Fetch
{
    /// <summary>
    /// Downloads video thumbnails and decodes them into WPF-compatible
    /// <see cref="BitmapImage"/> instances.
    /// </summary>
    public sealed class GetVideoThumbnail
    {
        // Shared for the whole process: HttpClient is designed to be reused, and creating
        // one per request can exhaust sockets.
        private static readonly HttpClient _httpClient = new();

        #region Public API

        /// <summary>
        /// Downloads and decodes the thumbnail at the specified URL.
        /// Returns <see langword="null"/> when the URL is empty or the
        /// thumbnail cannot be downloaded or decoded.
        /// The returned image is frozen, so it can be used from any thread.
        /// This method does not accept a <see cref="CancellationToken"/>: callers that can
        /// be superseded must re-check their own state after awaiting.
        /// </summary>
        public static async Task<BitmapImage?> LoadAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);

                using var stream = new MemoryStream(bytes);

                var bitmap = new BitmapImage();

                bitmap.BeginInit();

                // OnLoad decodes the whole image during EndInit, so the stream can be
                // disposed right after this block without breaking the bitmap.
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch
            {
                // The thumbnail is optional: any network or decoding failure simply
                // results in the placeholder being shown.
                return null;
            }
        }

        #endregion
    }
}