using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Waribasi.Rules.Tests
{
    public sealed class RuleActionQueryTests
    {
        private static RuleActionQuery Query(RuleEngine engine, PlayerId player)
        {
            var s = engine.Snapshot;
            return RuleActionQuery.FromPublic(s.PlayerA, s.PlayerB, s.CurrentPlayer, s.MatchPhase, s.TurnPhase,
                s.StateVersion, s.PlayerAEffects, s.PlayerBEffects, engine.GetObservation(player).OwnHand);
        }

        private static string Key(GameAction action)
        {
            if (action is AttackAction attack)
            {
                return $"A:{attack.Source}:{attack.Target}";
            }
            if (action is RedistributeAction distribution)
            {
                return $"R:{distribution.Left}:{distribution.Right}";
            }
            var card = (UseCardAction)action;
            return $"C:{card.CardId}:{card.Target}:{card.SwapValues}";
        }

        [Test]
        public void PublicProjectionPreservesCandidatesAcrossEveryNormalPosition()
        {
            int checkedPositions = 0;
            for (int a = 1; a < 25; a++)
            {
                for (int b = 1; b < 25; b++)
                {
                    foreach (PlayerId player in new[] { PlayerId.PlayerA, PlayerId.PlayerB })
                    {
                        var engine = RuleEngine.FromPosition(new PlayerHands(a / 5, a % 5), new PlayerHands(b / 5, b % 5), player);
                        var before = engine.Snapshot;
                        var query = Query(engine, player);
                        Assert.That(query.GetLegalActions(player).Select(Key), Is.EquivalentTo(engine.GetLegalActions(player).Select(Key)));
                        Assert.That(query.GetLegalActions(player).OfType<RedistributeAction>().Count(), Is.LessThanOrEqualTo(4));
                        Assert.That(engine.Snapshot, Is.SameAs(before));
                        checkedPositions++;
                    }
                }
            }
            Assert.That(checkedPositions, Is.EqualTo(1152));
        }

        [TestCase(CardKind.SelfSingle)]
        [TestCase(CardKind.OpponentSingle)]
        [TestCase(CardKind.SelfBoth)]
        [TestCase(CardKind.OpponentBoth)]
        [TestCase(CardKind.Defense)]
        [TestCase(CardKind.Seal)]
        [TestCase(CardKind.DrawTwo)]
        [TestCase(CardKind.DrawThree)]
        [TestCase(CardKind.DrawRandom)]
        [TestCase(CardKind.Revival)]
        public void PublicProjectionUsesTheRealOwnCardIdsWithoutChangingState(CardKind kind)
        {
            bool number = kind <= CardKind.OpponentBoth;
            bool both = kind == CardKind.SelfBoth || kind == CardKind.OpponentBoth;
            var engine = RuleEngine.FromPosition(new PlayerHands(2, 3), new PlayerHands(3, 4), PlayerId.PlayerB,
                playerBCards: new[] { new CardDefinition(kind, number ? 1 : 0, both ? 4 : 0) });
            var query = Query(engine, PlayerId.PlayerB);
            Assert.That(query.GetLegalActions(PlayerId.PlayerB).Select(Key), Is.EquivalentTo(engine.GetLegalActions(PlayerId.PlayerB).Select(Key)));
            Assert.That(query.GetLegalActions(PlayerId.PlayerB).OfType<UseCardAction>().All(c => c.CardId == engine.GetObservation(PlayerId.PlayerB).OwnHand[0].Id), Is.True);
            Assert.That(engine.GetObservation(PlayerId.PlayerB).OwnHand.Count, Is.EqualTo(1));
        }

        [Test]
        public void QueryCannotAccessOpponentCardsOrFormalRandom()
        {
            var first = RuleEngine.FromPosition(new PlayerHands(2, 2), new PlayerHands(1, 4), PlayerId.PlayerA,
                playerACards: new[] { new CardDefinition(CardKind.OpponentBoth, 1, 4) }, playerBCards: new[] { new CardDefinition(CardKind.Revival) });
            var second = RuleEngine.FromPosition(new PlayerHands(2, 2), new PlayerHands(1, 4), PlayerId.PlayerA,
                playerACards: new[] { new CardDefinition(CardKind.OpponentBoth, 1, 4) }, playerBCards: new[] { new CardDefinition(CardKind.Seal) });
            Assert.That(Query(first, PlayerId.PlayerA).GetLegalActions(PlayerId.PlayerA).Select(Key),
                Is.EquivalentTo(Query(second, PlayerId.PlayerA).GetLegalActions(PlayerId.PlayerA).Select(Key)));
            var fields = typeof(RuleActionQuery).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(fields.Any(f => f.FieldType == typeof(RuleEngine) || f.FieldType == typeof(RuleRandom)), Is.False);
        }

        [Test]
        public void QueryCopiesTheSuppliedOwnHand()
        {
            var cards = new List<HandCard> { new HandCard(91, new CardDefinition(CardKind.DrawTwo)) };
            var query = RuleActionQuery.FromPublic(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerA,
                MatchPhase.Active, TurnPhase.CardOrNormalAction, 5, default, default, cards);
            cards.Clear();
            Assert.That(query.GetLegalActions(PlayerId.PlayerA).OfType<UseCardAction>().Single().CardId, Is.EqualTo(91));
            Assert.That(query.Validate(PlayerId.PlayerA, new UseCardAction(91), 4), Is.EqualTo(ActionRejection.StaleState));
        }

        [Test]
        public void DefensiveTargetIsAttackableAndSealedSourceIsExcluded()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 3), new PlayerHands(2, 2), PlayerId.PlayerA,
                playerACards: new[] { new CardDefinition(CardKind.SelfSingle, 4) },
                playerAEffects: new PlayerEffects(new HandEffects(sealUntilTurn: 2), default),
                playerBEffects: new PlayerEffects(new HandEffects(defenseUntilTurn: 2), default));
            var actions = Query(engine, PlayerId.PlayerA).GetLegalActions(PlayerId.PlayerA);
            Assert.That(actions.OfType<AttackAction>().Any(a => a.Source == HandSide.Left), Is.False);
            Assert.That(actions.OfType<AttackAction>().Any(a => a.Target == HandSide.Left), Is.True);
            Assert.That(actions.OfType<UseCardAction>().Any(a => a.Target == HandSide.Left), Is.True);
            Assert.That(actions.OfType<RedistributeAction>().Any(a => a.Left == 3 && a.Right == 1), Is.True);
        }
    }
}
