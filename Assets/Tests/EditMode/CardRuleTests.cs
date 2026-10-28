using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Waribasi.Rules.Tests
{
    public class CardRuleTests
    {
        private static readonly PlayerId A = PlayerId.PlayerA;
        private static readonly PlayerId B = PlayerId.PlayerB;

        [TestCase(PlayerId.PlayerA)]
        [TestCase(PlayerId.PlayerB)]
        public void InitialDealHasGuaranteesIsAtomicAndCanOnlyHappenOnce(PlayerId first)
        {
            var engine = new RuleEngine(first, new RuleRandom(123));
            var before = engine.Snapshot;
            var dealt = engine.TryDealInitialCards(0);
            Assert.That(dealt.Accepted, Is.True);
            Assert.That(dealt.State.MatchPhase, Is.EqualTo(MatchPhase.Preparing));
            Assert.That(dealt.State.CurrentPlayer, Is.EqualTo(first));
            Assert.That(dealt.State.TurnNumber, Is.Zero);
            Assert.That(dealt.Events.Select(e => e.Type), Is.EqualTo(new[] { RuleEventType.CardsDrawn, RuleEventType.CardsDrawn }));
            foreach (var player in new[] { A, B })
            {
                var observation = engine.GetObservation(player);
                Assert.That(observation.OwnHand.Count, Is.EqualTo(5));
                Assert.That(observation.OwnHand[0].Definition.IsSelf, Is.True);
                Assert.That(observation.OwnHand[0].Definition.IsNumber, Is.True);
                Assert.That(observation.OwnHand[1].Definition.IsSelf, Is.False);
                Assert.That(observation.OwnHand[1].Definition.IsNumber, Is.True);
                Assert.That(observation.OwnHand.Count(c => c.Definition.Kind == CardKind.Revival), Is.LessThanOrEqualTo(1));
            }

            Assert.That(before.PlayerACardCount, Is.Zero);
            Assert.That(engine.TryDealInitialCards(1).Rejection, Is.EqualTo(ActionRejection.InitialCardsAlreadyDealt));
            Assert.That(engine.TryDealInitialCards(0).Rejection, Is.EqualTo(ActionRejection.StaleState));
            Assert.That(engine.Snapshot, Is.SameAs(dealt.State));
            Assert.That(engine.TryStart(1).Accepted, Is.True);
            Assert.That(engine.TryDealInitialCards(2).Rejection, Is.EqualTo(ActionRejection.MatchNotPreparing));
        }

        [Test]
        public void InitialDealExcludesRevivalImmediatelyAndAllowsDuplicateOtherCards()
        {
            var onePlayer = new[] { 0, 0, 0, 0, 97, 96, 0, 96, 0 };
            var engine = new RuleEngine(random: RuleRandom.FromSequence(onePlayer.Concat(onePlayer)));
            Assert.That(engine.TryDealInitialCards(0).Accepted, Is.True);
            foreach (var player in new[] { A, B })
            {
                Assert.That(engine.GetObservation(player).OwnHand.Select(c => c.Definition.Kind), Is.EqualTo(new[]
                {
                    CardKind.SelfSingle, CardKind.OpponentSingle, CardKind.Revival, CardKind.DrawTwo, CardKind.DrawTwo
                }));
            }

            var newMatch = new RuleEngine(random: RuleRandom.FromSequence(onePlayer.Concat(onePlayer)));
            newMatch.TryDealInitialCards(0);
            Assert.That(newMatch.GetObservation(A).OwnHand.Any(c => c.Definition.Kind == CardKind.Revival), Is.True);
        }

        [TestCase(CardKind.SelfSingle, HandSide.Left)]
        [TestCase(CardKind.SelfSingle, HandSide.Right)]
        [TestCase(CardKind.OpponentSingle, HandSide.Left)]
        [TestCase(CardKind.OpponentSingle, HandSide.Right)]
        public void SingleNumberChangesOnlySelectedHandAndLeavesNormalAction(CardKind kind, HandSide side)
        {
            var engine = Position(new[] { new CardDefinition(kind, 4) });
            var before = engine.GetObservation(A);
            var result = Use(engine, 1, side);
            var target = kind == CardKind.SelfSingle ? A : B;
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.GetHands(target).GetCount(side), Is.EqualTo(4));
            Assert.That(result.State.GetHands(target).GetCount(side == HandSide.Left ? HandSide.Right : HandSide.Left), Is.EqualTo(1));
            Assert.That(result.State.GetCardCount(A), Is.Zero);
            Assert.That(result.State.TurnPhase, Is.EqualTo(TurnPhase.NormalActionOnly));
            Assert.That(result.State.CurrentPlayer, Is.EqualTo(A));
            Assert.That(result.State.TurnNumber, Is.EqualTo(1));
            Assert.That(result.State.StateVersion, Is.EqualTo(1));
            Assert.That(before.OwnHand.Count, Is.EqualTo(1));
            Assert.That(before.PublicState.CardHistory, Is.Empty);
            Assert.That(result.State.CardHistory.Single().TargetPlayer, Is.EqualTo(target));
            Assert.That(engine.TryApply(A, new AttackAction(HandSide.Left, HandSide.Right), 1).Accepted, Is.True);
        }

        [TestCase(CardKind.SelfBoth, false)]
        [TestCase(CardKind.SelfBoth, true)]
        [TestCase(CardKind.OpponentBoth, false)]
        [TestCase(CardKind.OpponentBoth, true)]
        public void BothNumberSupportsEitherAssignment(CardKind kind, bool swap)
        {
            var engine = Position(new[] { new CardDefinition(kind, 4, 1) });
            var result = Use(engine, 1, swap: swap);
            Assert.That(result.Accepted, Is.True);
            var target = kind == CardKind.SelfBoth ? A : B;
            Assert.That(result.State.GetHands(target), Is.EqualTo(swap ? new PlayerHands(4, 1) : new PlayerHands(1, 4)));
            Assert.That(result.State.CardHistory.Single().SwapValues, Is.EqualTo(swap));
        }

        [TestCase(CardKind.SelfSingle)]
        [TestCase(CardKind.OpponentSingle)]
        [TestCase(CardKind.SelfBoth)]
        [TestCase(CardKind.OpponentBoth)]
        public void UnchangedNumberDoesNotConsumeCardPhaseVersionOrRandom(CardKind kind)
        {
            var card = new CardDefinition(kind, 1, kind == CardKind.SelfBoth || kind == CardKind.OpponentBoth ? 1 : 0);
            var engine = Position(new[] { card, new CardDefinition(CardKind.DrawTwo) }, random: Sequence(50, 50));
            var before = engine.GetObservation(A);
            var request = new UseCardAction(1, card.NeedsHand ? HandSide.Left : (HandSide?)null);
            Assert.That(engine.Validate(A, request, 0), Is.EqualTo(ActionRejection.CardUnchanged));
            Assert.That(engine.TryApply(A, request, 0).Rejection, Is.EqualTo(ActionRejection.CardUnchanged));
            Assert.That(engine.Snapshot, Is.SameAs(before.PublicState));
            Assert.That(engine.GetObservation(A).OwnHand, Is.SameAs(before.OwnHand));
            Assert.That(Use(engine, 2).Accepted, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OpponentBothChangesOnlyUndefendedSide(bool defendLeft)
        {
            var effects = defendLeft ? new PlayerEffects(new HandEffects(2), default) : new PlayerEffects(default, new HandEffects(2));
            var engine = Position(new[] { new CardDefinition(CardKind.OpponentBoth, 3, 4) }, bEffects: effects);
            var result = Use(engine, 1);
            Assert.That(result.State.PlayerB, Is.EqualTo(defendLeft ? new PlayerHands(1, 4) : new PlayerHands(3, 1)));
            Assert.That(result.Events.Count(e => e.Type == RuleEventType.ChangeBlocked), Is.EqualTo(1));
            Assert.That(result.State.PlayerBEffects, Is.EqualTo(effects));
        }

        [Test]
        public void FullDefenseRejectsNumberButAttackStillConsumesAction()
        {
            var defended = new PlayerEffects(new HandEffects(2), new HandEffects(2));
            var engine = Position(new[] { new CardDefinition(CardKind.OpponentBoth, 3, 4) }, bEffects: defended);
            var before = engine.Snapshot;
            Assert.That(Use(engine, 1).Rejection, Is.EqualTo(ActionRejection.CardUnchanged));
            Assert.That(engine.Snapshot, Is.SameAs(before));
            var attack = engine.TryApply(A, new AttackAction(HandSide.Left, HandSide.Right), 0);
            Assert.That(attack.Accepted, Is.True);
            Assert.That(attack.State.PlayerB, Is.EqualTo(before.PlayerB));
            Assert.That(attack.State.CurrentPlayer, Is.EqualTo(B));
            Assert.That(attack.Events.Any(e => e.Type == RuleEventType.HandChanged), Is.False);
            Assert.That(attack.Events.Last().Type, Is.EqualTo(RuleEventType.TurnChanged));
        }

        [Test]
        //変更:防御単独は自己変更を妨げず、封印併用は自己変更を拒否する。
        public void DefenseAllowsOwnNumberButSealBlocksItWithoutRefreshingExpiry()
        {
            var effects = new PlayerEffects(new HandEffects(2, 1), default);
            var engine = Position(new[] { new CardDefinition(CardKind.SelfSingle, 4) }, aEffects: effects);
            //変更:封印された左は拒否し、非封印の右を変更できる。
            Assert.That(Use(engine, 1, HandSide.Left).Rejection, Is.EqualTo(ActionRejection.OwnHandSealed));
            Assert.That(Use(engine, 1, HandSide.Right).Accepted, Is.True);
            Assert.That(engine.Snapshot.PlayerAEffects, Is.EqualTo(effects));
            Assert.That(engine.TryApply(A, new AttackAction(HandSide.Left, HandSide.Left), 1).Rejection,
                Is.EqualTo(ActionRejection.SourceHandSealed));
            //変更:配置変更で封印を外せない。非封印手の攻撃で手番を終える。
            Assert.That(engine.TryApply(A, new RedistributeAction(2, 3), 1).Rejection, Is.EqualTo(ActionRejection.OwnHandSealed));
            Assert.That(Apply(engine, A, new AttackAction(HandSide.Right, HandSide.Right)).Accepted, Is.True);
            Assert.That(engine.Snapshot.PlayerAEffects.Left.DefenseUntilTurn, Is.EqualTo(2));
            Assert.That(engine.Snapshot.PlayerAEffects.Left.IsSealed, Is.False);
        }

        [Test]
        //変更:最後の封印手は自己カードと配置変更を拒否し、スキップで期限解除する。
        public void SealOnDefendedLastHandBlocksOwnCardAndExpiresOnSkip()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(0, 4), A,
                playerACards: new[] { new CardDefinition(CardKind.Seal) },
                playerBCards: new[] { new CardDefinition(CardKind.SelfSingle, 3) },
                playerBEffects: new PlayerEffects(default, new HandEffects(2)));
            Assert.That(Use(engine, 1, HandSide.Right).Accepted, Is.True);
            Assert.That(engine.Snapshot.PlayerBEffects.Right.IsSealed, Is.True);
            Apply(engine, A, new RedistributeAction(0, 2));
            Assert.That(engine.Validate(B, new AttackAction(HandSide.Right, HandSide.Right), engine.Snapshot.StateVersion),
                Is.EqualTo(ActionRejection.SourceHandSealed));
            //変更:カードと配置を消費せず拒否してからスキップする。
            Assert.That(engine.TryApply(B, new UseCardAction(1, HandSide.Right), engine.Snapshot.StateVersion).Rejection, Is.EqualTo(ActionRejection.OwnHandSealed));
            Assert.That(Apply(engine, B, new RedistributeAction(1, 3)).Rejection, Is.EqualTo(ActionRejection.OwnHandSealed));
            var result = Apply(engine, B, new SkipAction());
            Assert.That(result.State.PlayerBEffects.Right.IsSealed, Is.False);
            Assert.That(result.State.PlayerBEffects.Right.IsDefended, Is.False);
            Assert.That(result.Events.Count(e => e.Type == RuleEventType.EffectRemoved), Is.EqualTo(2));
        }

        [Test]
        public void AppliedDefenseSurvivesOwnTurnAndExpiresOnlyAfterOpponentAction()
        {
            var engine = Position(new[] { new CardDefinition(CardKind.Defense) });
            Use(engine, 1, HandSide.Left);
            Apply(engine, A, new RedistributeAction(0, 2));
            Assert.That(engine.Snapshot.PlayerAEffects.Left.IsDefended, Is.False);
            var second = Position(new[] { new CardDefinition(CardKind.Defense) });
            Use(second, 1, HandSide.Left);
            Apply(second, A, new RedistributeAction(2, 0));
            Assert.That(second.Snapshot.PlayerAEffects.Left.DefenseUntilTurn, Is.EqualTo(2));
            var attack = Apply(second, B, new AttackAction(HandSide.Left, HandSide.Left));
            Assert.That(attack.State.PlayerA.Left, Is.EqualTo(2));
            Assert.That(attack.State.PlayerAEffects.Left.IsDefended, Is.False);
            Assert.That(attack.Events.Select(e => e.Type), Is.EqualTo(new[]
            {
                RuleEventType.ActionAccepted, RuleEventType.ChangeBlocked, RuleEventType.EffectRemoved, RuleEventType.TurnChanged
            }));
        }

        [Test]
        public void SwapKeepsEffectsOnSidesAndMergeClearsBothEffectsOnlyOnEliminatedSide()
        {
            //変更:合法な左右交換の効果固定は防御だけで検証する。封印は変更自体を拒否する。
            //削除:var effects = new PlayerEffects(new HandEffects(2, 2), new HandEffects(2));
            var effects = new PlayerEffects(new HandEffects(2), new HandEffects(2));
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 3), new PlayerHands(1, 1), A,
                playerAEffects: effects);
            var swap = Apply(engine, A, new RedistributeAction(3, 1));
            Assert.That(swap.State.PlayerAEffects, Is.EqualTo(effects));
            var merge = RuleEngine.FromPosition(new PlayerHands(1, 3), new PlayerHands(1, 1), A,
                playerAEffects: effects);
            var result = Apply(merge, A, new RedistributeAction(0, 4));
            Assert.That(result.State.PlayerAEffects.Left, Is.EqualTo(default(HandEffects)));
            Assert.That(result.State.PlayerAEffects.Right, Is.EqualTo(effects.Right));
            Assert.That(result.Events.Where(e => e.Type == RuleEventType.EffectRemoved).All(e =>
                e.Hand == HandSide.Left && e.RemovalReason == EffectRemovalReason.HandEliminated), Is.True);
            Apply(merge, B, new RedistributeAction(0, 2));
            Apply(merge, A, new RedistributeAction(2, 2));
            Assert.That(merge.Snapshot.PlayerAEffects.Left, Is.EqualTo(default(HandEffects)));
        }

        [TestCase(CardKind.DrawTwo, 2, 0)]
        [TestCase(CardKind.DrawThree, 3, 0)]
        [TestCase(CardKind.DrawRandom, 1, 0)]
        [TestCase(CardKind.DrawRandom, 2, 1)]
        [TestCase(CardKind.DrawRandom, 3, 2)]
        public void RefillRemovesFirstAndDrawsOnlyVacancies(CardKind kind, int requested, int roll)
        {
            var values = kind == CardKind.DrawRandom ? new[] { roll, 50, 50 } : new[] { 50, 50 };
            var engine = Position(new[]
            {
                new CardDefinition(kind), new CardDefinition(CardKind.Defense),
                new CardDefinition(CardKind.Defense), new CardDefinition(CardKind.Defense)
            }, random: Sequence(values));
            var old = engine.GetObservation(A);
            var result = Use(engine, 1);
            int drawn = Math.Min(2, requested);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.PlayerACardCount, Is.EqualTo(3 + drawn));
            Assert.That(result.State.CardHistory.Single().RequestedDrawCount, Is.EqualTo(requested));
            Assert.That(result.State.CardHistory.Single().DrawnCount, Is.EqualTo(drawn));
            Assert.That(result.Events.Single(e => e.Type == RuleEventType.CardsDrawn).DrawnCount, Is.EqualTo(drawn));
            Assert.That(old.OwnHand.Count, Is.EqualTo(4));
            Assert.That(engine.GetObservation(A).OwnHand.All(c => c.Id != 1), Is.True);
            Assert.That(engine.TryApply(A, new UseCardAction(5, HandSide.Left), 1).Rejection,
                Is.EqualTo(ActionRejection.CardAlreadyUsedThisTurn));
        }

        [Test]
        public void FullHandStillDrawsOneAndUnusedOverflowIsNotRolled()
        {
            var engine = Position(Enumerable.Repeat(new CardDefinition(CardKind.DrawThree), 5), random: Sequence(85, 0));
            Assert.That(Use(engine, 1).Accepted, Is.True);
            Assert.That(engine.Snapshot.PlayerACardCount, Is.EqualTo(5));
            Assert.That(engine.GetObservation(A).OwnHand.Last().Definition.Kind, Is.EqualTo(CardKind.DrawTwo));
            Assert.That(engine.Snapshot.CardHistory.Single().DrawnCount, Is.EqualTo(1));
        }

        [TestCase(HandSide.Left)]
        [TestCase(HandSide.Right)]
        //変更:封印はスキップで解除し、その後の攻撃で新しく引いた復活札が発動する。
        public void NewlyDrawnRevivalRemainsAvailableAfterSealedTurnSkip(HandSide side)
        {
            var aHands = side == HandSide.Left ? new PlayerHands(4, 0) : new PlayerHands(0, 4);
            var aEffects = side == HandSide.Left ? new PlayerEffects(new HandEffects(0, 1), default) :
                new PlayerEffects(default, new HandEffects(0, 1));
            var engine = RuleEngine.FromPosition(aHands, new PlayerHands(1, 1), A,
                playerACards: new[] { new CardDefinition(CardKind.DrawTwo) }, playerAEffects: aEffects,
                random: Sequence(97, 50));
            Use(engine, 1);
            //変更:片手交換を使わずスキップで手番を終える。脱落時の封印解除は別テストでも確認する。
            //削除:Apply(engine, A, new RedistributeAction(aHands.Right, aHands.Left));
            Apply(engine, A, new SkipAction());
            var last = side;
            var result = Apply(engine, B, new AttackAction(HandSide.Left, last));
            Assert.That(result.State.MatchPhase, Is.EqualTo(MatchPhase.Active));
            Assert.That(result.State.CurrentPlayer, Is.EqualTo(A));
            Assert.That(result.State.GetHands(A).GetCount(last), Is.EqualTo(1));
            //変更:反対側の脱落手はそのまま維持する。
            Assert.That(result.State.GetHands(A).GetCount(side == HandSide.Left ? HandSide.Right : HandSide.Left), Is.Zero);
            Assert.That(result.State.GetEffects(A).Get(last), Is.EqualTo(default(HandEffects)));
            Assert.That(result.State.CardHistory.Last().Automatic, Is.True);
            Assert.That(result.Events.Any(e => e.Type == RuleEventType.MatchEnded), Is.False);
            Assert.That(engine.GetObservation(A).RevivalAcquired, Is.True);
            Assert.That(engine.GetObservation(A).OwnHand.Any(c => c.Definition.Kind == CardKind.Revival), Is.False);
        }

        [Test]
        public void LastEliminationNotificationPrecedesEffectRemovalRevivalAndTurnChange()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(0, 4), A,
                playerBCards: new[] { new CardDefinition(CardKind.Revival) },
                playerBEffects: new PlayerEffects(default, new HandEffects(0, 2)));
            var result = Apply(engine, A, new AttackAction(HandSide.Left, HandSide.Right));
            Assert.That(result.Events.Select(e => e.Type), Is.EqualTo(new[]
            {
                RuleEventType.ActionAccepted, RuleEventType.HandChanged, RuleEventType.HandEliminated,
                RuleEventType.EffectRemoved, RuleEventType.CardUsed, RuleEventType.HandChanged,
                RuleEventType.HandRevived, RuleEventType.TurnChanged
            }));
            Assert.That(result.State.PlayerBEffects.Right.IsSealed, Is.False);
            Assert.That(result.State.TurnPhase, Is.EqualTo(TurnPhase.CardOrNormalAction));
            Assert.That(result.State.CardHistory.Single().Player, Is.EqualTo(B));
            Assert.That(result.State.CardHistory.Single().Hand, Is.EqualTo(HandSide.Right));
        }

        [Test]
        public void SingleHandEliminationDoesNotTriggerRevival()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(4, 1), A,
                playerBCards: new[] { new CardDefinition(CardKind.Revival) });
            var result = Apply(engine, A, new AttackAction(HandSide.Left, HandSide.Left));
            Assert.That(result.State.PlayerB, Is.EqualTo(new PlayerHands(0, 1)));
            Assert.That(result.State.PlayerBCardCount, Is.EqualTo(1));
            Assert.That(result.State.CardHistory, Is.Empty);
        }

        [TestCase(MatchEndReason.Surrender)]
        [TestCase(MatchEndReason.Timeout)]
        [TestCase(MatchEndReason.Disconnected)]
        [TestCase(MatchEndReason.CommunicationInterrupted)]
        public void ExternalEndNeverConsumesRevival(MatchEndReason reason)
        {
            var engine = Position(new[] { new CardDefinition(CardKind.Revival) });
            var before = engine.GetObservation(A);
            var result = reason == MatchEndReason.CommunicationInterrupted ? engine.TryInterrupt(0) : engine.TryEnd(A, reason, 0);
            Assert.That(result.Accepted, Is.True);
            Assert.That(engine.GetObservation(A).OwnHand, Is.SameAs(before.OwnHand));
            Assert.That(result.State.CardHistory, Is.Empty);
            Assert.That(result.Events.Select(e => e.Type), Is.EqualTo(new[] { RuleEventType.MatchEnded }));
        }

        [Test]
        public void RevivalCannotBeReacquiredEvenAfterAutomaticConsumption()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(0, 4), A,
                playerBCards: new[] { new CardDefinition(CardKind.Revival), new CardDefinition(CardKind.DrawThree) },
                random: Sequence(96, 0, 96, 0, 96, 0));
            Apply(engine, A, new AttackAction(HandSide.Left, HandSide.Right));
            var result = Apply(engine, B, new UseCardAction(2));
            Assert.That(result.Accepted, Is.True);
            Assert.That(engine.GetObservation(B).RevivalAcquired, Is.True);
            Assert.That(engine.GetObservation(B).OwnHand.Select(c => c.Definition.Kind),
                Is.EqualTo(new[] { CardKind.DrawTwo, CardKind.DrawTwo, CardKind.DrawTwo }));
        }

        [Test]
        public void InvalidCardRequestsAreRejectedWithoutAnyPrivateOrPublicMutation()
        {
            var engine = Position(new[]
            {
                new CardDefinition(CardKind.Defense), new CardDefinition(CardKind.SelfBoth, 2, 3),
                new CardDefinition(CardKind.DrawTwo), new CardDefinition(CardKind.Revival)
            });
            var requests = new[]
            {
                new UseCardAction(-1), new UseCardAction(999), new UseCardAction(1),
                new UseCardAction(1, (HandSide)99), new UseCardAction(1, HandSide.Left, true),
                new UseCardAction(2, HandSide.Left), new UseCardAction(3, HandSide.Left), new UseCardAction(4)
            };
            foreach (var request in requests)
            {
                var before = engine.GetObservation(A);
                Assert.That(engine.TryApply(A, request, 0).Accepted, Is.False);
                Assert.That(engine.Snapshot, Is.SameAs(before.PublicState));
                Assert.That(engine.GetObservation(A).OwnHand, Is.SameAs(before.OwnHand));
            }

            Assert.That(engine.TryApply(B, new UseCardAction(1, HandSide.Left), 0).Rejection, Is.EqualTo(ActionRejection.NotYourTurn));
            Assert.That(Use(engine, 1, HandSide.Left).Accepted, Is.True);
            Assert.That(engine.TryApply(A, new UseCardAction(2), 0).Rejection, Is.EqualTo(ActionRejection.StaleState));
            Assert.That(Use(engine, 2).Rejection, Is.EqualTo(ActionRejection.CardAlreadyUsedThisTurn));
        }

        [TestCase(CardKind.SelfSingle)]
        [TestCase(CardKind.OpponentSingle)]
        [TestCase(CardKind.SelfBoth)]
        [TestCase(CardKind.OpponentBoth)]
        [TestCase(CardKind.Defense)]
        [TestCase(CardKind.Seal)]
        public void CardsCannotTargetEliminatedHands(CardKind kind)
        {
            bool number = kind <= CardKind.OpponentBoth;
            bool both = kind == CardKind.SelfBoth || kind == CardKind.OpponentBoth;
            var card = new CardDefinition(kind, number ? 2 : 0, both ? 3 : 0);
            var engine = RuleEngine.FromPosition(new PlayerHands(0, 1), new PlayerHands(0, 1), A,
                playerACards: new[] { card });
            Assert.That(Use(engine, 1, both ? (HandSide?)null : HandSide.Left).Rejection,
                Is.EqualTo(ActionRejection.TargetHandEliminated));
        }

        [Test]
        public void CandidateEnumerationMatchesExecutionForAllCardKindsAndSides()
        {
            int attempts = 0;
            foreach (CardKind kind in Enum.GetValues(typeof(CardKind)))
            {
                bool number = kind <= CardKind.OpponentBoth;
                bool both = kind == CardKind.SelfBoth || kind == CardKind.OpponentBoth;
                var card = new CardDefinition(kind, number ? 2 : 0, both ? 4 : 0);
                foreach (var actor in new[] { A, B })
                {
                    var engine = RuleEngine.FromPosition(new PlayerHands(1, 3), new PlayerHands(3, 1), actor,
                        playerACards: new[] { card }, playerBCards: new[] { card }, random: new RuleRandom(3));
                    var candidates = engine.GetLegalActions(actor).OfType<UseCardAction>().Select(Key).ToList();
                    Assert.That(candidates.Distinct().Count(), Is.EqualTo(candidates.Count));
                    foreach (var target in new HandSide?[] { null, HandSide.Left, HandSide.Right, (HandSide)99 })
                    {
                        foreach (bool swap in new[] { false, true })
                        {
                            var request = new UseCardAction(1, target, swap);
                            bool accepted = engine.Fork().TryApply(actor, request, 0).Accepted;
                            Assert.That(accepted, Is.EqualTo(candidates.Contains(Key(request))), kind + ":" + Key(request));
                            attempts++;
                        }
                    }
                }
            }

            Assert.That(attempts, Is.EqualTo(160));
        }

        [Test]
        public void ForkDoesNotConsumeOfficialHandHistoryOrFutureRandom()
        {
            var engine = Position(new[] { new CardDefinition(CardKind.DrawTwo) }, random: Sequence(97, 50));
            var before = engine.GetObservation(A);
            var fork = engine.Fork();
            fork.GetLegalActions(A);
            fork.Validate(A, new UseCardAction(1), 0);
            var simulated = Use(fork, 1);
            Assert.That(engine.Snapshot, Is.SameAs(before.PublicState));
            Assert.That(engine.GetObservation(A).OwnHand, Is.SameAs(before.OwnHand));
            var actual = Use(engine, 1);
            Assert.That(actual.State.CardHistory.Count, Is.EqualTo(1));
            Assert.That(engine.GetObservation(A).OwnHand.Select(c => c.Definition.Kind),
                Is.EqualTo(fork.GetObservation(A).OwnHand.Select(c => c.Definition.Kind)));
            Assert.That(simulated.State, Is.Not.SameAs(actual.State));
        }

        [Test]
        public void FailedDrawCannotPartiallyCommitOrAdvanceRandom()
        {
            var engine = Position(new[] { new CardDefinition(CardKind.DrawTwo) }, random: Sequence(50));
            var before = engine.GetObservation(A);
            Assert.Throws<InvalidOperationException>(() => Use(engine, 1));
            Assert.That(engine.Snapshot, Is.SameAs(before.PublicState));
            Assert.That(engine.GetObservation(A).OwnHand, Is.SameAs(before.OwnHand));
            Assert.Throws<InvalidOperationException>(() => Use(engine, 1));
        }

        [Test]
        public void SameObservationCannotRevealActualOpponentCardsAcquisitionFlagOrRandom()
        {
            var first = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), A,
                playerACards: new[] { new CardDefinition(CardKind.DrawTwo) },
                playerBCards: new[] { new CardDefinition(CardKind.Revival) }, random: new RuleRandom(1));
            var second = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), A,
                playerACards: new[] { new CardDefinition(CardKind.DrawTwo) },
                playerBCards: new[] { new CardDefinition(CardKind.Defense) }, random: new RuleRandom(999));
            var one = first.GetObservation(A);
            var two = second.GetObservation(A);
            Assert.That(one.OwnHand.Select(c => c.Definition.Kind), Is.EqualTo(two.OwnHand.Select(c => c.Definition.Kind)));
            Assert.That(one.RevivalAcquired, Is.EqualTo(two.RevivalAcquired));
            Assert.That(one.PublicState.PlayerBCardCount, Is.EqualTo(two.PublicState.PlayerBCardCount));
            var simOne = RuleEngine.FromObservation(one, new[] { new CardDefinition(CardKind.Seal) }, false, Sequence(50, 50));
            var simTwo = RuleEngine.FromObservation(two, new[] { new CardDefinition(CardKind.Seal) }, false, Sequence(50, 50));
            Use(simOne, 1);
            Use(simTwo, 1);
            Assert.That(simOne.GetObservation(A).OwnHand.Select(c => c.Definition.Kind),
                Is.EqualTo(simTwo.GetObservation(A).OwnHand.Select(c => c.Definition.Kind)));
            Assert.That(first.Snapshot.StateVersion, Is.Zero);
            Assert.That(second.Snapshot.StateVersion, Is.Zero);
            Assert.Throws<ArgumentException>(() => RuleEngine.FromObservation(one, Array.Empty<CardDefinition>(), false, new RuleRandom(1)));
            Assert.Throws<ArgumentNullException>(() => RuleEngine.FromObservation(one,
                new[] { new CardDefinition(CardKind.Defense) }, false, null));
        }

        [Test]
        public void HypotheticalHandCannotContradictPublicRevivalUse()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(0, 4), A,
                playerBCards: new[] { new CardDefinition(CardKind.Revival), new CardDefinition(CardKind.Defense) });
            Apply(engine, A, new AttackAction(HandSide.Left, HandSide.Right));
            var observation = engine.GetObservation(A);
            Assert.Throws<ArgumentException>(() => RuleEngine.FromObservation(observation,
                new[] { new CardDefinition(CardKind.Revival) }, true, new RuleRandom(1)));
            Assert.Throws<ArgumentException>(() => RuleEngine.FromObservation(observation,
                new[] { new CardDefinition(CardKind.Defense) }, false, new RuleRandom(1)));
            Assert.DoesNotThrow(() => RuleEngine.FromObservation(observation,
                new[] { new CardDefinition(CardKind.Defense) }, true, new RuleRandom(1)));
        }

        [Test]
        public void PublicObjectsDoNotHoldPrivateHandOrRandomFields()
        {
            var publicTypes = new[] { typeof(GameState), typeof(RuleEvent), typeof(ActionResult), typeof(CardUseRecord) };
            foreach (var type in publicTypes)
            {
                foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    Assert.That(field.FieldType, Is.Not.EqualTo(typeof(RuleRandom)));
                    Assert.That(field.FieldType, Is.Not.EqualTo(typeof(PlayerObservation)));
                    Assert.That(field.FieldType.FullName.Contains("PrivateCards"), Is.False);
                    Assert.That(field.FieldType.FullName.Contains("HandCard"), Is.False);
                }
            }
        }

        [Test]
        public void PartiallyDefendedButOtherwiseUnchangedBothCardIsNotConsumed()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 4), A,
                playerACards: new[] { new CardDefinition(CardKind.OpponentBoth, 3, 4) },
                playerBEffects: new PlayerEffects(new HandEffects(2), default));
            var before = engine.GetObservation(A);
            Assert.That(Use(engine, 1).Rejection, Is.EqualTo(ActionRejection.CardUnchanged));
            Assert.That(engine.Snapshot, Is.SameAs(before.PublicState));
            Assert.That(engine.GetObservation(A).OwnHand, Is.SameAs(before.OwnHand));
        }

        [Test]
        public void ConsecutiveSealCardsAreAllowedOnFollowingOwnTurns()
        {
            var engine = Position(new[] { new CardDefinition(CardKind.Seal), new CardDefinition(CardKind.Seal) });
            Use(engine, 1, HandSide.Left);
            Apply(engine, A, new RedistributeAction(0, 2));
            //変更:封印された本人は配置変更できず、非封印手から攻撃する。
            //削除:Apply(engine, B, new RedistributeAction(0, 2));
            Apply(engine, B, new AttackAction(HandSide.Right, HandSide.Right));
            Assert.That(engine.Snapshot.PlayerBEffects.Left.IsSealed, Is.False);
            Assert.That(Use(engine, 2, HandSide.Right).Accepted, Is.True);
            Assert.That(engine.Snapshot.PlayerBEffects.Right.SealUntilTurn, Is.EqualTo(4));
        }

        [Test]
        public void UsedCardIdCannotBeReusedForARefilledSlotAndObservationCollectionsAreReadOnly()
        {
            var engine = Position(new[] { new CardDefinition(CardKind.DrawTwo) }, random: Sequence(50, 50));
            var old = engine.GetObservation(A);
            Use(engine, 1);
            Apply(engine, A, new RedistributeAction(0, 2));
            Apply(engine, B, new RedistributeAction(0, 2));
            Assert.That(Use(engine, 1, HandSide.Right).Rejection, Is.EqualTo(ActionRejection.CardNotFound));
            Assert.That(engine.GetObservation(A).OwnHand.Select(c => c.Id), Is.EqualTo(new long[] { 2, 3 }));
            Assert.Throws<NotSupportedException>(() => ((IList<HandCard>)old.OwnHand).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<CardUseRecord>)engine.Snapshot.CardHistory).Clear());
        }

        [Test]
        public void PlayerBObservationPreservesOwnCardIdsAndUsesOnlyAssumedOpponentCards()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), B,
                playerACards: new[] { new CardDefinition(CardKind.Revival) },
                playerBCards: new[] { new CardDefinition(CardKind.Defense) });
            var observation = engine.GetObservation(B);
            var simulation = RuleEngine.FromObservation(observation,
                new[] { new CardDefinition(CardKind.Seal) }, false, new RuleRandom(6));
            Assert.That(simulation.GetObservation(B).OwnHand[0].Id, Is.EqualTo(observation.OwnHand[0].Id));
            Assert.That(simulation.GetObservation(A).RevivalAcquired, Is.False);
            var result = Apply(simulation, B, new UseCardAction(1, HandSide.Right));
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.PlayerBEffects.Right.IsDefended, Is.True);
            Assert.That(engine.Snapshot.PlayerBEffects.Right.IsDefended, Is.False);
        }

        [Test]
        public void InvalidCardDefinitionsAndPositionsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CardDefinition((CardKind)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CardDefinition(CardKind.SelfSingle, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CardDefinition(CardKind.SelfBoth, 2, 5));
            Assert.Throws<ArgumentException>(() => new CardDefinition(CardKind.Defense, 1));
            Assert.Throws<ArgumentException>(() => Position(Enumerable.Repeat(new CardDefinition(CardKind.Defense), 6)));
            Assert.Throws<ArgumentException>(() => Position(Enumerable.Repeat(new CardDefinition(CardKind.Revival), 2)));
            Assert.Throws<ArgumentException>(() => RuleEngine.FromPosition(new PlayerHands(0, 1), new PlayerHands(1, 1), A,
                playerAEffects: new PlayerEffects(new HandEffects(1), default)));
            Assert.Throws<ArgumentException>(() => Position(null, aEffects: new PlayerEffects(new HandEffects(3), default)));
            Assert.Throws<ArgumentOutOfRangeException>(() => RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), A, turnNumber: 0));
        }

        private static RuleEngine Position(IEnumerable<CardDefinition> cards, PlayerEffects aEffects = default,
            PlayerEffects bEffects = default, RuleRandom random = null)
        {
            return RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), A,
                playerACards: cards, playerAEffects: aEffects, playerBEffects: bEffects, random: random ?? new RuleRandom(123));
        }

        private static RuleRandom Sequence(params int[] values)
        {
            return RuleRandom.FromSequence(values);
        }

        private static ActionResult Use(RuleEngine engine, long id, HandSide? side = null, bool swap = false)
        {
            return Apply(engine, A, new UseCardAction(id, side, swap));
        }

        private static ActionResult Apply(RuleEngine engine, PlayerId actor, GameAction action)
        {
            return engine.TryApply(actor, action, engine.Snapshot.StateVersion);
        }

        private static string Key(UseCardAction action)
        {
            return action.CardId + ":" + action.Target + ":" + action.SwapValues;
        }
    }
}
