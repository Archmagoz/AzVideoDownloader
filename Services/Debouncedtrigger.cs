using System.Windows.Threading;

namespace AzVideoDownloader.Services
{
    /// <summary>
    /// Fires a callback either after a debounce delay (each call to
    /// <see cref="Arm"/> restarts the countdown) or immediately via
    /// <see cref="TriggerNow"/>, which cancels any pending debounce.
    /// Wraps a <see cref="DispatcherTimer"/> so callers don't need to
    /// manage Start/Stop/Tick bookkeeping themselves.
    /// </summary>
    public sealed class DebouncedTrigger
    {
        private readonly DispatcherTimer _timer;
        private readonly Action _callback;

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

        /// <summary>(Re)starts the countdown. Repeated calls restart it.</summary>
        public void Arm()
        {
            _timer.Stop();
            _timer.Start();
        }

        /// <summary>Cancels a pending countdown without firing the callback.</summary>
        public void Cancel() => _timer.Stop();

        /// <summary>Cancels any pending countdown and fires the callback now.</summary>
        public void TriggerNow()
        {
            _timer.Stop();
            _callback();
        }
    }
}