using System;

namespace Waribasi.MatchFlow
{
    public sealed class NpcTurnTiming
    {
        public long TurnNumber { get; private set; } = -1;
        public double ActionNotBefore { get; private set; }
        public double ThinkMilliseconds { get; private set; }
        public double WaitMilliseconds { get; private set; }
        public double RemainingThinkMilliseconds => Math.Max(1, 1000 - ThinkMilliseconds);

        public void ObserveTurn(long turnNumber, double now, double minimumSeconds)
        {
            if (TurnNumber == turnNumber)
            {
                return;
            }
            TurnNumber = turnNumber;
            ActionNotBefore = now + Math.Max(0, minimumSeconds);
            ThinkMilliseconds = WaitMilliseconds = 0;
        }

        public void CardPublished(double now, double minimumSeconds)
        {
            ActionNotBefore = Math.Max(ActionNotBefore, now + Math.Max(0, minimumSeconds));
        }

        public bool CanAct(long turnNumber, double now)
        {
            return TurnNumber == turnNumber && now >= ActionNotBefore;
        }

        public void RecordThinking(double milliseconds)
        {
            ThinkMilliseconds += Math.Max(0, milliseconds);
        }

        public void RecordWaiting(double milliseconds)
        {
            WaitMilliseconds += Math.Max(0, milliseconds);
        }
    }
}
