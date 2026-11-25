using System;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>Clock handed to plugins.</summary>
    public class HostTimeController : ITimeController
    {
        public DateTime Now { get { return DateTime.Now; } }

        public IWatch CreateWatch()
        {
            Watch watch = new Watch();
            // Watch starts itself for normal host uptime, but the factory
            // contract for CreateWatch is a stopped, zero-length stopwatch.
            watch.Stop();
            watch.Reset();
            watch.Stop();
            return watch;
        }

        public IWatch CreateAndStartWatch()
        {
            Watch watch = new Watch();
            // Keep this explicit: it documents that this factory differs from
            // CreateWatch even if Watch's constructor implementation changes.
            watch.Start();
            return watch;
        }
    }
}
