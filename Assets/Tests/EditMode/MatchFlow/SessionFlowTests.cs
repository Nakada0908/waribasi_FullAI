using System;
using System.Linq;
using NUnit.Framework;
using Waribasi.Rules;

namespace Waribasi.MatchFlow.Tests
{
    public sealed class SessionFlowTests
    {
        [Test]
        public void EveryRoomFirstAndTimeCombinationSurvivesSettingsAndRematch()
        {
            foreach (FirstTurnSetting first in Enum.GetValues(typeof(FirstTurnSetting)))
            {
                foreach (int seconds in MatchSetup.TimeOptions)
                {
                    var setup = new MatchSetup(MatchMode.Room, first, seconds);
                    var flow = Result(setup);
                    Guid oldMatch = flow.MatchId;
                    Assert.That(flow.VoteRematch(PlayerId.PlayerA, oldMatch, 1, true), Is.True);
                    Assert.That(flow.TryRematch(), Is.False);
                    Assert.That(flow.VoteRematch(PlayerId.PlayerB, oldMatch, 1, true), Is.True);
                    Assert.That(flow.TryRematch(), Is.True);
                    Assert.That(flow.TryRematch(), Is.False);
                    Assert.That(flow.Round, Is.EqualTo(2));
                    Assert.That(flow.MatchId, Is.EqualTo(Guid.Empty));
                    Assert.That(flow.Setup, Is.SameAs(setup));
                    Assert.That(flow.Setup.First, Is.EqualTo(first));
                    Assert.That(flow.Setup.TurnSeconds, Is.EqualTo(seconds));
                    Assert.That(flow.VoteRematch(PlayerId.PlayerA, oldMatch, 1, true), Is.False);
                    Assert.That(flow.BindMatch(Guid.NewGuid()), Is.True);
                    Assert.That(flow.PlayerARematch || flow.PlayerBRematch, Is.False);
                }
            }
            Assert.That(MatchSetup.TimeOptions.Distinct().Count(), Is.EqualTo(8));
        }

        [TestCase(15)]
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        [TestCase(180)]
        [TestCase(300)]
        [TestCase(600)]
        [TestCase(0)]
        public void RandomModeAlwaysUsesThirtySecondsAndRandomFirst(int seconds)
        {
            var setup = new MatchSetup(MatchMode.Random, FirstTurnSetting.PlayerB, seconds);
            Assert.That(setup.First, Is.EqualTo(FirstTurnSetting.Random));
            Assert.That(setup.TurnSeconds, Is.EqualTo(30));
            Assert.That(new MatchSetup(MatchMode.Npc, FirstTurnSetting.PlayerA, seconds).TurnSeconds, Is.Zero);
        }

        [TestCase(-1)]
        [TestCase(1)]
        [TestCase(14)]
        [TestCase(16)]
        [TestCase(601)]
        [TestCase(int.MaxValue)]
        public void FreeFormOrOutOfRangeTimeIsRejected(int seconds)
        {
            Assert.Throws<ArgumentException>(() => new MatchSetup(MatchMode.Room, FirstTurnSetting.Random, seconds));
        }

