using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace AzVideoDownloader.Services.Fetch
{
    /// <summary>
    /// Downloads a thumbnail image and decodes it into a WPF-ready
    /// <see cref="BitmapImage"/>. Downloading via HttpClient first (rather
    /// than setting BitmapImage.UriSource directly) avoids blocking the UI
    /// thread on the synchronous load that UriSource triggers.
    /// </summary>
    public sealed class GetVideoThumbnail
    {
        private static readonly HttpClient _httpClient = new();

        /// <summary>
        /// Returns the decoded image, or null if the URL is empty or the
        /// download/decode failed. Failures are swallowed intentionally -
        /// a missing thumbnail should never interrupt the rest of the UI.
        /// </summary>
        public async Task<BitmapImage?> LoadAsync(string? url)
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
    }
}