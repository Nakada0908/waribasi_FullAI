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
        InvalidEndReason,
        //追加:カード・初期配布・封印の拒否を区別する。
        InitialCardsNotDealt,
        InitialCardsAlreadyDealt,
        CardAlreadyUsedThisTurn,
        CardNotFound,
        AutomaticCardOnly,
        InvalidCardTarget,
        CardUnchanged,
        SourceHandSealed,
        //追加:v1.1の封印・片手交換・条件付きスキップの拒否理由。
        OwnHandSealed,
        SingleHandSwap,
        SkipNotAllowed
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
        MatchEnded,
        //追加:すべて公開可能な通知だけを定義する。
        CardsDrawn,
        CardUsed,
        EffectApplied,
        EffectRemoved,
        ChangeBlocked,
        //追加:カード成立とは別の通常行動として公開する。
        TurnSkipped
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

        //追加:札内容は使用済みの公開記録だけ。補充は枚数のみ。
        public CardUseRecord CardUse { get; }
        public int? DrawnCount { get; }
        public EffectKind? Effect { get; }
        public long? ExpiresAfterTurn { get; }
        public EffectRemovalReason? RemovalReason { get; }

        internal RuleEvent(RuleEventType type, PlayerId? player = null, HandSide? hand = null,
            int? previousCount = null, int? count = null,
            ActionRejection rejection = ActionRejection.None,
            //変更:公開するカード使用・効果の情報を受け取る。
            MatchEndReason endReason = MatchEndReason.None, CardUseRecord cardUse = null,
            int? drawnCount = null, EffectKind? effect = null, long? expiresAfterTurn = null,
            EffectRemovalReason? removalReason = null)
        {
            Type = type;
            Player = player;
            Hand = hand;
            PreviousCount = previousCount;
            Count = count;
            Rejection = rejection;
            EndReason = endReason;
            //追加:通知には未使用札・抽選状態を保持しない。
            CardUse = cardUse;
            DrawnCount = drawnCount;
            Effect = effect;
            ExpiresAfterTurn = expiresAfterTurn;
            RemovalReason = removalReason;
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
