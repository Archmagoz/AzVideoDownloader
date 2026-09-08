using Microsoft.Win32;
using MaterialDesignThemes.Wpf;

namespace AzVideoDownloader.Services
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
        /// Applies the specified theme mode to the application.
        /// </summary>
        public static void ApplyTheme(ThemeMode mode)
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