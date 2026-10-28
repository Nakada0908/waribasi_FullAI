using System;
using System.Linq;
using NUnit.Framework;
using Waribasi.Rules;

namespace Waribasi.Npc.Tests
{
    public sealed class OpponentHandEstimatorTests
    {
        private static CardDefinition[] Initial => new[]
        {
            new CardDefinition(CardKind.SelfSingle, 2), new CardDefinition(CardKind.OpponentSingle, 3),
            new CardDefinition(CardKind.Defense), new CardDefinition(CardKind.Seal), new CardDefinition(CardKind.SelfBoth, 1, 4)
        };

        [Test]
        public void InitialHandsKeepBothGuaranteesAndAtMostOneRevival()
        {
            int revival = 0;
            for (ulong seed = 1; seed <= 100; seed++)
            {
                var engine = new RuleEngine(random: new RuleRandom(seed));
                engine.TryDealInitialCards(0);
                engine.TryStart(1);
                var estimator = new OpponentHandEstimator();
                estimator.Generate(engine.GetObservation(PlayerId.PlayerA), 8, new RuleRandom(seed));
                Assert.That(estimator.Hypotheses.Count, Is.EqualTo(8));
                foreach (var hypothesis in estimator.Hypotheses)
                {
                    Assert.That(hypothesis.Cards.Count, Is.EqualTo(5));
                    Assert.That(hypothesis.Cards.Any(card => card.IsNumber && card.IsSelf), Is.True);
                    Assert.That(hypothesis.Cards.Any(card => card.IsNumber && !card.IsSelf), Is.True);
                    Assert.That(hypothesis.Cards.Count(card => card.Kind == CardKind.Revival), Is.LessThanOrEqualTo(1));
                    revival += hypothesis.Cards.Count(card => card.Kind == CardKind.Revival);
                    Assert.That(hypothesis.RevivalAcquired, Is.EqualTo(hypothesis.Cards.Any(card => card.Kind == CardKind.Revival)));
                }
            }
            Assert.That(revival, Is.InRange(30, 110));
        }

        [TestCase(CardKind.DrawTwo)]
        [TestCase(CardKind.DrawThree)]
        [TestCase(CardKind.DrawRandom)]
        public void RefillUsesThePublicActualCountAtTheHandLimit(CardKind kind)
        {
            var cards = Initial;
            cards[4] = new CardDefinition(kind);
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerB,
                playerBCards: cards, random: new RuleRandom(12));
            var card = engine.GetObservation(PlayerId.PlayerB).OwnHand.Last();
            Assert.That(engine.TryApply(PlayerId.PlayerB, new UseCardAction(card.Id), 0).Accepted, Is.True);
            Assert.That(engine.Snapshot.CardHistory.Single().DrawnCount, Is.EqualTo(1));
            var estimator = new OpponentHandEstimator();
            estimator.Generate(engine.GetObservation(PlayerId.PlayerA), 16, new RuleRandom(7));
            Assert.That(estimator.Hypotheses.Count, Is.EqualTo(16));
            Assert.That(estimator.Hypotheses.All(value => value.Cards.Count == 5), Is.True);
        }

        [Test]
        public void PublicAutomaticRevivalExcludesItFromAllFutureRefills()
        {
            var cards = Initial;
            cards[4] = new CardDefinition(CardKind.Revival);
            var engine = RuleEngine.FromPosition(new PlayerHands(2, 1), new PlayerHands(3, 0), PlayerId.PlayerA,
                playerBCards: cards);
            engine.TryApply(PlayerId.PlayerA, new AttackAction(HandSide.Left, HandSide.Left), 0);
            Assert.That(engine.Snapshot.CardHistory.Single().Automatic, Is.True);
            var estimator = new OpponentHandEstimator();
            estimator.Generate(engine.GetObservation(PlayerId.PlayerA), 32, new RuleRandom(9));
            foreach (var hypothesis in estimator.Hypotheses)
            {
                Assert.That(hypothesis.RevivalAcquired, Is.True);
                Assert.That(hypothesis.Cards.All(card => card.Kind != CardKind.Revival), Is.True);
                for (ulong seed = 1; seed <= 30; seed++)
                {
                    var random = new RuleRandom(seed);
                    Assert.That(CardDrawing.Draw(ref random, hypothesis.RevivalAcquired).Kind, Is.Not.EqualTo(CardKind.Revival));
                }
            }
            Assert.That(estimator.Hypotheses.Count, Is.EqualTo(32));
        }

