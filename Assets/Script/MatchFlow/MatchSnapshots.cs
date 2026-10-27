using System;
using System.Linq;
using Waribasi.Rules;

namespace Waribasi.MatchFlow
{
    [Serializable]
    public sealed class HandStateMessage
    {
        public int count;
        public long defenseUntil;
        public long sealUntil;
    }

    [Serializable]
    public sealed class CardUseMessage
    {
        public int player;
        public int kind;
        public int first;
        public int second;
        public int targetPlayer;
        public int targetHand;
        public bool swapValues;
        public bool automatic;
        public int requestedDrawCount;
        public int drawnCount;
        public long turnNumber;
        public long stateVersion;
    }

    [Serializable]
    public sealed class RuleEventMessage
    {
        public int type;
        public int player;
        public int hand;
        public int previousCount;
        public int count;
        public int drawnCount;
        public int effect;
        public long expiresAfterTurn;
        public int removalReason;
        public int endReason;
        public CardUseMessage cardUse;
    }

    [Serializable]
    public sealed class PublicMatchMessage
    {
        public string matchId;
        public long sequence;
        public long stateVersion;
        public long turnNumber;
        public ulong playerAClientId;
        public ulong playerBClientId;
        public int currentPlayer;
        public int matchPhase;
        public int turnPhase;
        public int gate;
        public int preparationStage;
        public int turnSeconds;
        public double serverTime;
        public bool hasDeadline;
        public double deadline;
        public double presentationUntil;
        public int winner;
        public int endReason;
        public bool initialCardsDealt;
        public int playerACardCount;
        public int playerBCardCount;
        public int historyCount;
        //順番はA左・A右・B左・B右。
        public HandStateMessage[] hands;
        public RuleEventMessage[] events;
    }

    [Serializable]
    public sealed class OwnCardMessage
    {
        public long id;
        public int kind;
        public int first;
        public int second;

        public HandCard ToCard()
        {
            return new HandCard(id, new CardDefinition((CardKind)kind, first, second));
        }
    }

    [Serializable]
    public sealed class OwnHandMessage
    {
        public string matchId;
        public long stateVersion;
        public int player;
        public bool revivalAcquired;
        public OwnCardMessage[] cards;
    }

    [Serializable]
    public sealed class HistoryPageMessage
    {
        public string matchId;
        public int offset;
        public int total;
        public CardUseMessage[] records;
    }

    [Serializable]
    public sealed class RequestResponseMessage
    {
        public string matchId;
        public ulong requestId;
        public bool accepted;
        public int rejection;
        public int ruleRejection;
        public long stateVersion;
        public bool replayed;
    }

    public static class MatchMessages
    {
        public const int HistoryPageSize = 8;
        public const int MaximumServerMessageCharacters = 16384;

        //追加:ローカル表示では接続IDを割り当てず、本人の席は進行側から直接渡す。
        public static PublicMatchMessage LocalPublic(MatchAuthority authority, int stage, double now)
        {
            return Public(authority, 0, 0, stage, now);
        }

        public static PublicMatchMessage Public(MatchAuthority authority, ulong clientA, ulong clientB,
            int preparationStage, double now)
        {
            var state = authority.State;
            var hands = new HandStateMessage[4];
            for (int seat = 0; seat < 2; seat++)
            {
                for (int side = 0; side < 2; side++)
                {
                    var effect = state.GetEffects((PlayerId)seat).Get((HandSide)side);
                    hands[seat * 2 + side] = new HandStateMessage
                    {
                        count = state.GetHands((PlayerId)seat).GetCount((HandSide)side),
                        defenseUntil = effect.DefenseUntilTurn,
                        sealUntil = effect.SealUntilTurn
                    };
                }
            }
            return new PublicMatchMessage
            {
                matchId = authority.MatchId.ToString("N"), sequence = authority.DeliverySequence,
                stateVersion = state.StateVersion, turnNumber = state.TurnNumber,
                playerAClientId = clientA, playerBClientId = clientB, currentPlayer = (int)state.CurrentPlayer,
                matchPhase = (int)state.MatchPhase, turnPhase = (int)state.TurnPhase, gate = (int)authority.Gate,
                preparationStage = preparationStage, turnSeconds = authority.TurnSeconds, serverTime = now,
                hasDeadline = authority.Deadline.HasValue, deadline = authority.Deadline ?? 0,
                presentationUntil = authority.PresentationUntil, winner = state.Winner.HasValue ? (int)state.Winner.Value : -1,
                endReason = (int)state.EndReason, initialCardsDealt = state.InitialCardsDealt,
                playerACardCount = state.PlayerACardCount, playerBCardCount = state.PlayerBCardCount,
                historyCount = state.CardHistory.Count, hands = hands,
                events = authority.LastResult == null ? Array.Empty<RuleEventMessage>() :
                    authority.LastResult.Events.Select(Event).ToArray()
            };
        }

        //宛先は呼出側が認証した席から決める。公開メッセージへ合成しない。
        public static OwnHandMessage OwnHand(MatchAuthority authority, PlayerId authenticatedPlayer)
        {
            var observation = authority.GetObservation(authenticatedPlayer);
            return new OwnHandMessage
            {
                matchId = authority.MatchId.ToString("N"), stateVersion = observation.PublicState.StateVersion,
                player = (int)authenticatedPlayer, revivalAcquired = observation.RevivalAcquired,
                cards = observation.OwnHand.Select(c => new OwnCardMessage
                {
                    id = c.Id, kind = (int)c.Definition.Kind, first = c.Definition.First, second = c.Definition.Second
                }).ToArray()
            };
        }

        public static HistoryPageMessage History(MatchAuthority authority, int offset)
        {
            var history = authority.State.CardHistory;
            offset = Math.Max(0, Math.Min(offset, history.Count));
            return new HistoryPageMessage
            {
                matchId = authority.MatchId.ToString("N"), offset = offset, total = history.Count,
                records = history.Skip(offset).Take(HistoryPageSize).Select(CardUse).ToArray()
            };
        }

        private static CardUseMessage CardUse(CardUseRecord record)
        {
            return record == null ? null : new CardUseMessage
            {
                player = (int)record.Player, kind = (int)record.Card.Kind,
                first = record.Card.First, second = record.Card.Second,
                targetPlayer = record.TargetPlayer.HasValue ? (int)record.TargetPlayer.Value : -1,
                targetHand = record.Hand.HasValue ? (int)record.Hand.Value : -1,
                swapValues = record.SwapValues, automatic = record.Automatic,
                requestedDrawCount = record.RequestedDrawCount, drawnCount = record.DrawnCount,
                turnNumber = record.TurnNumber, stateVersion = record.StateVersion
            };
        }

        private static RuleEventMessage Event(RuleEvent value)
        {
            return new RuleEventMessage
            {
                type = (int)value.Type, player = value.Player.HasValue ? (int)value.Player.Value : -1,
                hand = value.Hand.HasValue ? (int)value.Hand.Value : -1,
                previousCount = value.PreviousCount ?? -1, count = value.Count ?? -1,
                drawnCount = value.DrawnCount ?? -1, effect = value.Effect.HasValue ? (int)value.Effect.Value : -1,
                expiresAfterTurn = value.ExpiresAfterTurn ?? 0,
                removalReason = value.RemovalReason.HasValue ? (int)value.RemovalReason.Value : -1,
                endReason = (int)value.EndReason, cardUse = CardUse(value.CardUse)
            };
        }
    }
}
