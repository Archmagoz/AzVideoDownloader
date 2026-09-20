using System.IO;
using System.Windows;
using System.Windows.Controls;

using Microsoft.Win32;

namespace AzVideoDownloader
{
    /// <summary>
    /// Output folder selection and the history of recently used directories.
    /// </summary>
    public partial class MainWindow
    {
        // Maximum number of output directories retained in history.
        private const int MaxRecentOutputDirectories = 5;

        private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Selecionar pasta de saída",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
                SelectOutputDirectory(dialog.FolderName);
        }

        /// <summary>
        /// Adds an existing directory to the history and selects it.
        /// </summary>
        private void SelectOutputDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;

            AddRecentOutputDirectory(directory);

            // Select after the ComboBox items have been refreshed.
            OutputDir.SelectedItem = directory;
        }

        /// <summary>
        /// Activates the selected directory and moves it to the top of the history.
        /// </summary>
        private void OutputDir_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OutputDir.SelectedItem is not string directory)
                return;

            var directories = MoveToFront(GetRecentOutputDirectories(), directory);
            SaveRecentOutputDirectories(directories);

            // Rebuild the ComboBox only when the order is out of sync with the history.
            if (OutputDir.Items.Count > 0 &&
                !EqualsIgnoreCase(OutputDir.Items[0] as string, directory))
            {
                PopulateRecentOutputDirectories(directories);
                OutputDir.SelectedItem = directory;
            }
        }

        /// <summary>
        /// Adds a directory to the history, trimmed to the maximum size, and refreshes the ComboBox.
        /// </summary>
        private void AddRecentOutputDirectory(string directory)
        {
            var directories = MoveToFront(GetRecentOutputDirectories(), directory);

            if (directories.Count > MaxRecentOutputDirectories)
                directories.RemoveRange(
                    MaxRecentOutputDirectories,
                    directories.Count - MaxRecentOutputDirectories);

            SaveRecentOutputDirectories(directories);
            PopulateRecentOutputDirectories(directories);
        }

        /// <summary>
        /// Loads persisted directories and removes paths that no longer exist.
        /// </summary>
        private void LoadRecentOutputDirectories()
        {
            var directories = GetRecentOutputDirectories()
                .Where(Directory.Exists)
                .ToList();

            SaveRecentOutputDirectories(directories);
            PopulateRecentOutputDirectories(directories);

            if (directories.Count > 0)
                OutputDir.SelectedItem = directories[0];
        }

        private static List<string> GetRecentOutputDirectories()
        {
            var stored = Properties.Settings.Default.RecentOutputDirectories;

            if (string.IsNullOrWhiteSpace(stored))
                return [];

            return [.. stored
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRecentOutputDirectories)];
        }

        private static void SaveRecentOutputDirectories(IEnumerable<string> directories)
        {
            Properties.Settings.Default.RecentOutputDirectories = string.Join("|", directories);
            Properties.Settings.Default.Save();
        }

        private void PopulateRecentOutputDirectories(IEnumerable<string> directories)
        {
            OutputDir.Items.Clear();

            foreach (var directory in directories)
                OutputDir.Items.Add(directory);
        }

        /// <summary>
        /// Removes any existing occurrence of a directory and inserts it at the top of the list.
        /// </summary>
        private static List<string> MoveToFront(List<string> directories, string directory)
        {
            directories.RemoveAll(path => EqualsIgnoreCase(path, directory));
            directories.Insert(0, directory);

            return directories;
        }
    }
}