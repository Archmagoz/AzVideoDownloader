using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using AzVideoDownloader.Helpers;

namespace AzVideoDownloader
{
    /// <summary>
    /// Video title shown in the preview card. Click-to-edit lets the user override the
    /// output file name; the edit is locked while a download is running.
    /// </summary>
    public partial class MainWindow
    {
        // Text shown when no video is loaded. VideoTitleText_TextChanged treats this exact
        // value as "no title", so it must match what ResetToDefaultState writes
        // (MainWindow.VideoInfo.cs).
        private const string TitlePlaceholder = "—";
        private const string TitleEditToolTip = "Clique para editar o nome do arquivo";

        // Title reported by yt-dlp. Null when no video is loaded.
        private string? _fetchedTitle;

        // Sanitized name typed by the user, used as the output file name.
        // Null means "use the yt-dlp default name".
        private string? _customFileName;

        private bool _isEditingTitle;

        // True while this file is writing VideoTitleText.Text, so the change
        // notification is not mistaken for a newly fetched title.
        private bool _isRefreshingTitle;

        private bool IsDownloading => _downloadCts is not null;

        // The title is editable only when a video is loaded and no download is running.
        private bool CanEditTitle => !IsDownloading && _fetchedTitle is not null;

        #region Initialization

        /// <summary>
        /// Starts observing VideoTitleText.Text. Any change not made by this class is treated
        /// as a newly loaded title, so the fetch code does not need to know about the edit
        /// feature. Must be called once, after InitializeComponent.
        /// </summary>
        private void InitializeTitleTracking()
        {
            DependencyPropertyDescriptor
                .FromProperty(TextBlock.TextProperty, typeof(TextBlock))
                .AddValueChanged(VideoTitleText, VideoTitleText_TextChanged);

            UpdateTitleEditability();
        }

        /// <summary>
        /// Handles a title written by external code (e.g. the metadata fetch): it becomes
        /// the new fetched title and any previous user edit is discarded.
        /// </summary>
        private void VideoTitleText_TextChanged(object? sender, EventArgs e)
        {
            if (_isRefreshingTitle)
                return;

            EndTitleEdit(commit: false);

            var text = VideoTitleText.Text;

            // The placeholder means "no video loaded", not a real title.
            _fetchedTitle =
                string.IsNullOrWhiteSpace(text) || text == TitlePlaceholder
                    ? null
                    : text;

            _customFileName = null;

            UpdateTitleEditability();
        }

        #endregion

        #region Title State

        /// <summary>
        /// Sets the title reported by yt-dlp and discards any previous user edit.
        /// Pass null to reset the preview when no video is loaded.
        /// Optional: writing VideoTitleText.Text directly has the same effect.
        /// </summary>
        private void SetVideoTitle(string? title)
        {
            EndTitleEdit(commit: false);

            _fetchedTitle = string.IsNullOrWhiteSpace(title) ? null : title;
            _customFileName = null;

            RefreshTitleDisplay();
            UpdateTitleEditability();
        }

        /// <summary>
        /// Updates the affordances of the title (hand cursor and tooltip) according to
        /// <see cref="CanEditTitle"/>. The control itself stays enabled so the text keeps
        /// its normal appearance while a download is running; <see cref="BeginTitleEdit"/>
        /// enforces the lock.
        /// </summary>
        private void UpdateTitleEditability()
        {
            var canEdit = CanEditTitle;

            VideoTitleText.Cursor = canEdit ? Cursors.Hand : null;
            VideoTitleText.ToolTip = canEdit ? TitleEditToolTip : null;
        }

        #endregion

        #region Edit Workflow

        private void VideoTitleText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            BeginTitleEdit();
            e.Handled = true;
        }

        private void VideoTitleEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    EndTitleEdit(commit: true);
                    e.Handled = true;
                    break;

                case Key.Escape:
                    EndTitleEdit(commit: false);
                    e.Handled = true;
                    break;
            }
        }

        private void VideoTitleEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
            EndTitleEdit(commit: true);

        /// <summary>
        /// Switches the title to its inline editor. Does nothing unless
        /// <see cref="CanEditTitle"/> allows it; this is where the download lock is enforced.
        /// </summary>
        private void BeginTitleEdit()
        {
            if (!CanEditTitle || _isEditingTitle)
                return;

            _isEditingTitle = true;

            VideoTitleEditor.Text = VideoTitleText.Text;
            VideoTitleText.Visibility = Visibility.Collapsed;
            VideoTitleEditor.Visibility = Visibility.Visible;

            // Focus is deferred: the editor was just made visible and cannot take
            // keyboard focus until layout has processed the visibility change.
            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                () =>
                {
                    VideoTitleEditor.Focus();
                    VideoTitleEditor.SelectAll();
                });
        }

        /// <summary>
        /// Leaves edit mode. The flag is cleared first because collapsing the editor
        /// raises LostKeyboardFocus, which would otherwise re-enter this method.
        /// </summary>
        private void EndTitleEdit(bool commit)
        {
            if (!_isEditingTitle)
                return;

            _isEditingTitle = false;

            if (commit)
                ApplyEditedTitle(VideoTitleEditor.Text);

            VideoTitleEditor.Visibility = Visibility.Collapsed;
            VideoTitleText.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Sanitizes the typed name and stores it as the output name. An empty result or a
        /// name equal to the fetched title reverts to the default yt-dlp behavior.
        /// The displayed title is then refreshed, so the user sees the sanitized value.
        /// </summary>
        private void ApplyEditedTitle(string rawTitle)
        {
            // Nothing changed: keep the current state untouched.
            if (string.Equals(rawTitle, VideoTitleText.Text, StringComparison.Ordinal))
                return;

            var sanitized = FileNameSanitizer.Sanitize(rawTitle);

            _customFileName =
                sanitized.Length == 0 ||
                string.Equals(sanitized, _fetchedTitle, StringComparison.Ordinal)
                    ? null
                    : sanitized;

            RefreshTitleDisplay();
        }

        /// <summary>
        /// Writes the displayed title: the custom name if set, otherwise the fetched title,
        /// otherwise the placeholder. Guarded so the change notification is ignored
        /// by <see cref="VideoTitleText_TextChanged"/>.
        /// </summary>
        private void RefreshTitleDisplay()
        {
            _isRefreshingTitle = true;

            try
            {
                VideoTitleText.Text = _customFileName ?? _fetchedTitle ?? TitlePlaceholder;
            }
            finally
            {
                _isRefreshingTitle = false;
            }
        }

        #endregion
    }
}