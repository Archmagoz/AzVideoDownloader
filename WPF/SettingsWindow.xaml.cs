using System.Windows;

using AzVideoDownloader.Services;

namespace AzVideoDownloader
{
    public partial class SettingsWindow : Window
    {
        private bool _isLoadingSettings;

        public SettingsWindow()
        {
            InitializeComponent();

            LoadSettings();
        }

        /// <summary>
        /// Loads the persisted application settings and updates the UI.
        /// </summary>
        private void LoadSettings()
        {
            _isLoadingSettings = true;

            try
            {
                PopupsToggle.IsChecked =
                    Properties.Settings.Default.ShowPopups;

                if (Enum.TryParse(
                        Properties.Settings.Default.ThemeMode,
                        out ThemeManager.ThemeMode themeMode))
                {
                    DarkModeToggle.IsChecked =
                        themeMode == ThemeManager.ThemeMode.Dark;

                    ThemeManager.ApplyTheme(themeMode);
                }
                else
                {
                    DarkModeToggle.IsChecked = false;

                    ThemeManager.ApplyTheme(
                        ThemeManager.ThemeMode.Light);
                }
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        private void DarkModeToggle_Checked(
            object sender,
            RoutedEventArgs e)
        {
            if (_isLoadingSettings)
                return;

            SetTheme(ThemeManager.ThemeMode.Dark);
        }

        private void DarkModeToggle_Unchecked(
            object sender,
            RoutedEventArgs e)
        {
            if (_isLoadingSettings)
                return;

            SetTheme(ThemeManager.ThemeMode.Light);
        }

        /// <summary>
        /// Applies and persists the selected application theme.
        /// </summary>
        private static void SetTheme(
            ThemeManager.ThemeMode themeMode)
        {
            ThemeManager.ApplyTheme(themeMode);

            Properties.Settings.Default.ThemeMode =
                themeMode.ToString();

            Properties.Settings.Default.Save();
        }

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

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}