        [Test]
        public void UsingAllFiveCardsLeavesAnEmptyHandAndDoesNotInventPastRevival()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerB,
                playerBCards: Initial);
            foreach (long id in new long[] { 1, 2, 3, 4, 5 })
            {
                var use = engine.GetLegalActions(PlayerId.PlayerB).OfType<UseCardAction>().First(action => action.CardId == id);
                Assert.That(engine.TryApply(PlayerId.PlayerB, use, engine.Snapshot.StateVersion).Accepted, Is.True);
                var normal = engine.GetLegalActions(PlayerId.PlayerB).OfType<RedistributeAction>().First(action => action.Left > 0 && action.Right > 0);
                engine.TryApply(PlayerId.PlayerB, normal, engine.Snapshot.StateVersion);
                //変更:封印中も合法な応答を選び、次の札を検証できる両手生存局面を保つ。
                //削除:var response = engine.GetLegalActions(PlayerId.PlayerA).OfType<RedistributeAction>().First();
                var response = engine.GetLegalActions(PlayerId.PlayerA).First(action => !(action is UseCardAction) &&
                    engine.Fork().TryApply(PlayerId.PlayerA, action, engine.Snapshot.StateVersion).State.PlayerB.Left > 0 &&
                    engine.Fork().TryApply(PlayerId.PlayerA, action, engine.Snapshot.StateVersion).State.PlayerB.Right > 0);
                engine.TryApply(PlayerId.PlayerA, response, engine.Snapshot.StateVersion);
            }
            var estimator = new OpponentHandEstimator();
            estimator.Generate(engine.GetObservation(PlayerId.PlayerA), 8, new RuleRandom(1));
            Assert.That(estimator.Hypotheses.Count, Is.EqualTo(8));
            Assert.That(estimator.Hypotheses.All(value => value.Cards.Count == 0 && !value.RevivalAcquired), Is.True);
            estimator.Generate(engine.GetObservation(PlayerId.PlayerA), 8, new RuleRandom(1));
            Assert.That(estimator.Hypotheses.Count, Is.EqualTo(8));
        }

        [Test]
        public void ContradictoryInitialCountsAreDiagnosedAndDoNotProduceInventedHands()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerA);
            var estimator = new OpponentHandEstimator();
            estimator.Generate(engine.GetObservation(PlayerId.PlayerA), 8, new RuleRandom(1));
            Assert.That(estimator.Hypotheses, Is.Empty);
            Assert.That(estimator.Diagnostic, Is.Not.Null);
        }

        [Test]
        public void HandOrderAndOfficialRandomDoNotRevealHiddenInformation()
        {
            var first = new RuleEngine(random: new RuleRandom(1));
            var second = new RuleEngine(random: new RuleRandom(12345));
            first.TryDealInitialCards(0);
            second.TryDealInitialCards(0);
            first.TryStart(1);
            second.TryStart(1);
            var a = new OpponentHandEstimator();
            var b = new OpponentHandEstimator();
            a.Generate(first.GetObservation(PlayerId.PlayerA), 16, new RuleRandom(7));
            b.Generate(second.GetObservation(PlayerId.PlayerA), 16, new RuleRandom(7));
            Assert.That(a.Hypotheses.Select(Key), Is.EqualTo(b.Hypotheses.Select(Key)));
        }

        private static string Key(NpcHandHypothesis hypothesis)
        {
            return string.Join("/", hypothesis.Cards.Select(card => $"{card.Kind}:{card.First}:{card.Second}")) +
                $"/{hypothesis.RevivalAcquired}/{hypothesis.Weight:R}";
        }
    }
}
