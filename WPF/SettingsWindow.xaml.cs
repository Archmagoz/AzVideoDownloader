using System.Windows;

using AzVideoDownloader.Services;

namespace AzVideoDownloader
{
    public partial class SettingsWindow : Window
    {
        #region Fields

        // Prevents setting changes from being persisted while the controls
        // are being initialized from the stored application settings.
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
        /// Loads the persisted application settings and updates the UI.
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
        /// Loads the persisted popup visibility setting into the UI.
        /// </summary>
        private void LoadPopupSetting()
        {
            PopupsToggle.IsChecked =
                Properties.Settings.Default.ShowPopups;
        }

        /// <summary>
        /// Loads the persisted theme setting and updates the dark mode toggle.
        /// </summary>
        private void LoadThemeSetting()
        {
            // Use the resolved theme rather than the persisted ThemeMode
            // directly. This is important for System mode: when Windows is
            // currently using dark mode, System resolves to Dark and the
            // toggle should therefore appear checked.
            DarkModeToggle.IsChecked =
                ThemeManager.CurrentThemeMode ==
                ThemeManager.ThemeMode.Dark;
        }

        #endregion

        #region Theme

        /// <summary>
        /// Handles enabling dark mode.
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
        /// Handles disabling dark mode.
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
        /// Handles changes to the application's popup visibility setting.
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
        /// Closes the settings window without performing any additional actions.
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