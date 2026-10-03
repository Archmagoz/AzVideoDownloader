using System.Windows.Threading;

namespace AzVideoDownloader.Helpers
{
    /// <summary>
    /// Debounces a callback: <see cref="Arm"/> (re)starts a countdown and the callback runs
    /// only once the countdown elapses without being re-armed. Backed by a
    /// <see cref="DispatcherTimer"/>, so the callback always runs on the dispatcher thread
    /// that created the instance, and the instance must be created and used on that thread.
    /// </summary>
    public sealed class DebouncedTrigger
    {
        #region Fields

        private readonly DispatcherTimer _timer;
        private readonly Action _callback;

        #endregion

        #region Constructor

        /// <param name="delay">Quiet period required after the last <see cref="Arm"/> call.</param>
        /// <param name="callback">Action to invoke when the countdown elapses or on <see cref="TriggerNow"/>.</param>
        public DebouncedTrigger(TimeSpan delay, Action callback)
        {
            _callback = callback;
            _timer = new DispatcherTimer { Interval = delay };
            _timer.Tick += (_, _) =>
            {
                // DispatcherTimer repeats by default; stop it so the callback fires once per Arm.
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
            // Stop before Start so the interval is measured from this call.
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