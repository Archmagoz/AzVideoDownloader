using System.Windows.Threading;

namespace AzVideoDownloader.Helpers
{
    /// <summary>
    /// Provides debounced and immediate callback triggering through a
    /// <see cref="DispatcherTimer"/>.
    /// </summary>
    public sealed class DebouncedTrigger
    {
        #region Fields

        private readonly DispatcherTimer _timer;
        private readonly Action _callback;

        #endregion

        #region Constructor

        public DebouncedTrigger(TimeSpan delay, Action callback)
        {
            _callback = callback;
            _timer = new DispatcherTimer { Interval = delay };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                _callback();
            };
        }

        #endregion

        #region Public API

        /// <summary>
        /// Starts or restarts the debounce countdown.
        /// </summary>
        public void Arm()
        {
            _timer.Stop();
            _timer.Start();
        }

        /// <summary>
        /// Cancels the debounce countdown and invokes the callback immediately.
        /// </summary>
        public void TriggerNow()
        {
            _timer.Stop();
            _callback();
        }

        /// <summary>
        /// Cancels the debounce countdown without invoking the callback.
        /// </summary>
        public void Cancel() => _timer.Stop();

        #endregion
    }
}