        [Test]
        public void InvalidModeFirstOrOfflineSessionIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new MatchSetup((MatchMode)99));
            Assert.Throws<ArgumentException>(() => new MatchSetup(MatchMode.Room, (FirstTurnSetting)(-1)));
            Assert.Throws<ArgumentException>(() => new SessionFlowState(Guid.Empty, new MatchSetup(MatchMode.Room)));
            Assert.Throws<ArgumentException>(() => new SessionFlowState(Guid.NewGuid(), new MatchSetup(MatchMode.Npc)));
        }

        [Test]
        public void StartRequiresBothCurrentSettingsReceiptsAndConnectedPeer()
        {
            var flow = new SessionFlowState(Guid.NewGuid(), new MatchSetup(MatchMode.Room));
            Assert.That(flow.AcknowledgeSettings(PlayerId.PlayerA, flow.SessionId, 1), Is.False);
            flow.ConnectPeer();
            Assert.That(flow.AcknowledgeSettings(PlayerId.PlayerA, Guid.NewGuid(), 1), Is.False);
            Assert.That(flow.AcknowledgeSettings(PlayerId.PlayerA, flow.SessionId, 2), Is.False);
            Assert.That(flow.AcknowledgeSettings((PlayerId)3, flow.SessionId, 1), Is.False);
            Assert.That(flow.AcknowledgeSettings(PlayerId.PlayerA, flow.SessionId, 1), Is.True);
            Assert.That(flow.TryBegin(), Is.False);
            Assert.That(flow.AcknowledgeSettings(PlayerId.PlayerB, flow.SessionId, 1), Is.True);
            Assert.That(flow.TryBegin(), Is.True);
            Assert.That(flow.TryBegin(), Is.False);
            Assert.That(flow.AcknowledgeSettings(PlayerId.PlayerB, flow.SessionId, 1), Is.False);
        }

        [Test]
        public void PreparationCancelCannotInterruptAnActiveGame()
        {
            var flow = Started();
            Assert.That(flow.CancelPreparation(), Is.False);
            Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Playing));
            Assert.That(flow.VoteRematch(PlayerId.PlayerA, flow.MatchId, flow.Round, true), Is.False);
            Assert.That(flow.Finish(Guid.NewGuid()), Is.False);
            Assert.That(flow.Finish(flow.MatchId), Is.True);
            Assert.That(flow.Finish(flow.MatchId), Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void CancelBeforeStartClosesWithoutResultAndCannotRestart(int stage)
        {
            var flow = new SessionFlowState(Guid.NewGuid(), new MatchSetup(MatchMode.Room));
            flow.ConnectPeer();
            if (stage >= 1)
            {
                flow.AcknowledgeSettings(PlayerId.PlayerA, flow.SessionId, 1);
                flow.AcknowledgeSettings(PlayerId.PlayerB, flow.SessionId, 1);
                flow.TryBegin();
            }
            if (stage >= 2)
            {
                flow.BindMatch(Guid.NewGuid());
            }
            Assert.That(flow.CancelPreparation(), Is.True);
            Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Closed));
            Assert.That(flow.TryBegin(), Is.False);
            Assert.That(flow.BindMatch(Guid.NewGuid()), Is.False);
            Assert.That(flow.Finish(flow.MatchId), Is.False);
        }

        [Test]
        public void LeavingAfterResultClearsBothVotesAndKeepsTheConfirmedMatch()
        {
            var flow = Result(new MatchSetup(MatchMode.Room));
            Guid match = flow.MatchId;
            flow.VoteRematch(PlayerId.PlayerA, match, 1, true);
            flow.DisconnectPeer();
            Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Result));
            Assert.That(flow.MatchId, Is.EqualTo(match));
            Assert.That(flow.PeerPresent, Is.False);
            Assert.That(flow.PlayerARematch || flow.PlayerBRematch, Is.False);
            Assert.That(flow.VoteRematch(PlayerId.PlayerB, match, 1, true), Is.False);
            Assert.That(flow.TryRematch(), Is.False);
        }

        [Test]
        public void ActiveDisconnectStillAllowsTheAuthorityToPublishItsResult()
        {
            var flow = Started();
            flow.DisconnectPeer();
            Assert.That(flow.Finish(flow.MatchId), Is.True);
            Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Result));
            Assert.That(flow.TryRematch(), Is.False);
        }

        [Test]
        public void VotesCanBeRevokedAndWrongMatchOrRoundIsIgnored()
        {
            var flow = Result(new MatchSetup(MatchMode.Room));
            Assert.That(flow.VoteRematch(PlayerId.PlayerA, Guid.NewGuid(), 1, true), Is.False);
            Assert.That(flow.VoteRematch(PlayerId.PlayerA, flow.MatchId, 0, true), Is.False);
            flow.VoteRematch(PlayerId.PlayerA, flow.MatchId, 1, true);
            flow.VoteRematch(PlayerId.PlayerA, flow.MatchId, 1, false);
            flow.VoteRematch(PlayerId.PlayerB, flow.MatchId, 1, true);
            Assert.That(flow.TryRematch(), Is.False);
        }

        [Test]
        public void NpcEntryValidatesSettingsAndHasNoOnlineFallback()
        {
            Assert.That(NpcMatchEntry.TryStart(null), Is.EqualTo(NpcStartResult.InvalidSettings));
            Assert.Throws<ArgumentException>(() => new NpcMatchSetup((NpcDifficulty)3, NpcFirstPlayerChoice.Random));
            Assert.Throws<ArgumentException>(() => new NpcMatchSetup(NpcDifficulty.Beginner, (NpcFirstPlayerChoice)3));
            foreach (NpcDifficulty difficulty in Enum.GetValues(typeof(NpcDifficulty)))
            {
                foreach (NpcFirstPlayerChoice first in Enum.GetValues(typeof(NpcFirstPlayerChoice)))
                {
                    var setup = new NpcMatchSetup(difficulty, first);
                    Assert.That(setup.TurnSeconds, Is.Zero);
                    Assert.That(setup.ReturnScene, Is.EqualTo("StartGame"));
                    Assert.That(NpcMatchEntry.TryStart(setup), Is.EqualTo(NpcStartResult.NotConnected));
                    Assert.That(NpcMatchEntry.LastSetup, Is.SameAs(setup));
                }
            }
        }

        [Test]
        public void NpcHandlerReceivesTheExactRequestOnceAndRejectsReentrancy()
        {
            var setup = new NpcMatchSetup(NpcDifficulty.Intermediate, NpcFirstPlayerChoice.Npc);
            int calls = 0;
            Func<NpcMatchSetup, NpcStartResult> handler = current =>
            {
                calls++;
                Assert.That(current, Is.SameAs(setup));
                Assert.That(NpcMatchEntry.TryStart(current), Is.EqualTo(NpcStartResult.Busy));
                return NpcStartResult.Accepted;
            };
            NpcMatchEntry.Register(handler);
            try
            {
                Assert.That(NpcMatchEntry.TryStart(setup), Is.EqualTo(NpcStartResult.Accepted));
                Assert.That(calls, Is.EqualTo(1));
            }
            finally
            {
                NpcMatchEntry.Unregister(handler);
            }
            Assert.That(NpcMatchEntry.TryStart(setup), Is.EqualTo(NpcStartResult.NotConnected));
        }

        [Test]
        public void UnsupportedNpcDifficultyIsPreservedWithoutSubstitution()
        {
            Func<NpcMatchSetup, NpcStartResult> handler = current => NpcStartResult.UnsupportedDifficulty;
            NpcMatchEntry.Register(handler);
            try
            {
                var setup = new NpcMatchSetup(NpcDifficulty.Advanced, NpcFirstPlayerChoice.Player);
                Assert.That(NpcMatchEntry.TryStart(setup), Is.EqualTo(NpcStartResult.UnsupportedDifficulty));
                Assert.That(NpcMatchEntry.LastSetup.Difficulty, Is.EqualTo(NpcDifficulty.Advanced));
            }
            finally
            {
                NpcMatchEntry.Unregister(handler);
            }
        }

        private static SessionFlowState Started(MatchSetup setup = null)
        {
            var flow = new SessionFlowState(Guid.NewGuid(), setup ?? new MatchSetup(MatchMode.Room));
            flow.ConnectPeer();
            flow.AcknowledgeSettings(PlayerId.PlayerA, flow.SessionId, 1);
            flow.AcknowledgeSettings(PlayerId.PlayerB, flow.SessionId, 1);
            Assert.That(flow.TryBegin(), Is.True);
            Assert.That(flow.BindMatch(Guid.NewGuid()), Is.True);
            Assert.That(flow.MarkPlaying(flow.MatchId), Is.True);
            return flow;
        }

        private static SessionFlowState Result(MatchSetup setup)
        {
            var flow = Started(setup);
            Assert.That(flow.Finish(flow.MatchId), Is.True);
            return flow;
        }
    }
}
