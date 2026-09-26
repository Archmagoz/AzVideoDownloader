using System.Windows;

namespace AzVideoDownloader.Helpers
{
    public static class UserNotification
    {
        #region Methods

        /// <summary>
        /// Displays an OK message box when user popups are enabled in the application settings.
        /// </summary>
        public static void ShowPopup(
            string message,
            MessageBoxImage image,
            string title = MainWindow.AppTitle)
        {
            if (!Properties.Settings.Default.ShowPopups)
                return;

            ShowPopupForced(message, image, title);
        }

        /// <summary>
        /// Displays an OK message box regardless of the application settings.
        /// </summary>
        public static void ShowPopupForced(
            string message,
            MessageBoxImage image,
            string title = MainWindow.AppTitle)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, image);
        }

        #endregion
    }
}