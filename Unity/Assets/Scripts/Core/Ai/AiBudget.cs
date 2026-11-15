using System.Collections.Generic;

namespace Fnmw.Core.Ai
{
    /// <summary>
    /// A safety net against runaway requests, on top of each feature's own cooldown.
    /// Pure: the caller passes the time, so tests need no clock.
    /// </summary>
    public sealed class AiBudget
    {
        public int MaxPerSession = 80;
        public int MaxConcurrent = 2;
        /// <summary>Seconds between two requests of the same feature.</summary>
        public float MinIntervalPerFeature = 2f;

        readonly Dictionary<string, float> _lastStart = new Dictionary<string, float>();
        public int Started { get; private set; }
        public int InFlight { get; private set; }

        /// <summary>Returns null and counts the request when it may start, otherwise the reason it may not.</summary>
        public string TryStart(string feature, float now)
        {
            feature ??= "misc";
            if (Started >= MaxPerSession) return "session limit of " + MaxPerSession + " requests reached";
            if (InFlight >= MaxConcurrent) return InFlight + " requests already in flight";
            if (_lastStart.TryGetValue(feature, out float last) && now - last < MinIntervalPerFeature)
                return "'" + feature + "' asked again within " + MinIntervalPerFeature + " s";
            _lastStart[feature] = now;
            Started++;
            InFlight++;
            return null;
        }

        /// <summary>Call once for every request TryStart allowed, however it ended.</summary>
        public void End()
        {
            if (InFlight > 0) InFlight--;
        }
    }
}
