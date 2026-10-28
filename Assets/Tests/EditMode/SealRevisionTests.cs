using System;
using System.Linq;
using NUnit.Framework;

namespace Waribasi.Rules.Tests
{
    public sealed class SealRevisionTests
    {
        private const PlayerId A = PlayerId.PlayerA;

        [TestCase(CardKind.SelfSingle, HandSide.Left, ActionRejection.OwnHandSealed)]
        [TestCase(CardKind.SelfSingle, HandSide.Right, ActionRejection.None)]
        [TestCase(CardKind.Defense, HandSide.Left, ActionRejection.OwnHandSealed)]
        [TestCase(CardKind.Defense, HandSide.Right, ActionRejection.None)]
        [TestCase(CardKind.OpponentSingle, HandSide.Left, ActionRejection.None)]
        [TestCase(CardKind.Seal, HandSide.Left, ActionRejection.None)]
        public void OnlyOwnersOwnTargetCardsAreBlocked(CardKind kind, HandSide side, ActionRejection expected)
        {
            var card = new CardDefinition(kind, kind <= CardKind.OpponentBoth ? 4 : 0);
            var sealedLeft = new PlayerEffects(new HandEffects(sealUntilTurn: 1), default);
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 2), new PlayerHands(2, 3), A,
                playerACards: new[] { card }, playerAEffects: sealedLeft, playerBEffects: sealedLeft);
            var before = engine.Snapshot;
            var result = engine.TryApply(A, new UseCardAction(1, side), 0);
            Assert.That(result.Rejection, Is.EqualTo(expected));
            Assert.That(result.Accepted, Is.EqualTo(expected == ActionRejection.None));
            if (!result.Accepted)
            {
                Assert.That(engine.Snapshot, Is.SameAs(before));
                Assert.That(engine.GetObservation(A).OwnHand.Count, Is.EqualTo(1));
            }
            else
            {
                Assert.That(result.State.GetEffects(A).Left.IsSealed, Is.True);
                Assert.That(result.State.TurnNumber, Is.EqualTo(1));
            }
        }

        [TestCase(HandSide.Left, false)]
        [TestCase(HandSide.Left, true)]
        [TestCase(HandSide.Right, false)]
        [TestCase(HandSide.Right, true)]
        public void EitherSealedHandRejectsEntireSelfBothCard(HandSide sealedSide, bool swap)
        {
            var seal = new HandEffects(sealUntilTurn: 1);
            var effects = sealedSide == HandSide.Left ? new PlayerEffects(seal, default) : new PlayerEffects(default, seal);
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 2), new PlayerHands(2, 3), A,
                playerACards: new[] { new CardDefinition(CardKind.SelfBoth, 3, 4) }, playerAEffects: effects);
            var before = engine.Snapshot;
            Assert.That(engine.TryApply(A, new UseCardAction(1, swapValues: swap), 0).Rejection,
                Is.EqualTo(ActionRejection.OwnHandSealed));
            Assert.That(engine.Snapshot, Is.SameAs(before));
        }

        [Test]
        public void OpponentBothIgnoresSealsButRespectsDefenseOnEachSide()
        {
            var effects = new PlayerEffects(new HandEffects(2, 2), new HandEffects(0, 2));
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 2), new PlayerHands(2, 3), A,
                playerACards: new[] { new CardDefinition(CardKind.OpponentBoth, 1, 4) },
                playerAEffects: effects, playerBEffects: effects);
            var result = engine.TryApply(A, new UseCardAction(1), 0);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.PlayerB, Is.EqualTo(new PlayerHands(2, 4)));
            Assert.That(result.State.PlayerBEffects, Is.EqualTo(effects));
            Assert.That(result.Events.Count(e => e.Type == RuleEventType.ChangeBlocked), Is.EqualTo(1));
        }

        [Test]
        public void RejectedOwnCardDoesNotAdvanceOfficialDrawRandomOrConsumePhase()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 2), new PlayerHands(2, 3), A,
                playerACards: new[] { new CardDefinition(CardKind.SelfSingle, 4), new CardDefinition(CardKind.DrawRandom) },
                playerAEffects: new PlayerEffects(new HandEffects(sealUntilTurn: 1), default), random: new RuleRandom(123));
            var control = engine.Fork();
            var before = engine.GetObservation(A);
            Assert.That(engine.TryApply(A, new UseCardAction(1, HandSide.Left), 0).Accepted, Is.False);
            Assert.That(engine.GetObservation(A).OwnHand, Is.SameAs(before.OwnHand));
            Assert.That(engine.Snapshot, Is.SameAs(before.PublicState));
            foreach (var position in new[] { engine, control })
            {
                Assert.That(position.TryApply(A, new UseCardAction(2), 0).Accepted, Is.True);
            }
            Assert.That(engine.GetObservation(A).OwnHand.Select(Key), Is.EqualTo(control.GetObservation(A).OwnHand.Select(Key)));
        }

        [TestCase(PlayerId.PlayerA)]
        [TestCase(PlayerId.PlayerB)]
        public void SealsBlockAllRedistributionButAllowUnsealedAttacks(PlayerId actor)
        {
            foreach (var hands in new[] { new PlayerHands(1, 3), new PlayerHands(0, 4), new PlayerHands(4, 0) })
            {
                foreach (var side in new[] { HandSide.Left, HandSide.Right })
                {
                    if (hands.GetCount(side) == 0)
                    {
                        continue;
                    }
                    var seal = new HandEffects(sealUntilTurn: 1);
                    var effects = side == HandSide.Left ? new PlayerEffects(seal, default) : new PlayerEffects(default, seal);
                    var engine = RuleEngine.FromPosition(actor == A ? hands : new PlayerHands(2, 3),
                        actor == A ? new PlayerHands(2, 3) : hands, actor,
                        playerAEffects: actor == A ? effects : default, playerBEffects: actor == A ? default : effects);
                    var state = engine.Snapshot;
                    var query = RuleActionQuery.FromPublic(state.PlayerA, state.PlayerB, actor, state.MatchPhase,
                        state.TurnPhase, 0, state.PlayerAEffects, state.PlayerBEffects, Array.Empty<HandCard>());
                    Assert.That(query.GetLegalActions(actor).OfType<RedistributeAction>(), Is.Empty);
                    for (int left = 0; left <= 4; left++)
                    {
                        Assert.That(engine.TryApply(actor, new RedistributeAction(left, hands.Total - left), 0).Rejection,
                            Is.EqualTo(ActionRejection.OwnHandSealed));
                    }
                    Assert.That(engine.Snapshot, Is.SameAs(state));
                    Assert.That(query.GetLegalActions(actor).OfType<AttackAction>().Any(),
                        Is.EqualTo(hands.Left > 0 && hands.Right > 0));
                    Assert.That(query.RequiresSkip(actor), Is.EqualTo(hands.Left == 0 || hands.Right == 0));
                }
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void SingleHandSwapIsRejectedOnBothSidesButRevivalSplitRemainsLegal(int total)
        {
            foreach (bool leftAlive in new[] { false, true })
            {
                var hands = new PlayerHands(leftAlive ? total : 0, leftAlive ? 0 : total);
                var engine = RuleEngine.FromPosition(hands, new PlayerHands(1, 1), A);
                var before = engine.Snapshot;
                Assert.That(engine.TryApply(A, new RedistributeAction(hands.Right, hands.Left), 0).Rejection,
                    Is.EqualTo(ActionRejection.SingleHandSwap));
                Assert.That(engine.Snapshot, Is.SameAs(before));
                if (total >= 2)
                {
                    Assert.That(engine.TryApply(A, new RedistributeAction(1, total - 1), 0).Accepted, Is.True);
                }
            }
        }

        [TestCase(PlayerId.PlayerA, HandSide.Left, TurnPhase.CardOrNormalAction)]
        [TestCase(PlayerId.PlayerA, HandSide.Right, TurnPhase.NormalActionOnly)]
        [TestCase(PlayerId.PlayerB, HandSide.Left, TurnPhase.NormalActionOnly)]
        [TestCase(PlayerId.PlayerB, HandSide.Right, TurnPhase.CardOrNormalAction)]
        public void SkipExpiresEffectsOnceWithoutChangingHandsDrawingOrReviving(PlayerId actor, HandSide alive, TurnPhase phase)
        {
            var hands = alive == HandSide.Left ? new PlayerHands(4, 0) : new PlayerHands(0, 4);
            var seal = new HandEffects(1, 1);
            var effects = alive == HandSide.Left ? new PlayerEffects(seal, default) : new PlayerEffects(default, seal);
            var cards = new[] { new CardDefinition(CardKind.Revival) };
            var engine = RuleEngine.FromPosition(actor == A ? hands : new PlayerHands(1, 1),
                actor == A ? new PlayerHands(1, 1) : hands, actor, phase,
                playerACards: actor == A ? cards : null, playerBCards: actor == A ? null : cards,
                playerAEffects: actor == A ? effects : default, playerBEffects: actor == A ? default : effects);
            var before = engine.GetObservation(actor);
            Assert.That(engine.GetLegalActions(actor).Single(), Is.TypeOf<SkipAction>());
            var result = engine.TryApply(actor, new SkipAction(), 0);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.GetHands(actor), Is.EqualTo(hands));
            Assert.That(engine.GetObservation(actor).OwnHand, Is.SameAs(before.OwnHand));
            Assert.That(result.State.GetEffects(actor), Is.EqualTo(default(PlayerEffects)));
            Assert.That(result.State.CurrentPlayer, Is.EqualTo(GameState.GetOpponent(actor)));
            Assert.That(result.State.StateVersion, Is.EqualTo(1));
            Assert.That(result.State.TurnNumber, Is.EqualTo(2));
            Assert.That(result.State.Winner, Is.Null);
            Assert.That(result.Events.Select(e => e.Type), Is.EqualTo(new[]
            {
                RuleEventType.ActionAccepted, RuleEventType.TurnSkipped, RuleEventType.EffectRemoved,
                RuleEventType.EffectRemoved, RuleEventType.TurnChanged
            }));
            Assert.That(engine.TryApply(actor, new SkipAction(), 0).Rejection, Is.EqualTo(ActionRejection.StaleState));
        }

        [TestCase(1, 3, false)]
        [TestCase(0, 4, false)]
        [TestCase(1, 3, true)]
        public void SkipCannotBeUsedAsAnArbitraryPass(int left, int right, bool seal)
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(left, right), new PlayerHands(1, 1), A,
                playerAEffects: seal ? new PlayerEffects(new HandEffects(0, 1), new HandEffects(0, 1)) : default);
            Assert.That(engine.TryApply(A, new SkipAction(), 0).Rejection, Is.EqualTo(ActionRejection.SkipNotAllowed));
            Assert.That(engine.Snapshot.StateVersion, Is.Zero);
        }

        private static string Key(HandCard card)
        {
            return $"{card.Id}:{card.Definition.Kind}:{card.Definition.First}:{card.Definition.Second}";
        }
    }
}
