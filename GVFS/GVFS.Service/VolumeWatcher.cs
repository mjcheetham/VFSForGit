using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using GVFS.Common.Tracing;

namespace GVFS.Service
{
    internal class VolumeWatcher : IDisposable
    {
        private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(15);

        private readonly ITracer _tracer;
        private readonly object _callbackLock = new object();
        private readonly Dictionary<string, ICollection<Action<string>>> _volumeCallbacks;
        private readonly Timer _pollingTimer;

        public VolumeWatcher(ITracer tracer)
        {
            _tracer = tracer;
            _volumeCallbacks = new Dictionary<string, ICollection<Action<string>>>(StringComparer.OrdinalIgnoreCase);
            _pollingTimer = new Timer(PollingInterval.TotalMilliseconds)
            {
                // Don't automatically re-trigger the timer; we'll reset only if there are callbacks still to service
                AutoReset = false
            };
            _pollingTimer.Elapsed += TryInvokeVolumeAvailableCallbacks;
        }

        public void RegisterForPathFirstAvailable(string volumePath, Action<string> callback)
        {
            lock (_callbackLock)
            {
                _tracer.RelatedEvent(EventLevel.Informational, nameof(RegisterForPathFirstAvailable), new EventMetadata
                {
                    ["Volume"] = volumePath,
                });

                ICollection<Action<string>> callbacks;
                if (!_volumeCallbacks.TryGetValue(volumePath, out callbacks))
                {
                    callbacks = new List<Action<string>>();
                    _volumeCallbacks.Add(volumePath, callbacks);
                }

                callbacks.Add(callback);

                // Ensure the polling timer is running since we have at least one callback to service
                _pollingTimer.Start();
                _tracer.RelatedInfo($"{nameof(VolumeWatcher)}: Started polling for volume availability.");
            }
        }

        public void Dispose()
        {
            _pollingTimer.Stop();
            _pollingTimer.Elapsed -= TryInvokeVolumeAvailableCallbacks;
            _pollingTimer.Dispose();
        }

        private void TryInvokeVolumeAvailableCallbacks(object sender, ElapsedEventArgs e)
        {
            lock (_callbackLock)
            {
                foreach (var volumePath in _volumeCallbacks.Keys)
                {
                    // Check if the volume is now available
                    if (Directory.Exists(volumePath))
                    {
                        _tracer.RelatedEvent(EventLevel.Informational, "VolumeAvailable", new EventMetadata
                        {
                            ["Volume"] = volumePath,
                        });

                        // Invoke all the registered callbacks for this volume
                        foreach (Action<string> callback in _volumeCallbacks[volumePath])
                        {
                            callback.Invoke(volumePath);
                        }

                        // Unregister the callbacks for this volume
                        _volumeCallbacks.Remove(volumePath);
                    }
                }

                // If there are still volumes that have not become available yet, keep polling
                if (_volumeCallbacks.Any())
                {
                    _pollingTimer.Start();
                }
                else
                {
                    _tracer.RelatedInfo($"{nameof(VolumeWatcher)}: No more callbacks to service. Finished polling for volume availability.");
                }
            }
        }
    }
}
