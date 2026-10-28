using System;
using System.Linq;
using NUnit.Framework;
using Waribasi.Rules;

namespace Waribasi.Npc.Tests
{
    public sealed class StrongestNpcTests
    {
        private static NpcSearchOptions Exact => new NpcSearchOptions
        {
            MaxMilliseconds = 0, RootWidth = 88, ReplyWidth = 88, DrawSamples = 1,
            MaxApplications = 200000, MaxWorkItems = 1000000, NearScore = 0
        };

        [Test]
        public void CurrentRevivalChangesEvaluationWithIdenticalOwnObservation()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(2, 1), new PlayerHands(3, 0), PlayerId.PlayerA,
                playerBCards: new[] { new CardDefinition(CardKind.Defense) });
            var observation = engine.GetObservation(PlayerId.PlayerA);
            using (var plain = StrongestNpc.CreateSearch(observation, new[] { new CardDefinition(CardKind.Defense) }, new RuleRandom(7), Exact))
            using (var revival = StrongestNpc.CreateSearch(observation, new[] { new CardDefinition(CardKind.Revival) }, new RuleRandom(7), Exact))
            {
                plain.CompleteWithinBudget();
                revival.CompleteWithinBudget();
                Assert.That(plain.Decision.Evaluation.WinWeight, Is.EqualTo(1));
                Assert.That(revival.Decision.Evaluation.WinWeight, Is.LessThan(1));
                Assert.That(plain.Statistics.Hypotheses, Is.EqualTo(1));
                Assert.That(revival.Decision.Action, Is.Not.Null);
            }
        }

        [Test]
        public void SearchNeverAdvancesFormalStateOrFormalDrawRandom()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(2, 3), PlayerId.PlayerA,
                playerACards: new[] { new CardDefinition(CardKind.DrawRandom) },
                playerBCards: new[] { new CardDefinition(CardKind.Revival) }, random: new RuleRandom(1234));
            var control = engine.Fork();
            var snapshot = engine.Snapshot;
            var observation = engine.GetObservation(PlayerId.PlayerA);
            using (var search = StrongestNpc.CreateSearch(observation,
                engine.GetObservation(PlayerId.PlayerB).OwnHand.Select(card => card.Definition), new RuleRandom(7), Exact))
            {
                search.CompleteWithinBudget();
                Assert.That(engine.Snapshot, Is.SameAs(snapshot));
                var action = new UseCardAction(observation.OwnHand[0].Id);
                Assert.That(engine.TryApply(PlayerId.PlayerA, action, 0).Accepted, Is.True);
                control.TryApply(PlayerId.PlayerA, action, 0);
                var actual = engine.GetObservation(PlayerId.PlayerA).OwnHand;
                var expected = control.GetObservation(PlayerId.PlayerA).OwnHand;
                Assert.That(actual.Select(c => $"{c.Id}:{c.Definition.Kind}:{c.Definition.First}:{c.Definition.Second}"),
                    Is.EqualTo(expected.Select(c => $"{c.Id}:{c.Definition.Kind}:{c.Definition.First}:{c.Definition.Second}")));
            }
        }

        [Test]
        public void CardActionIsReconsideredWithNewObservationAndRemainsLegal()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(3, 0), PlayerId.PlayerA,
                playerACards: new[] { new CardDefinition(CardKind.SelfSingle, 2) });
            for (int phase = 0; phase < 2; phase++)
            {
                using (var search = StrongestNpc.CreateSearch(engine.GetObservation(PlayerId.PlayerA),
                    Array.Empty<CardDefinition>(), new RuleRandom(7), Exact))
                {
                    search.CompleteWithinBudget();
                    Assert.That(search.Decision.Action is UseCardAction, Is.EqualTo(phase == 0));
                    Assert.That(engine.TryApply(PlayerId.PlayerA, search.Decision.Action, engine.Snapshot.StateVersion).Accepted, Is.True);
                }
            }
            Assert.That(engine.Snapshot.Winner, Is.EqualTo(PlayerId.PlayerA));
        }

        [Test]
        public void InvalidCountIsRefusedAndCancellationDiscardsAction()
        {
            var observation = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerA).GetObservation(PlayerId.PlayerA);
            Assert.Throws<ArgumentException>(() => StrongestNpc.CreateSearch(observation,
                new[] { new CardDefinition(CardKind.Defense) }, new RuleRandom(1)));
            using (var search = StrongestNpc.CreateSearch(observation, Array.Empty<CardDefinition>(), new RuleRandom(7)))
            {
                search.Advance(1);
                search.Dispose();
                Assert.That(search.Decision.Action, Is.Null);
                Assert.That(search.Statistics.Stop, Is.EqualTo(NpcSearchStop.Cancelled));
            }
        }

        [Test]
        public void BoundedSearchReturnsLegalFallback()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerA);
            var options = StrongestNpc.CreateOptions();
            options.MaxApplications = 1;
            options.MaxMilliseconds = 0;
            using (var search = StrongestNpc.CreateSearch(engine.GetObservation(PlayerId.PlayerA), Array.Empty<CardDefinition>(), new RuleRandom(7), options))
            {
                search.CompleteWithinBudget();
                Assert.That(search.Statistics.Applications, Is.LessThanOrEqualTo(1));
                Assert.That(engine.TryApply(PlayerId.PlayerA, search.Decision.Action, 0).Accepted, Is.True);
            }
        }
    }
}
