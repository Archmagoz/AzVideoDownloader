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
        private static readonly HttpClient _httpClient = new();

        #region Public API

        /// <summary>
        /// Downloads and decodes the thumbnail at the specified URL.
        /// Returns <see langword="null"/> when the URL is empty or the
        /// thumbnail cannot be downloaded or decoded.
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
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}