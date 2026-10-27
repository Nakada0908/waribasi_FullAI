using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Waribasi.Rules;

namespace Waribasi.MatchFlow.Tests
{
    public sealed class MatchPrivacyTests
    {
        [Test]
        public void HiddenCardsRevivalFlagsAndRandomDoNotChangePublicPayload()
        {
            Guid id = Guid.NewGuid();
            var first = new MatchAuthority(id, RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1),
                PlayerId.PlayerA, playerACards: new[] { new CardDefinition(CardKind.Defense) },
                playerBCards: new[] { new CardDefinition(CardKind.Seal) }, random: new RuleRandom(1)), 30, 100);
            var second = new MatchAuthority(id, RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1),
                PlayerId.PlayerA, playerACards: new[] { new CardDefinition(CardKind.SelfSingle, 4) },
                playerBCards: new[] { new CardDefinition(CardKind.DrawThree) },
                playerARevivalAcquired: true, playerBRevivalAcquired: true, random: new RuleRandom(9999)), 30, 100);
            EqualGraph(MatchMessages.Public(first, 0, 1, 3, 100), MatchMessages.Public(second, 0, 1, 3, 100));
        }

        [TestCase(PlayerId.PlayerA, CardKind.Defense)]
        [TestCase(PlayerId.PlayerB, CardKind.Seal)]
        public void PrivateProjectionContainsOnlyTheAuthenticatedPlayersHand(PlayerId player, CardKind expected)
        {
            var match = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1),
                PlayerId.PlayerA, playerACards: new[] { new CardDefinition(CardKind.Defense) },
                playerBCards: new[] { new CardDefinition(CardKind.Seal) }), 30, 100);
            var payload = MatchMessages.OwnHand(match, player);
            Assert.That(payload.player, Is.EqualTo((int)player));
            Assert.That(payload.cards.Length, Is.EqualTo(1));
            Assert.That(payload.cards[0].kind, Is.EqualTo((int)expected));
            Assert.That(payload.cards[0].ToCard().Definition.Kind, Is.EqualTo(expected));
            Assert.That(typeof(PublicMatchMessage).GetFields().Any(f => f.FieldType == typeof(OwnHandMessage)), Is.False);
            Assert.That(typeof(OwnHandMessage).GetFields().Count(f => f.FieldType == typeof(OwnCardMessage[])), Is.EqualTo(1));
        }

        [Test]
        public void UsedCardPublishesDefinitionWithoutPrivateCardId()
        {
            var match = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1),
                PlayerId.PlayerA, playerACards: new[] { new CardDefinition(CardKind.Defense) }), 30, 100);
            Assert.That(match.Submit(PlayerId.PlayerA, new MatchRequest(match.MatchId, 1, 0,
                MatchOperation.UseCard, target: 0, cardId: 1), 100).Accepted, Is.True);
            var payload = MatchMessages.Public(match, 0, 1, 3, 100);
            Assert.That(payload.events.Single(e => e.type == (int)RuleEventType.CardUsed).cardUse.kind, Is.EqualTo((int)CardKind.Defense));
            Assert.That(typeof(CardUseMessage).GetFields().Any(f => f.Name.ToLowerInvariant().Contains("id")), Is.False);
            Assert.That(MatchMessages.OwnHand(match, PlayerId.PlayerA).cards, Is.Empty);
        }

        [Test]
        public void LongHistoryIsPaginatedWithoutTruncatingTheStoredHistory()
        {
            var draws = Enumerable.Repeat(new[] { 85, 0 }, 200).SelectMany(x => x);
            var cards = new[] { new CardDefinition(CardKind.DrawTwo) };
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 3), new PlayerHands(1, 3), PlayerId.PlayerA,
                playerACards: cards, playerBCards: cards, random: RuleRandom.FromSequence(draws));
            var match = new MatchAuthority(Guid.NewGuid(), engine, 0, 100);
            ulong[] ids = { 0, 0 };
            for (int i = 0; i < 20; i++)
            {
                var player = match.State.CurrentPlayer;
                long cardId = match.GetObservation(player).OwnHand[0].Id;
                Assert.That(match.Submit(player, new MatchRequest(match.MatchId, ++ids[(int)player], match.State.StateVersion,
                    MatchOperation.UseCard, cardId: cardId), 100 + i * 2).Accepted, Is.True);
                var hands = match.State.GetHands(player);
                Assert.That(match.Submit(player, new MatchRequest(match.MatchId, ++ids[(int)player], match.State.StateVersion,
                    MatchOperation.Redistribute, left: hands.Right, right: hands.Left), 100 + i * 2).Accepted, Is.True);
                match.Tick(101 + i * 2);
            }
            Assert.That(match.State.CardHistory.Count, Is.EqualTo(20));
            var one = MatchMessages.History(match, 0);
            var two = MatchMessages.History(match, 8);
            var three = MatchMessages.History(match, 16);
            Assert.That(one.records.Length, Is.EqualTo(8));
            Assert.That(two.records.Length, Is.EqualTo(8));
            Assert.That(three.records.Length, Is.EqualTo(4));
            Assert.That(one.total, Is.EqualTo(20));
            Assert.That(one.records.Concat(two.records).Concat(three.records).Select(r => r.stateVersion).Distinct().Count(), Is.EqualTo(20));
        }

        [Test]
        public void GateOnlyChangeHasANewDeliverySequenceWithTheSameRuleVersion()
        {
            var match = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerA), 30, 100);
            match.Submit(PlayerId.PlayerA, new MatchRequest(match.MatchId, 1, 0, MatchOperation.Attack, 0, 0), 100);
            var before = MatchMessages.Public(match, 0, 1, 3, 100);
            match.Tick(101);
            var after = MatchMessages.Public(match, 0, 1, 3, 101);
            Assert.That(after.stateVersion, Is.EqualTo(before.stateVersion));
            Assert.That(after.sequence, Is.GreaterThan(before.sequence));
            Assert.That(after.events, Is.Empty);
            Assert.That(after.hasDeadline, Is.True);
        }

        private static void EqualGraph(object first, object second)
        {
            if (first == null || second == null)
            {
                Assert.That(second, Is.EqualTo(first));
                return;
            }
            Assert.That(second.GetType(), Is.EqualTo(first.GetType()));
            var type = first.GetType();
            if (type.IsPrimitive || first is string || type.IsEnum)
            {
                Assert.That(second, Is.EqualTo(first));
                return;
            }
            if (first is IEnumerable sequence)
            {
                var left = sequence.Cast<object>().ToArray();
                var right = ((IEnumerable)second).Cast<object>().ToArray();
                Assert.That(right.Length, Is.EqualTo(left.Length));
                for (int i = 0; i < left.Length; i++)
                {
                    EqualGraph(left[i], right[i]);
                }
                return;
            }
            foreach (var field in type.GetFields())
            {
                EqualGraph(field.GetValue(first), field.GetValue(second));
            }
        }
    }
}
