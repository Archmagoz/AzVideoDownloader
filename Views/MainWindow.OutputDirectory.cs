using System.IO;
using System.Windows;
using System.Windows.Controls;

using Microsoft.Win32;

using AzVideoDownloader.Helpers;

namespace AzVideoDownloader
{
    /// <summary>
    /// Output folder selection and the history of recently used directories.
    /// The history is persisted in user settings and mirrored in the OutputDir ComboBox,
    /// with the most recently used directory always first.
    /// </summary>
    public partial class MainWindow
    {
        // Maximum number of output directories retained in history.
        private const int MaxRecentOutputDirectories = 5;

        // Separator used to serialize the history into a single setting.
        // Safe because '|' is not a valid character in Windows paths.
        private const char RecentDirectoriesSeparator = '|';

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
                !(OutputDir.Items[0] as string).EqualsIgnoreCase(directory))
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
        /// The pruned list is written back so stale entries do not accumulate.
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

        /// <summary>
        /// Reads the persisted history, dropping blanks and case-insensitive duplicates
        /// and capping it at <see cref="MaxRecentOutputDirectories"/> entries.
        /// </summary>
        private static List<string> GetRecentOutputDirectories()
        {
            var stored = Properties.Settings.Default.RecentOutputDirectories;

            if (string.IsNullOrWhiteSpace(stored))
                return [];

            return [.. stored
                .Split(RecentDirectoriesSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRecentOutputDirectories)];
        }

        private static void SaveRecentOutputDirectories(IEnumerable<string> directories)
        {
            Properties.Settings.Default.RecentOutputDirectories =
                string.Join(RecentDirectoriesSeparator, directories);
            Properties.Settings.Default.Save();
        }

        private void PopulateRecentOutputDirectories(IEnumerable<string> directories)
        {
            OutputDir.Items.Clear();

            foreach (var directory in directories)
                OutputDir.Items.Add(directory);
        }

        /// <summary>
        /// Removes any existing occurrence of a directory (case-insensitive) and inserts it
        /// at the top of the list. Mutates and returns the same list instance.
        /// </summary>
        private static List<string> MoveToFront(List<string> directories, string directory)
        {
            directories.RemoveAll(path => path.EqualsIgnoreCase(directory));
            directories.Insert(0, directory);

            return directories;
        }
    }
}