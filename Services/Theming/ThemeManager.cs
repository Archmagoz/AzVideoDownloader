using Microsoft.Win32;
using MaterialDesignThemes.Wpf;

namespace AzVideoDownloader.Services.Theming
{
    /// <summary>
    /// Manages the application's theme mode and applies the corresponding
    /// Material Design base theme.
    /// The user's choice is persisted in application settings; <see cref="ThemeMode.System"/>
    /// follows the Windows app theme, but is resolved only when the theme is applied
    /// (changes made in Windows while the app is running are not tracked).
    /// </summary>
    public static class ThemeManager
    {
        #region Theme Mode

        public enum ThemeMode
        {
            System,
            Light,
            Dark
        }

        /// <summary>
        /// Gets the theme mode persisted in application settings.
        /// Falls back to <see cref="ThemeMode.System"/> when the stored
        /// value cannot be parsed.
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
        /// Gets the effective theme mode currently resolved by the application.
        /// When <see cref="ThemeMode.System"/> is selected, the value is
        /// resolved from the current Windows theme, so the result is always
        /// <see cref="ThemeMode.Light"/> or <see cref="ThemeMode.Dark"/>.
        /// </summary>
        public static ThemeMode CurrentThemeMode =>
            SavedThemeMode == ThemeMode.System
                ? IsWindowsDarkMode()
                    ? ThemeMode.Dark
                    : ThemeMode.Light
                : SavedThemeMode;

        #endregion

        #region Public API

        /// <summary>
        /// Applies the theme mode currently persisted in application settings.
        /// </summary>
        public static void ApplySavedTheme()
        {
            ApplyTheme(SavedThemeMode);
        }

        /// <summary>
        /// Persists and applies the specified application theme mode.
        /// </summary>
        public static void SetTheme(ThemeMode mode)
        {
            Properties.Settings.Default.ThemeMode = mode.ToString();
            Properties.Settings.Default.Save();

            ApplyTheme(mode);
        }

        /// <summary>
        /// Determines whether Windows is currently configured to use
        /// dark mode for applications.
        /// Returns <see langword="false"/> (light) when the registry value is missing,
        /// as on Windows versions that predate the setting.
        /// </summary>
        public static bool IsWindowsDarkMode()
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            // AppsUseLightTheme is 0 for dark and 1 for light.
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Applies the specified theme mode to the Material Design palette.
        /// System mode is resolved against the current Windows application theme.
        /// Only the base theme (light/dark) is changed; the rest of the current palette is kept.
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

        #endregion
    }
}