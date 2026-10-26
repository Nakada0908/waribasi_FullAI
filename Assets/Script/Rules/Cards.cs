using System;
using System.Collections.Generic;
using System.Linq;

namespace Waribasi.Rules
{
    public enum CardKind
    {
        SelfSingle,
        OpponentSingle,
        SelfBoth,
        OpponentBoth,
        Defense,
        Seal,
        DrawTwo,
        DrawThree,
        DrawRandom,
        Revival
    }

    public sealed class CardDefinition
    {
        public CardKind Kind { get; }
        public int First { get; }
        public int Second { get; }
        public bool IsNumber => Kind <= CardKind.OpponentBoth;
        public bool IsBoth => Kind == CardKind.SelfBoth || Kind == CardKind.OpponentBoth;
        public bool IsSelf => Kind == CardKind.SelfSingle || Kind == CardKind.SelfBoth || Kind == CardKind.Defense;
        public bool IsDraw => Kind == CardKind.DrawTwo || Kind == CardKind.DrawThree || Kind == CardKind.DrawRandom;
        public bool NeedsHand => Kind == CardKind.SelfSingle || Kind == CardKind.OpponentSingle ||
            Kind == CardKind.Defense || Kind == CardKind.Seal;

        public CardDefinition(CardKind kind, int first = 0, int second = 0)
        {
            if (kind < CardKind.SelfSingle || kind > CardKind.Revival)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Kind = kind;
            if (IsNumber)
            {
                if (first < 1 || first > 4 || (IsBoth ? second < 1 || second > 4 : second != 0))
                {
                    throw new ArgumentOutOfRangeException(nameof(first));
                }
            }
            else if (first != 0 || second != 0)
            {
                throw new ArgumentException("Only number cards have finger counts.");
            }

            First = IsBoth ? Math.Min(first, second) : first;
            Second = IsBoth ? Math.Max(first, second) : second;
        }
    }

    public sealed class HandCard
    {
        public long Id { get; }
        public CardDefinition Definition { get; }

        public HandCard(long id, CardDefinition definition)
        {
            if (id <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            Id = id;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }
    }

    public sealed class CardUseRecord
    {
        public PlayerId Player { get; }
        public CardDefinition Card { get; }
        public PlayerId? TargetPlayer { get; }
        public HandSide? Hand { get; }
        public bool SwapValues { get; }
        public bool Automatic { get; }
        public int RequestedDrawCount { get; }
        public int DrawnCount { get; }
        public long TurnNumber { get; }
        public long StateVersion { get; }

        internal CardUseRecord(PlayerId player, CardDefinition card, PlayerId? targetPlayer,
            HandSide? hand, bool swapValues, bool automatic, int requestedDrawCount, int drawnCount,
            long turnNumber, long stateVersion)
        {
            Player = player;
            Card = card;
            TargetPlayer = targetPlayer;
            Hand = hand;
            SwapValues = swapValues;
            Automatic = automatic;
            RequestedDrawCount = requestedDrawCount;
            DrawnCount = drawnCount;
            TurnNumber = turnNumber;
            StateVersion = stateVersion;
        }
    }

    internal sealed class PrivateCards
    {
        internal IReadOnlyList<HandCard> Hand { get; }
        internal bool RevivalAcquired { get; }
        internal long NextId { get; }

        internal PrivateCards(IEnumerable<HandCard> hand = null, bool revivalAcquired = false, long nextId = 1)
        {
            var copy = new List<HandCard>(hand ?? Array.Empty<HandCard>());
            if (copy.Count > 5 || copy.Any(c => c == null) || copy.Select(c => c.Id).Distinct().Count() != copy.Count ||
                copy.Count(c => c.Definition.Kind == CardKind.Revival) > 1 ||
                nextId <= 0 || copy.Any(c => c.Id >= nextId))
            {
                throw new ArgumentException("Invalid private hand.");
            }

            Hand = copy.AsReadOnly();
            RevivalAcquired = revivalAcquired || copy.Any(c => c.Definition.Kind == CardKind.Revival);
            NextId = nextId;
        }

        internal static PrivateCards FromDefinitions(IEnumerable<CardDefinition> definitions, bool revivalAcquired)
        {
            long id = 1;
            var hand = (definitions ?? Array.Empty<CardDefinition>()).Select(c => new HandCard(id++, c)).ToList();
            return new PrivateCards(hand, revivalAcquired, id);
        }

        internal PrivateCards Add(CardDefinition card)
        {
            if (card.Kind == CardKind.Revival && RevivalAcquired)
            {
                throw new InvalidOperationException("Revival has already been acquired.");
            }

            return new PrivateCards(Hand.Concat(new[] { new HandCard(NextId, card) }),
                RevivalAcquired || card.Kind == CardKind.Revival, checked(NextId + 1));
        }

        internal PrivateCards Remove(long id)
        {
            return new PrivateCards(Hand.Where(c => c.Id != id), RevivalAcquired, NextId);
        }
    }
}
