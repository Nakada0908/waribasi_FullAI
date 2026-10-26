using System.Collections.Generic;

namespace Waribasi.Rules
{
    public enum ActionRejection
    {
        None,
        InvalidPlayer,
        InvalidHandSide,
        InvalidAction,
        MatchNotPreparing,
        MatchNotActive,
        StaleState,
        NotYourTurn,
        SourceHandEliminated,
        TargetHandEliminated,
        FingerCountOutOfRange,
        TotalNotPreserved,
        DistributionUnchanged,
        InvalidEndReason
    }

    public enum RuleEventType
    {
        MatchStarted,
        ActionAccepted,
        ActionRejected,
        HandChanged,
        HandEliminated,
        HandRevived,
        TurnChanged,
        MatchEnded
    }

    public sealed class RuleEvent
    {
        public RuleEventType Type { get; }
        public PlayerId? Player { get; }
        public HandSide? Hand { get; }
        public int? PreviousCount { get; }
        public int? Count { get; }
        public ActionRejection Rejection { get; }
        public MatchEndReason EndReason { get; }

        internal RuleEvent(RuleEventType type, PlayerId? player = null, HandSide? hand = null,
            int? previousCount = null, int? count = null,
            ActionRejection rejection = ActionRejection.None,
            MatchEndReason endReason = MatchEndReason.None)
        {
            Type = type;
            Player = player;
            Hand = hand;
            PreviousCount = previousCount;
            Count = count;
            Rejection = rejection;
            EndReason = endReason;
        }
    }

    public sealed class ActionResult
    {
        public bool Accepted { get; }
        public ActionRejection Rejection { get; }
        public GameState State { get; }
        public IReadOnlyList<RuleEvent> Events { get; }

        internal ActionResult(bool accepted, ActionRejection rejection, GameState state,
            IEnumerable<RuleEvent> events)
        {
            Accepted = accepted;
            Rejection = rejection;
            State = state;
            Events = new List<RuleEvent>(events).AsReadOnly();
        }
    }
}
