using Microsoft.Win32;
using MaterialDesignThemes.Wpf;

namespace AzVideoDownloader.Services.Theming
{
    /// <summary>
    /// Manages the application's Material Design theme.
    /// </summary>
    public static class ThemeManager
    {
        public enum ThemeMode
        {
            System,
            Light,
            Dark
        }

        /// <summary>
        /// Gets the theme mode persisted in application settings.
        /// </summary>
        public static ThemeMode SavedThemeMode
        {
            get
            {
                if (Enum.TryParse(
                        Properties.Settings.Default.ThemeMode,
                        out ThemeMode mode))
                {
                    return mode;
                }

                return ThemeMode.System;
            }
        }

        /// <summary>
        /// Gets the theme currently resolved by the application.
        /// </summary>
        public static ThemeMode CurrentThemeMode =>
            SavedThemeMode == ThemeMode.System
                ? IsWindowsDarkMode()
                    ? ThemeMode.Dark
                    : ThemeMode.Light
                : SavedThemeMode;

        /// <summary>
        /// Applies the currently persisted application theme.
        /// </summary>
        public static void ApplySavedTheme()
        {
            ApplyTheme(SavedThemeMode);
        }

        /// <summary>
        /// Applies and persists the specified application theme.
        /// </summary>
        public static void SetTheme(ThemeMode mode)
        {
            Properties.Settings.Default.ThemeMode =
                mode.ToString();

            Properties.Settings.Default.Save();

            ApplyTheme(mode);
        }

        /// <summary>
        /// Applies the specified theme mode to the application.
        /// </summary>
        private static void ApplyTheme(ThemeMode mode)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();

            theme.SetBaseTheme(
                mode == ThemeMode.Dark ||
                (mode == ThemeMode.System && IsWindowsDarkMode())
                    ? BaseTheme.Dark
                    : BaseTheme.Light);

            paletteHelper.SetTheme(theme);
        }

        /// <summary>
        /// Determines whether Windows is currently using dark mode.
        /// </summary>
        public static bool IsWindowsDarkMode()
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
    }
}