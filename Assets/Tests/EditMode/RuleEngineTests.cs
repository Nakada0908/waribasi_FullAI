using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Waribasi.Rules.Tests
{
    public class RuleEngineTests
    {
        [TestCase(PlayerId.PlayerA)]
        [TestCase(PlayerId.PlayerB)]
        public void NewMatchWaitsForExplicitStartAndUsesSelectedFirstPlayer(PlayerId firstPlayer)
        {
            var engine = new RuleEngine(firstPlayer);
            var preparing = engine.Snapshot;
            Assert.That(preparing.PlayerA, Is.EqualTo(new PlayerHands(1, 1)));
            Assert.That(preparing.PlayerB, Is.EqualTo(new PlayerHands(1, 1)));
            Assert.That(preparing.MatchPhase, Is.EqualTo(MatchPhase.Preparing));
            AssertRejected(engine, firstPlayer, new AttackAction(HandSide.Left, HandSide.Right),
                ActionRejection.MatchNotActive);

            //変更:先手確定後に配布し、その後で操作受付を開始する。
            Assert.That(engine.TryStart(0).Rejection, Is.EqualTo(ActionRejection.InitialCardsNotDealt));
            Assert.That(engine.TryDealInitialCards(0).Accepted, Is.True);
            var result = engine.TryStart(1);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.CurrentPlayer, Is.EqualTo(firstPlayer));
            Assert.That(result.State.TurnNumber, Is.EqualTo(1));
            //変更:配布と開始がそれぞれ一度ずつ版を進める。
            Assert.That(result.State.StateVersion, Is.EqualTo(2));
            Assert.That(result.State.TurnPhase, Is.EqualTo(TurnPhase.CardOrNormalAction));
            Assert.That(result.Events.Select(e => e.Type), Is.EqualTo(new[]
            {
                RuleEventType.MatchStarted, RuleEventType.TurnChanged
            }));
            Assert.That(preparing.MatchPhase, Is.EqualTo(MatchPhase.Preparing));
            //変更:最新の版でも二重開始は拒否する。
            Assert.That(engine.TryStart(2).Accepted, Is.False);
        }

        [TestCase(1, 1, 2)]
        [TestCase(1, 4, 0)]
        [TestCase(2, 3, 0)]
        [TestCase(4, 3, 2)]
        [TestCase(4, 4, 3)]
        public void AttackAddsCountsAndEliminatesExactlyFive(int source, int target, int expected)
        {
            var engine = Position(source, 1, target, 2);
            var result = engine.TryApply(PlayerId.PlayerA,
                new AttackAction(HandSide.Left, HandSide.Left), 0);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.PlayerA.Left, Is.EqualTo(source));
            Assert.That(result.State.PlayerB.Left, Is.EqualTo(expected));
            Assert.That(result.State.PlayerB.Right, Is.EqualTo(2));
            Assert.That(result.State.CurrentPlayer, Is.EqualTo(PlayerId.PlayerB));
            Assert.That(result.State.StateVersion, Is.EqualTo(1));
            Assert.That(result.State.TurnNumber, Is.EqualTo(2));
            Assert.That(result.Events.Count(e => e.Type == RuleEventType.HandEliminated),
                Is.EqualTo(expected == 0 ? 1 : 0));
        }

        [Test]
        public void PlayerBAndRightSourceUseTheSameRules()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(4, 1), new PlayerHands(1, 3),
                PlayerId.PlayerB);
            var result = engine.TryApply(PlayerId.PlayerB,
                new AttackAction(HandSide.Right, HandSide.Left), 0);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.PlayerA, Is.EqualTo(new PlayerHands(2, 1)));
            Assert.That(result.State.PlayerB, Is.EqualTo(new PlayerHands(1, 3)));
            Assert.That(result.State.CurrentPlayer, Is.EqualTo(PlayerId.PlayerA));
        }

        [TestCase(1, 3, 2, 2)]
        [TestCase(1, 1, 0, 2)]
        [TestCase(1, 1, 2, 0)]
        [TestCase(0, 4, 2, 2)]
        [TestCase(4, 0, 2, 2)]
        [TestCase(1, 3, 3, 1)]
        [TestCase(0, 4, 4, 0)]
        public void RedistributionSupportsMergeRevivalAndSwapAndEndsTheTurn(
            int left, int right, int nextLeft, int nextRight)
        {
            var engine = Position(left, right, 1, 1);
            var result = engine.TryApply(PlayerId.PlayerA, new RedistributeAction(nextLeft, nextRight), 0);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.PlayerA, Is.EqualTo(new PlayerHands(nextLeft, nextRight)));
            Assert.That(result.State.PlayerA.Total, Is.EqualTo(left + right));
            Assert.That(result.State.CurrentPlayer, Is.EqualTo(PlayerId.PlayerB));
            Assert.That(result.State.TurnNumber, Is.EqualTo(2));
            Assert.That(result.Events.Count(e => e.Type == RuleEventType.TurnChanged), Is.EqualTo(1));
            AssertRejected(engine, PlayerId.PlayerA, new AttackAction(HandSide.Right, HandSide.Left),
                ActionRejection.NotYourTurn);
        }

        [TestCase(1, 1, ActionRejection.DistributionUnchanged)]
        [TestCase(0, 0, ActionRejection.TotalNotPreserved)]
        [TestCase(2, 2, ActionRejection.TotalNotPreserved)]
        [TestCase(-1, 3, ActionRejection.FingerCountOutOfRange)]
        [TestCase(3, -1, ActionRejection.FingerCountOutOfRange)]
        [TestCase(5, -3, ActionRejection.FingerCountOutOfRange)]
        [TestCase(0, 5, ActionRejection.FingerCountOutOfRange)]
        [TestCase(int.MaxValue, int.MinValue, ActionRejection.FingerCountOutOfRange)]
        public void InvalidRedistributionDoesNotConsumeAnything(int left, int right,
            ActionRejection rejection)
        {
            AssertRejected(Position(1, 1, 1, 1), PlayerId.PlayerA,
                new RedistributeAction(left, right), rejection);
        }

        [Test]
        public void DeadSourceAndTargetAreRejectedIndependentlyOfTheirVisualPosition()
        {
            var engine = Position(0, 2, 0, 3);
            AssertRejected(engine, PlayerId.PlayerA, new AttackAction(HandSide.Left, HandSide.Right),
                ActionRejection.SourceHandEliminated);
            AssertRejected(engine, PlayerId.PlayerA, new AttackAction(HandSide.Right, HandSide.Left),
                ActionRejection.TargetHandEliminated);
        }

        [Test]
        public void InvalidActorSideAndActionAreRejectedWithoutExceptionsOrStateChanges()
        {
            var engine = Position(1, 1, 1, 1);
            AssertRejected(engine, (PlayerId)99, new AttackAction(HandSide.Left, HandSide.Right),
                ActionRejection.InvalidPlayer);
            AssertRejected(engine, PlayerId.PlayerA, new AttackAction((HandSide)99, HandSide.Left),
                ActionRejection.InvalidHandSide);
            AssertRejected(engine, PlayerId.PlayerA, new AttackAction(HandSide.Left, (HandSide)99),
                ActionRejection.InvalidHandSide);
            AssertRejected(engine, PlayerId.PlayerA, null, ActionRejection.InvalidAction);
            AssertRejected(engine, PlayerId.PlayerA, new UnsupportedAction(), ActionRejection.InvalidAction);
        }

        [Test]
        public void StaleAndRepeatedRequestsLeaveTheCommittedStateUntouched()
        {
            var engine = Position(1, 1, 1, 1);
            var before = engine.Snapshot;
            var action = new AttackAction(HandSide.Left, HandSide.Right);
            Assert.That(engine.Validate(PlayerId.PlayerA, action, 5), Is.EqualTo(ActionRejection.StaleState));
            Assert.That(engine.Snapshot, Is.SameAs(before));
            Assert.That(engine.TryApply(PlayerId.PlayerA, action, 0).Accepted, Is.True);
            var committed = engine.Snapshot;

            var duplicate = engine.TryApply(PlayerId.PlayerA, action, 0);

            Assert.That(duplicate.Rejection, Is.EqualTo(ActionRejection.StaleState));
            Assert.That(engine.Snapshot, Is.SameAs(committed));
            Assert.That(before.PlayerB.Right, Is.EqualTo(1));
            Assert.That(before.StateVersion, Is.EqualTo(0));
        }

        [Test]
        public void EliminatingTheLastHandCommitsOneResultAfterElimination()
        {
            var engine = Position(1, 1, 0, 4);
            var result = engine.TryApply(PlayerId.PlayerA,
                new AttackAction(HandSide.Left, HandSide.Right), 0);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.MatchPhase, Is.EqualTo(MatchPhase.Finished));
            Assert.That(result.State.Winner, Is.EqualTo(PlayerId.PlayerA));
            Assert.That(result.State.Loser, Is.EqualTo(PlayerId.PlayerB));
            Assert.That(result.State.EndReason, Is.EqualTo(MatchEndReason.HandsEliminated));
            Assert.That(result.Events.Select(e => e.Type), Is.EqualTo(new[]
            {
                RuleEventType.ActionAccepted, RuleEventType.HandChanged,
                RuleEventType.HandEliminated, RuleEventType.MatchEnded
            }));
            Assert.That(result.Events[2].Player, Is.EqualTo(PlayerId.PlayerB));
            Assert.That(result.Events[2].Hand, Is.EqualTo(HandSide.Right));
            AssertRejected(engine, PlayerId.PlayerB, new RedistributeAction(0, 0),
                ActionRejection.MatchNotActive);
            Assert.That(engine.TryInterrupt(1).Accepted, Is.False);
            Assert.That(engine.TryEnd(PlayerId.PlayerA, MatchEndReason.Timeout, 1).Accepted, Is.False);
            Assert.That(engine.Snapshot, Is.SameAs(result.State));
        }

        [Test]
        public void RedistributionReportsEliminationAndRevivalForTheActualSides()
        {
            var engine = Position(0, 4, 1, 1);
            var result = engine.TryApply(PlayerId.PlayerA, new RedistributeAction(4, 0), 0);
            var revived = result.Events.Single(e => e.Type == RuleEventType.HandRevived);
            var eliminated = result.Events.Single(e => e.Type == RuleEventType.HandEliminated);

            Assert.That(revived.Hand, Is.EqualTo(HandSide.Left));
            Assert.That(revived.PreviousCount, Is.EqualTo(0));
            Assert.That(revived.Count, Is.EqualTo(4));
            Assert.That(eliminated.Hand, Is.EqualTo(HandSide.Right));
            Assert.That(eliminated.PreviousCount, Is.EqualTo(4));
            Assert.That(eliminated.Count, Is.EqualTo(0));
        }

        [Test]
        public void NormalActionAfterACardStillEndsTheTurnAndResetsTheNextPhase()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1),
                PlayerId.PlayerA, TurnPhase.NormalActionOnly);
            var result = engine.TryApply(PlayerId.PlayerA, new RedistributeAction(0, 2), 0);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.CurrentPlayer, Is.EqualTo(PlayerId.PlayerB));
            Assert.That(result.State.TurnPhase, Is.EqualTo(TurnPhase.CardOrNormalAction));
        }

        [TestCase(MatchEndReason.Surrender)]
        [TestCase(MatchEndReason.Timeout)]
        [TestCase(MatchEndReason.Disconnected)]
        public void ExternalDefeatEndsWithoutChangingHandsOrTriggeringRevival(MatchEndReason reason)
        {
            var engine = Position(1, 1, 2, 3);
            var result = engine.TryEnd(PlayerId.PlayerA, reason, 0);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.Winner, Is.EqualTo(PlayerId.PlayerB));
            Assert.That(result.State.EndReason, Is.EqualTo(reason));
            Assert.That(result.State.PlayerA, Is.EqualTo(new PlayerHands(1, 1)));
            Assert.That(result.State.PlayerB, Is.EqualTo(new PlayerHands(2, 3)));
            Assert.That(result.Events.Select(e => e.Type), Is.EqualTo(new[] { RuleEventType.MatchEnded }));
            Assert.That(engine.TryEnd(PlayerId.PlayerB, reason, 1).Accepted, Is.False);
            Assert.That(engine.Snapshot, Is.SameAs(result.State));
        }

        [TestCase(MatchEndReason.None)]
        [TestCase(MatchEndReason.HandsEliminated)]
        [TestCase(MatchEndReason.CommunicationInterrupted)]
        [TestCase((MatchEndReason)99)]
        public void ExternalDefeatCannotBypassBoardResolutionOrInventAReason(MatchEndReason reason)
        {
            var engine = Position(1, 1, 1, 1);
            var before = engine.Snapshot;
            Assert.That(engine.TryEnd(PlayerId.PlayerA, reason, 0).Rejection,
                Is.EqualTo(ActionRejection.InvalidEndReason));
            Assert.That(engine.Snapshot, Is.SameAs(before));
        }

        [Test]
        public void CommunicationInterruptionDoesNotInventAWinnerAndRemainsFinal()
        {
            var engine = Position(1, 1, 1, 1);
            var result = engine.TryInterrupt(0);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.MatchPhase, Is.EqualTo(MatchPhase.Interrupted));
            Assert.That(result.State.Winner, Is.Null);
            Assert.That(result.State.Loser, Is.Null);
            Assert.That(result.State.EndReason, Is.EqualTo(MatchEndReason.CommunicationInterrupted));
            Assert.That(engine.TryEnd(PlayerId.PlayerA, MatchEndReason.Disconnected, 1).Accepted, Is.False);
            AssertRejected(engine, PlayerId.PlayerA, new RedistributeAction(0, 2),
                ActionRejection.MatchNotActive);
        }

        [Test]
        public void ForksAndCandidateEnumerationDoNotChangeTheOfficialState()
        {
            var engine = Position(1, 3, 1, 1);
            var official = engine.Snapshot;
            var fork = engine.Fork();
            var candidates = fork.GetLegalActions(PlayerId.PlayerA);
            Assert.That(candidates.Count, Is.GreaterThan(0));
            Assert.That(fork.TryApply(PlayerId.PlayerA, candidates[0], 0).Accepted, Is.True);
            Assert.That(engine.Snapshot, Is.SameAs(official));
            Assert.That(fork.Snapshot, Is.Not.SameAs(official));
            Assert.That(engine.GetLegalActions(PlayerId.PlayerB), Is.Empty);
            Assert.That(engine.GetLegalActions((PlayerId)99), Is.Empty);
        }

        [Test]
        public void InvalidInitialPositionsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerHands(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerHands(1, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RuleEngine((PlayerId)99));
            Assert.Throws<ArgumentException>(() => Position(0, 0, 1, 1));
            Assert.Throws<ArgumentException>(() => Position(1, 1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RuleEngine.FromPosition(
                new PlayerHands(1, 1), new PlayerHands(1, 1), (PlayerId)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => RuleEngine.FromPosition(
                new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerA, (TurnPhase)99));
        }

        [Test]
        public void AllLivingBoardsHaveExactlyTheValidatedNormalActionsAndPreserveInvariants()
        {
            int positions = 0;
            int attempts = 0;
            foreach (var playerA in LivingHands())
            {
                foreach (var playerB in LivingHands())
                {
                    foreach (var actor in new[] { PlayerId.PlayerA, PlayerId.PlayerB })
                    {
                        var engine = RuleEngine.FromPosition(playerA, playerB, actor);
                        var before = engine.Snapshot;
                        var legal = engine.GetLegalActions(actor);
                        var keys = new HashSet<string>(legal.Select(ActionKey));
                        Assert.That(keys.Count, Is.EqualTo(legal.Count));
                        foreach (var action in AllNormalRequests())
                        {
                            attempts++;
                            var fork = engine.Fork();
                            var result = fork.TryApply(actor, action, 0);
                            Assert.That(result.Accepted, Is.EqualTo(keys.Contains(ActionKey(action))),
                                "Candidates and execution disagree for " + ActionKey(action));
                            if (!result.Accepted)
                            {
                                Assert.That(fork.Snapshot, Is.SameAs(before));
                                continue;
                            }

                            Assert.That(result.State.StateVersion, Is.EqualTo(1));
                            foreach (var player in new[] { PlayerId.PlayerA, PlayerId.PlayerB })
                            {
                                Assert.That(result.State.GetHands(player).Left, Is.InRange(0, 4));
                                Assert.That(result.State.GetHands(player).Right, Is.InRange(0, 4));
                            }

                            if (action is RedistributeAction)
                            {
                                Assert.That(result.State.GetHands(actor).Total, Is.EqualTo(before.GetHands(actor).Total));
                                Assert.That(result.State.GetHands(GameState.GetOpponent(actor)),
                                    Is.EqualTo(before.GetHands(GameState.GetOpponent(actor))));
                            }
                            else
                            {
                                Assert.That(result.State.GetHands(actor), Is.EqualTo(before.GetHands(actor)));
                            }

                            if (result.State.MatchPhase == MatchPhase.Active)
                            {
                                Assert.That(result.State.CurrentPlayer, Is.EqualTo(GameState.GetOpponent(actor)));
                                Assert.That(result.State.TurnNumber, Is.EqualTo(2));
                                Assert.That(result.State.PlayerA.IsEliminated || result.State.PlayerB.IsEliminated, Is.False);
                            }
                            else
                            {
                                Assert.That(result.State.Winner, Is.EqualTo(actor));
                                Assert.That(result.State.GetHands(GameState.GetOpponent(actor)).IsEliminated, Is.True);
                                Assert.That(fork.GetLegalActions(actor), Is.Empty);
                            }
                        }

                        Assert.That(engine.Snapshot, Is.SameAs(before));
                        positions++;
                    }
                }
            }

            Assert.That(positions, Is.EqualTo(1152));
            Assert.That(attempts, Is.EqualTo(33408));
        }

        private static RuleEngine Position(int aLeft, int aRight, int bLeft, int bRight)
        {
            return RuleEngine.FromPosition(new PlayerHands(aLeft, aRight), new PlayerHands(bLeft, bRight),
                PlayerId.PlayerA);
        }

        private static void AssertRejected(RuleEngine engine, PlayerId actor, GameAction action,
            ActionRejection rejection)
        {
            var before = engine.Snapshot;
            var result = engine.TryApply(actor, action, before.StateVersion);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Rejection, Is.EqualTo(rejection));
            Assert.That(result.State, Is.SameAs(before));
            Assert.That(engine.Snapshot, Is.SameAs(before));
            Assert.That(result.Events.Select(e => e.Type), Is.EqualTo(new[] { RuleEventType.ActionRejected }));
        }

        private static IEnumerable<PlayerHands> LivingHands()
        {
            for (int left = 0; left <= 4; left++)
            {
                for (int right = 0; right <= 4; right++)
                {
                    if (left + right > 0)
                    {
                        yield return new PlayerHands(left, right);
                    }
                }
            }
        }

        private static IEnumerable<GameAction> AllNormalRequests()
        {
            foreach (var source in new[] { HandSide.Left, HandSide.Right })
            {
                foreach (var target in new[] { HandSide.Left, HandSide.Right })
                {
                    yield return new AttackAction(source, target);
                }
            }

            for (int left = 0; left <= 4; left++)
            {
                for (int right = 0; right <= 4; right++)
                {
                    yield return new RedistributeAction(left, right);
                }
            }
        }

        private static string ActionKey(GameAction action)
        {
            if (action is AttackAction attack)
            {
                return "attack:" + attack.Source + ":" + attack.Target;
            }

            var distribution = (RedistributeAction)action;
            return "distribute:" + distribution.Left + ":" + distribution.Right;
        }

        private sealed class UnsupportedAction : GameAction
        {
        }
    }
}
