using System;
using System.Collections.Generic;

namespace SolarSystemApp.Gameplay
{
    internal enum ShipJobType
    {
        None = 0,
        Mine = 1,
        Haul = 2,
        Patrol = 3
    }

    internal sealed class ShipJobState
    {
        public ShipJobType Job = ShipJobType.None;

        // super simple progression counters (keeps current functionality intact)
        public double Accumulator = 0.0;
        public int Completed = 0;

        // optional target info
        public string TargetLabel = "";
    }

    internal sealed class ShipJobs
    {
        // PERF: ordinal comparer avoids culture costs and is deterministic
        private readonly Dictionary<string, ShipJobState> _jobs = new(StringComparer.Ordinal);

        public ShipJobState GetOrCreate(string shipName)
        {
            if (string.IsNullOrWhiteSpace(shipName)) shipName = "UNKNOWN";

            if (!_jobs.TryGetValue(shipName, out var st))
            {
                st = new ShipJobState();
                _jobs[shipName] = st;
            }
            return st;
        }
    }
}
