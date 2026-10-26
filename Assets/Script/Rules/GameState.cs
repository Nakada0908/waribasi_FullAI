using System;
//追加:公開履歴を変更不能な一覧として保持する。
using System.Collections.Generic;

namespace Waribasi.Rules
{
    public enum PlayerId
    {
        PlayerA,
        PlayerB
    }

    public enum HandSide
    {
        Left,
        Right
    }

    public enum MatchPhase
    {
        Preparing,
        Active,
        Finished,
        Interrupted
    }

    public enum TurnPhase
    {
        CardOrNormalAction,
        NormalActionOnly
    }

    public enum MatchEndReason
    {
        None,
        HandsEliminated,
        Surrender,
        Timeout,
        Disconnected,
        CommunicationInterrupted
    }

    public readonly struct PlayerHands
    {
        public int Left { get; }
        public int Right { get; }
        public int Total => Left + Right;
        public bool IsEliminated => Total == 0;

        public PlayerHands(int left, int right)
        {
            if (left < 0 || left > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(left));
            }

            if (right < 0 || right > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(right));
            }

            Left = left;
            Right = right;
        }

        public int GetCount(HandSide side)
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

        internal PlayerHands WithCount(HandSide side, int count)
        {
            switch (side)
            {
                case HandSide.Left:
                    return new PlayerHands(count, Right);
                case HandSide.Right:
                    return new PlayerHands(Left, count);
                default:
                    throw new ArgumentOutOfRangeException(nameof(side));
            }
        }
    }

    public sealed class GameState
    {
        public PlayerHands PlayerA { get; }
        public PlayerHands PlayerB { get; }
        public PlayerId CurrentPlayer { get; }
        public MatchPhase MatchPhase { get; }
        public TurnPhase TurnPhase { get; }
        public long TurnNumber { get; }
        public long StateVersion { get; }
        public PlayerId? Winner { get; }
        public PlayerId? Loser => Winner.HasValue ? GetOpponent(Winner.Value) : (PlayerId?)null;
        public MatchEndReason EndReason { get; }

        //追加:公開情報には実手札や復活札の未公開入手状況を含めない。
        public PlayerEffects PlayerAEffects { get; }
        public PlayerEffects PlayerBEffects { get; }
        public int PlayerACardCount { get; }
        public int PlayerBCardCount { get; }
        public bool InitialCardsDealt { get; }
        public IReadOnlyList<CardUseRecord> CardHistory { get; }

        internal GameState(PlayerHands playerA, PlayerHands playerB, PlayerId currentPlayer,
            MatchPhase matchPhase, TurnPhase turnPhase, long turnNumber, long stateVersion,
            //変更:公開効果・枚数・履歴を盤面と同じ版に固定する。
            PlayerId? winner = null, MatchEndReason endReason = MatchEndReason.None,
            PlayerEffects playerAEffects = default, PlayerEffects playerBEffects = default,
            int playerACardCount = 0, int playerBCardCount = 0,
            IEnumerable<CardUseRecord> cardHistory = null, bool initialCardsDealt = false)
        {
            PlayerA = playerA;
            PlayerB = playerB;
            CurrentPlayer = currentPlayer;
            MatchPhase = matchPhase;
            TurnPhase = turnPhase;
            TurnNumber = turnNumber;
            StateVersion = stateVersion;
            Winner = winner;
            EndReason = endReason;
            //追加:外部から渡された履歴も複製して読み取り専用にする。
            PlayerAEffects = playerAEffects;
            PlayerBEffects = playerBEffects;
            PlayerACardCount = playerACardCount;
            PlayerBCardCount = playerBCardCount;
            CardHistory = new List<CardUseRecord>(cardHistory ?? Array.Empty<CardUseRecord>()).AsReadOnly();
            InitialCardsDealt = initialCardsDealt;
        }

        //追加:公開状態の左右効果・手札枚数を席から読み取る。
        public PlayerEffects GetEffects(PlayerId player)
        {
            if (!IsValidPlayer(player))
            {
                throw new ArgumentOutOfRangeException(nameof(player));
            }

            return player == PlayerId.PlayerA ? PlayerAEffects : PlayerBEffects;
        }

        public int GetCardCount(PlayerId player)
        {
            if (!IsValidPlayer(player))
            {
                throw new ArgumentOutOfRangeException(nameof(player));
            }

            return player == PlayerId.PlayerA ? PlayerACardCount : PlayerBCardCount;
        }

        public PlayerHands GetHands(PlayerId player)
        {
            switch (player)
            {
                case PlayerId.PlayerA:
                    return PlayerA;
                case PlayerId.PlayerB:
                    return PlayerB;
                default:
                    throw new ArgumentOutOfRangeException(nameof(player));
            }
        }

        public static PlayerId GetOpponent(PlayerId player)
        {
            switch (player)
            {
                case PlayerId.PlayerA:
                    return PlayerId.PlayerB;
                case PlayerId.PlayerB:
                    return PlayerId.PlayerA;
                default:
                    throw new ArgumentOutOfRangeException(nameof(player));
            }
        }

        internal static bool IsValidPlayer(PlayerId player)
        {
            return player == PlayerId.PlayerA || player == PlayerId.PlayerB;
        }

        internal static bool IsValidSide(HandSide side)
        {
            return side == HandSide.Left || side == HandSide.Right;
        }
    }
}
