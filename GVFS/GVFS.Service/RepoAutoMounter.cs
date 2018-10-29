using System;
using System.IO;
using System.Timers;
using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Service.Handlers;

namespace GVFS.Service
{
    public class RepoAutoMounter : IDisposable
    {
        private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(15);

        private readonly ITracer tracer;
        private readonly RepoRegistry repoRegistry;
        private readonly int sessionId;
        private readonly GVFSMountProcess mountProcess;
        private readonly string userSid;
        private readonly Timer volumeTimer;

        public RepoAutoMounter(ITracer tracer, RepoRegistry repoRegistry, int sessionId)
        {
            this.tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
            this.repoRegistry = repoRegistry ?? throw new ArgumentNullException(nameof(repoRegistry));
            this.sessionId = sessionId;

            // Create a mount process factory for this session/user
            this.mountProcess = new GVFSMountProcess(this.tracer, sessionId);
            this.userSid = this.mountProcess.CurrentUser.Identity.User?.Value;

            this.volumeTimer = new Timer(PollingInterval.TotalMilliseconds)
            {
                // Don't want to auto reset and keep polling.
                // Only reset the polling timer if there are still some repositories to mount.
                AutoReset = false,
            };
            this.volumeTimer.Elapsed += this.OnVolumeTimerElapsed;
        }

        public void Start()
        {
            this.tracer.RelatedInfo("Starting auto mounter for session {0}", this.sessionId);

            // Try mounting all the user's active repo straight away.
            // This will start the volume polling retry-loop if required.
            this.MountAll();
        }

        public void Dispose()
        {
            this.volumeTimer.Stop();
            this.volumeTimer.Elapsed -= this.OnVolumeTimerElapsed;
            this.volumeTimer.Dispose();
            this.mountProcess.Dispose();
        }

        private void MountAll()
        {
            bool allVolumesWereAvailable = true;

            if (this.repoRegistry.TryGetActiveReposForUser(this.userSid, out var activeRepos, out string errorMessage))
            {
                foreach (RepoRegistration repo in activeRepos)
                {
                    // Only try to mount the registered repository if the parent volume is available
                    string volumeRoot = GVFSPlatform.Instance.FileSystem.GetVolumeRoot(repo.EnlistmentRoot);
                    bool volumeAvailable = Directory.Exists(volumeRoot);

                    if (volumeAvailable)
                    {
                        this.Mount(repo.EnlistmentRoot);
                    }
                    else
                    {
                        allVolumesWereAvailable = false;
                    }
                }
            }
            else
            {
                this.tracer.RelatedError("Could not get repos to auto mount for user. Error: " + errorMessage);
            }

            // We didn't see all volumes as available; ensure we're polling for volume availability so we can retry
            if (allVolumesWereAvailable)
            {
                this.tracer.RelatedInfo("All volumes were available to try mounting registered repos. Automount complete.");
            }
            else
            {
                this.tracer.RelatedInfo($"Not all volumes were available to mount registered repos. Will retry after {PollingInterval.TotalMilliseconds} ms.");
                this.volumeTimer.Start();
            }
        }

        private void Mount(string enlistmentRoot)
        {
            var metadata = new EventMetadata
            {
                ["EnlistmentRoot"] = enlistmentRoot
            };

            using (var activity = this.tracer.StartActivity("AutoMount", EventLevel.Informational, metadata))
            {
                // TODO #1043088: We need to respect the elevation level of the original mount
                if (this.mountProcess.Mount(enlistmentRoot))
                {
                    this.SendNotification("GVFS AutoMount", "The following GVFS repo is now mounted:\n{0}", enlistmentRoot);
                    activity.RelatedInfo("Auto mount was successful for '{0}'", enlistmentRoot);
                }
                else
                {
                    this.SendNotification("GVFS AutoMount", "The following GVFS repo failed to mount:\n{0}", enlistmentRoot);
                    activity.RelatedError("Failed to auto mount '{0}'", enlistmentRoot);
                }
            }
        }

        private void OnVolumeTimerElapsed(object sender, ElapsedEventArgs e)
        {
            this.MountAll();
        }

        private void SendNotification(string title, string format, params object[] args)
        {
            var request = new NamedPipeMessages.Notification.Request
            {
                Title = title,
                Message = string.Format(format, args)
            };

            NotificationHandler.Instance.SendNotification(this.tracer, this.sessionId, request);
        }
    }
}
