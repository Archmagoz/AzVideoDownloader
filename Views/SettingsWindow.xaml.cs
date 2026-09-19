using System.Windows;

using AzVideoDownloader.Services.Theming;

namespace AzVideoDownloader
{
    public partial class SettingsWindow : Window
    {
        #region Fields

        // Prevents setting changes from being persisted during initialization.
        private bool _isLoadingSettings;

        #endregion

        #region Constructor

        public SettingsWindow()
        {
            InitializeComponent();

            LoadSettings();
        }

        #endregion

        #region Settings

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

        #endregion

        #region Theme

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

        #endregion

        #region Notifications

        /// <summary>
        /// Updates and persists the popup visibility setting.
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

        #endregion

        #region Window Actions

        /// <summary>
        /// Closes the settings window.
        /// </summary>
        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        #endregion
    }
}