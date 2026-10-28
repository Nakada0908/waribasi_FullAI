using System;

namespace Waribasi.MatchFlow
{
    public sealed class ReadableDisplayTiming
    {
        private string match;
        private long version = -1;
        private double readableAt = -1;

        public void Observe(string matchId, long stateVersion, bool readable, double now)
        {
            if (match != matchId || version != stateVersion)
            {
                match = matchId;
                version = stateVersion;
                Invalidate();
            }
            if (!readable || string.IsNullOrEmpty(matchId))
            {
                Invalidate();
            }
            else if (readableAt < 0)
            {
                readableAt = now;
            }
        }

        public void Invalidate()
        {
            readableAt = -1;
        }

        public bool IsComplete(string matchId, long stateVersion, double now, double minimumSeconds)
        {
            return readableAt >= 0 && match == matchId && version == stateVersion &&
                now - readableAt >= Math.Max(0, minimumSeconds);
        }
    }
}
