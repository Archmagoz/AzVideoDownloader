using System.Windows;

using AzVideoDownloader.Services.Theming;

namespace AzVideoDownloader
{
    /// <summary>
    /// Modal settings dialog (opened from MainWindow). Each change is persisted
    /// immediately; there is no separate save or cancel step.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        // Prevents setting changes from being persisted during initialization:
        // assigning IsChecked in LoadSettings raises the same events as a user click.
        private bool _isLoadingSettings;

        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        /// <summary>
        /// Loads persisted settings into the UI.
        /// </summary>
        private void LoadSettings()
        {
            _isLoadingSettings = true;

            try
            {
                LoadPopupSetting();
                LoadThemeSetting();
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        /// <summary>
        /// Loads the persisted popup visibility setting.
        /// </summary>
        private void LoadPopupSetting()
        {
            PopupsToggle.IsChecked =
                Properties.Settings.Default.ShowPopups;
        }

        /// <summary>
        /// Loads the persisted theme and reflects the resolved theme in the UI.
        /// </summary>
        private void LoadThemeSetting()
        {
            // Use the resolved theme so System mode reflects the current
            // Windows theme instead of the persisted mode itself.
            DarkModeToggle.IsChecked =
                ThemeManager.CurrentThemeMode ==
                ThemeManager.ThemeMode.Dark;
        }

        /// <summary>
        /// Enables dark mode.
        /// </summary>
        private void DarkModeToggle_Checked(
            object sender,
            RoutedEventArgs e)
        {
            if (_isLoadingSettings)
                return;

            ThemeManager.SetTheme(
                ThemeManager.ThemeMode.Dark);
        }

        /// <summary>
        /// Enables light mode.
        /// Note that this persists an explicit <see cref="ThemeManager.ThemeMode.Light"/>:
        /// once the user touches the toggle, the app no longer follows the Windows theme.
        /// </summary>
        private void DarkModeToggle_Unchecked(
            object sender,
            RoutedEventArgs e)
        {
            if (_isLoadingSettings)
                return;

            ThemeManager.SetTheme(
                ThemeManager.ThemeMode.Light);
        }

        /// <summary>
        /// Updates and persists the popup visibility setting.
        /// This is the setting read by <c>UserNotification.ShowPopup</c>; error popups
        /// shown through <c>ShowPopupForced</c> are not affected by it.
        /// </summary>
        private void PopupsToggle_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (_isLoadingSettings)
                return;

            Properties.Settings.Default.ShowPopups =
                PopupsToggle.IsChecked == true;

            Properties.Settings.Default.Save();
        }

        /// <summary>
        /// Closes the settings window.
        /// </summary>
        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}