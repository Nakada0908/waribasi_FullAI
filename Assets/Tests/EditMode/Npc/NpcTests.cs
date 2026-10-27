using System;
using System.Linq;
using NUnit.Framework;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Npc.Tests
{
    public sealed class NpcTests
    {
        private static PlayerObservation Observe(PlayerHands me, PlayerHands opponent,
            CardDefinition[] cards = null, CardDefinition[] opponentCards = null,
            PlayerEffects effects = default, PlayerEffects opponentEffects = default, ulong randomSeed = 1)
        {
            return RuleEngine.FromPosition(me, opponent, PlayerId.PlayerA, playerACards: cards,
                playerBCards: opponentCards, playerAEffects: effects, playerBEffects: opponentEffects,
                random: new RuleRandom(randomSeed)).GetObservation(PlayerId.PlayerA);
        }

        [TestCase(1, 4)]
        [TestCase(2, 3)]
        [TestCase(3, 2)]
        [TestCase(4, 1)]
        public void BeginnerAlwaysPrefersImmediateElimination(int source, int target)
        {
            var observation = Observe(new PlayerHands(source, 0), new PlayerHands(target, 2),
                new[] { new CardDefinition(CardKind.DrawTwo) });
            var random = new RuleRandom(100);
            var action = BeginnerNpc.Choose(observation, ref random, 100);
            Assert.That(action, Is.TypeOf<AttackAction>());
            var result = NpcSimulation.Create(observation, 2).TryApply(PlayerId.PlayerA, action, observation.PublicState.StateVersion);
            Assert.That(result.Events.Any(value => value.Type == RuleEventType.HandEliminated), Is.True);
        }

        [Test]
        public void BeginnerDoesNotMistakeDefenseForElimination()
        {
            var observation = Observe(new PlayerHands(2, 0), new PlayerHands(3, 4),
                new[] { new CardDefinition(CardKind.DrawTwo) },
                opponentEffects: new PlayerEffects(new HandEffects(defenseUntilTurn: 2), default));
            var random = new RuleRandom(2);
            Assert.That(BeginnerNpc.Choose(observation, ref random, 100), Is.TypeOf<UseCardAction>());
        }

        [Test]
        public void IntermediateFindsCardThenLastHandElimination()
        {
            var observation = Observe(new PlayerHands(1, 1), new PlayerHands(3, 0),
                new[] { new CardDefinition(CardKind.SelfSingle, 2) });
            var random = new RuleRandom(1);
            var decision = IntermediateNpc.Choose(observation, ref random);
            Assert.That(decision.Action, Is.TypeOf<UseCardAction>());
            Assert.That(decision.Evaluation.Finish, Is.EqualTo(1));
            var engine = NpcSimulation.Create(observation, 6);
            engine.TryApply(PlayerId.PlayerA, decision.Action, 0);
            var next = IntermediateNpc.Choose(engine.GetObservation(PlayerId.PlayerA), ref random);
            Assert.That(next.Action, Is.TypeOf<AttackAction>());
            Assert.That(engine.TryApply(PlayerId.PlayerA, next.Action, 1).State.Winner, Is.EqualTo(PlayerId.PlayerA));
        }

        [Test]
        public void IntermediateAvoidsImmediateDefeatWithDefense()
        {
            var observation = Observe(new PlayerHands(1, 0), new PlayerHands(4, 4),
                new[] { new CardDefinition(CardKind.Defense) });
            var random = new RuleRandom(3);
            var decision = IntermediateNpc.Choose(observation, ref random);
            Assert.That(decision.Action, Is.TypeOf<UseCardAction>());
            Assert.That(((UseCardAction)decision.Action).Target, Is.EqualTo(HandSide.Left));
            Assert.That(decision.Evaluation.FatalReplies, Is.Zero);
        }

        [Test]
        public void IntermediateKeepsCardWhenAnAttackAlreadyWins()
        {
            var observation = Observe(new PlayerHands(2, 1), new PlayerHands(3, 0),
                new[] { new CardDefinition(CardKind.SelfSingle, 4), new CardDefinition(CardKind.Defense) });
            var random = new RuleRandom(3);
            var decision = IntermediateNpc.Choose(observation, ref random);
            Assert.That(decision.Action, Is.TypeOf<AttackAction>());
            Assert.That(decision.Evaluation.CardCost, Is.Zero);
        }

        [Test]
        public void EvaluationAccountsForAutomaticRevivalAndItsConsumption()
        {
            var observation = Observe(new PlayerHands(1, 0), new PlayerHands(4, 4),
                new[] { new CardDefinition(CardKind.Revival) });
            var candidates = NpcCandidateGenerator.Generate(observation, 1);
            var vulnerable = candidates.First(candidate => candidate.Normal is RedistributeAction);
            var score = IntermediateNpc.Evaluate(observation, vulnerable);
            Assert.That(score.FatalReplies, Is.Zero);
            Assert.That(score.RevivalCost, Is.EqualTo(1));
        }

        [Test]
        public void IntermediatePrefersSupplyWithSmallHandInEqualSafePosition()
        {
            var observation = Observe(new PlayerHands(1, 1), new PlayerHands(1, 1),
                new[] { new CardDefinition(CardKind.DrawThree) });
            var random = new RuleRandom(3);
            Assert.That(IntermediateNpc.Choose(observation, ref random).Action, Is.TypeOf<UseCardAction>());
        }

        [Test]
        public void IntermediateDoesNotSpendASupplyCardBeforeAnImmediateWin()
        {
            var observation = Observe(new PlayerHands(2, 1), new PlayerHands(3, 0),
                new[] { new CardDefinition(CardKind.DrawTwo) });
            var random = new RuleRandom(3);
            var decision = IntermediateNpc.Choose(observation, ref random);
            Assert.That(decision.Action, Is.TypeOf<AttackAction>());
            Assert.That(decision.Evaluation.CardCost, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HiddenOpponentHandAndOfficialFutureRandomDoNotChangeDecisions(bool beginner)
        {
            var cards = new[] { new CardDefinition(CardKind.DrawTwo), new CardDefinition(CardKind.Seal) };
            var first = Observe(new PlayerHands(2, 1), new PlayerHands(3, 4), cards,
                new[] { new CardDefinition(CardKind.Revival) }, randomSeed: 1);
            var second = Observe(new PlayerHands(2, 1), new PlayerHands(3, 4), cards,
                new[] { new CardDefinition(CardKind.OpponentBoth, 4, 4) }, randomSeed: 99999);
            var a = new RuleRandom(77);
            var b = new RuleRandom(77);
            GameAction left = beginner ? BeginnerNpc.Choose(first, ref a) : IntermediateNpc.Choose(first, ref a).Action;
            GameAction right = beginner ? BeginnerNpc.Choose(second, ref b) : IntermediateNpc.Choose(second, ref b).Action;
            Assert.That(NpcSimulation.ActionKey(left), Is.EqualTo(NpcSimulation.ActionKey(right)));
        }

        [Test]
        public void CandidatesRespectSealsAndNeverUseASecondCard()
        {
            var observation = Observe(new PlayerHands(2, 2), new PlayerHands(3, 4),
                new[] { new CardDefinition(CardKind.SelfBoth, 1, 4), new CardDefinition(CardKind.Seal) },
                effects: new PlayerEffects(new HandEffects(sealUntilTurn: 2), default));
            foreach (var candidate in NpcCandidateGenerator.Generate(observation, 1))
            {
                Assert.That(candidate.Normal, Is.Not.TypeOf<UseCardAction>());
                if (candidate.Normal is AttackAction attack)
                {
                    Assert.That(attack.Source, Is.EqualTo(HandSide.Right));
                }
                Assert.That(candidate.Position.Snapshot.CurrentPlayer, Is.EqualTo(PlayerId.PlayerB));
            }
        }

        [Test]
        public void RecentEquivalentBoardIsPenalizedAfterOtherCriteria()
        {
            var observation = Observe(new PlayerHands(1, 1), new PlayerHands(1, 1));
            var candidate = NpcCandidateGenerator.Generate(observation, 1).First();
            var score = IntermediateNpc.Evaluate(observation, candidate);
            var repeated = IntermediateNpc.Evaluate(observation, candidate,
                new[] { NpcSimulation.BoardKey(candidate.Position.Snapshot) });
            Assert.That(score.CompareTo(repeated), Is.GreaterThan(0));
        }

        [TestCase(NpcFirstPlayerChoice.Player, PlayerId.PlayerA)]
        [TestCase(NpcFirstPlayerChoice.Npc, PlayerId.PlayerB)]
        [TestCase(NpcFirstPlayerChoice.Random, PlayerId.PlayerB)]
        public void LocalSessionIsUntimedAndRematchStartsFresh(NpcFirstPlayerChoice choice, PlayerId expected)
        {
            var session = new LocalMatchSession(new NpcMatchSetup(NpcDifficulty.Intermediate, choice), PlayerId.PlayerB);
            var authority = session.Authority;
            Assert.That(authority.State.CurrentPlayer, Is.EqualTo(expected));
            Assert.That(authority.DealInitialCards(), Is.True);
            Assert.That(authority.Start(1), Is.True);
            Assert.That(authority.Deadline, Is.Null);
            Assert.That(authority.Tick(1000000), Is.False);
            Assert.That(session.Surrender(authority.State.StateVersion, 1000001).Accepted, Is.True);
            var oldId = authority.MatchId;
            session.Restart(PlayerId.PlayerA);
            Assert.That(session.Authority.MatchId, Is.Not.EqualTo(oldId));
            Assert.That(session.Authority.State.InitialCardsDealt, Is.False);
            Assert.That(session.Authority.State.CardHistory, Is.Empty);
            Assert.That(session.Authority.GetObservation(PlayerId.PlayerB).RevivalAcquired, Is.False);
            Assert.That(session.Authority.State.PlayerA.Left, Is.EqualTo(1));
        }

        [Test]
        public void LocalSessionRejectsStaleAndOutOfTurnActions()
        {
            var session = new LocalMatchSession(new NpcMatchSetup(NpcDifficulty.Beginner, NpcFirstPlayerChoice.Player), PlayerId.PlayerA);
            session.Authority.DealInitialCards();
            session.Authority.Start(0);
            Assert.That(session.Submit(PlayerId.PlayerB, new AttackAction(HandSide.Left, HandSide.Left), 2, 0).Accepted, Is.False);
            Assert.That(session.Submit(PlayerId.PlayerA, new AttackAction(HandSide.Left, HandSide.Left), 0, 0).Accepted, Is.False);
            Assert.That(session.Authority.State.StateVersion, Is.EqualTo(2));
        }

        [Test]
        //変更:上級が中級への読み替えなしで同じ正式進行を開始できる。
        public void AdvancedUsesTheCommonLocalSession()
        {
            //削除:実装前の上級拒否テスト。
            //Assert.Throws<ArgumentException>(() => new LocalMatchSession(
            //    new NpcMatchSetup(NpcDifficulty.Advanced, NpcFirstPlayerChoice.Random), PlayerId.PlayerA));
            var session = new LocalMatchSession(new NpcMatchSetup(NpcDifficulty.Advanced, NpcFirstPlayerChoice.Npc), PlayerId.PlayerA);
            Assert.That(session.Setup.Difficulty, Is.EqualTo(NpcDifficulty.Advanced));
            Assert.That(session.Authority.State.CurrentPlayer, Is.EqualTo(session.Npc));
            Assert.That(session.Authority.TurnSeconds, Is.Zero);
        }

        [Test]
        public void BothStrategiesReturnLegalActionsAcrossAllLiveBoards()
        {
            int count = 0;
            for (int a = 0; a <= 4; a++)
            {
                for (int b = 0; b <= 4; b++)
                {
                    if (a + b == 0)
                    {
                        continue;
                    }
                    for (int c = 0; c <= 4; c++)
                    {
                        for (int d = 0; d <= 4; d++)
                        {
                            if (c + d == 0)
                            {
                                continue;
                            }
                            var observation = Observe(new PlayerHands(a, b), new PlayerHands(c, d));
                            var random = new RuleRandom(11);
                            var beginner = BeginnerNpc.Choose(observation, ref random);
                            var intermediate = IntermediateNpc.Choose(observation, ref random);
                            var position = NpcSimulation.Create(observation, 2);
                            Assert.That(position.Validate(PlayerId.PlayerA, beginner, 0), Is.EqualTo(ActionRejection.None));
                            Assert.That(position.Validate(PlayerId.PlayerA, intermediate.Action, 0), Is.EqualTo(ActionRejection.None));
                            count++;
                        }
                    }
                }
            }
            Assert.That(count, Is.EqualTo(576));
        }
    }
}
