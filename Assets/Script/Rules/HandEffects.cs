using System;

namespace Waribasi.Rules
{
    public enum EffectKind
    {
        Defense,
        Seal
    }

    public enum EffectRemovalReason
    {
        Expired,
        HandEliminated
    }

    public readonly struct HandEffects
    {
        public long DefenseUntilTurn { get; }
        public long SealUntilTurn { get; }
        public bool IsDefended => DefenseUntilTurn != 0;
        public bool IsSealed => SealUntilTurn != 0;

        public HandEffects(long defenseUntilTurn = 0, long sealUntilTurn = 0)
        {
            if (defenseUntilTurn < 0 || sealUntilTurn < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(defenseUntilTurn));
            }

            DefenseUntilTurn = defenseUntilTurn;
            SealUntilTurn = sealUntilTurn;
        }

        internal HandEffects With(EffectKind kind, long untilTurn)
        {
            return kind == EffectKind.Defense
                ? new HandEffects(untilTurn, SealUntilTurn)
                : new HandEffects(DefenseUntilTurn, untilTurn);
        }
    }

    public readonly struct PlayerEffects
    {
        public HandEffects Left { get; }
        public HandEffects Right { get; }

        public PlayerEffects(HandEffects left, HandEffects right)
        {
            Left = left;
            Right = right;
        }

        public HandEffects Get(HandSide side)
        {
            switch (side)
            {
                case HandSide.Left:
                    return Left;
                case HandSide.Right:
                    return Right;
                default:
                    throw new ArgumentOutOfRangeException(nameof(side));
            }
        }

        internal PlayerEffects With(HandSide side, HandEffects effects)
        {
            return side == HandSide.Left ? new PlayerEffects(effects, Right) : new PlayerEffects(Left, effects);
        }
    }
}
