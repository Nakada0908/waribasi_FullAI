using System;
using NUnit.Framework;
using Waribasi.Rules;

namespace Waribasi.MatchFlow.Tests
{
    public sealed class NpcUnlockTests
    {
        private sealed class Store : INpcUnlockStore
        {
            internal bool Value;
            internal int Writes;
            public bool ReadUnlocked() => Value;
            public void WriteUnlocked()
            {
                Value = true;
                Writes++;
            }
        }

        private static GameState Win()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(2, 1), new PlayerHands(3, 0), PlayerId.PlayerA);
            engine.TryApply(PlayerId.PlayerA, new AttackAction(HandSide.Left, HandSide.Left), 0);
            return engine.Snapshot;
        }

        [Test]
        public void AdvancedFormalBoardWinUnlocksOnceAndNewProgressReloadsIt()
        {
            var store = new Store();
            var progress = new NpcUnlockProgress(store);
            Assert.That(progress.IsStrongestUnlocked, Is.False);
            Assert.That(progress.RecordResult(NpcDifficulty.Advanced, Win(), PlayerId.PlayerA), Is.True);
            Assert.That(progress.RecordResult(NpcDifficulty.Advanced, Win(), PlayerId.PlayerA), Is.False);
            Assert.That(store.Writes, Is.EqualTo(1));
            Assert.That(new NpcUnlockProgress(store).IsStrongestUnlocked, Is.True);
        }

        [TestCase(NpcDifficulty.Beginner)]
        [TestCase(NpcDifficulty.Intermediate)]
        [TestCase(NpcDifficulty.Strongest)]
        public void OtherDifficultiesDoNotUnlock(NpcDifficulty difficulty)
        {
            Assert.That(new NpcUnlockProgress(new Store()).RecordResult(difficulty, Win(), PlayerId.PlayerA), Is.False);
        }

        [TestCase(MatchEndReason.Surrender)]
        [TestCase(MatchEndReason.Disconnected)]
        [TestCase(MatchEndReason.Timeout)]
        public void ExternalWinDoesNotUnlock(MatchEndReason reason)
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerA);
            engine.TryEnd(PlayerId.PlayerB, reason, 0);
            Assert.That(new NpcUnlockProgress(new Store()).RecordResult(NpcDifficulty.Advanced, engine.Snapshot, PlayerId.PlayerA), Is.False);
        }

        [Test]
        public void PreparingActiveInterruptedLossAndNullDoNotUnlock()
        {
            var progress = new NpcUnlockProgress(new Store());
            var engine = new RuleEngine(PlayerId.PlayerA);
            Assert.That(progress.RecordResult(NpcDifficulty.Advanced, engine.Snapshot, PlayerId.PlayerA), Is.False);
            engine.TryDealInitialCards(0);
            engine.TryStart(1);
            Assert.That(progress.RecordResult(NpcDifficulty.Advanced, engine.Snapshot, PlayerId.PlayerA), Is.False);
            engine.TryInterrupt(2);
            Assert.That(progress.RecordResult(NpcDifficulty.Advanced, engine.Snapshot, PlayerId.PlayerA), Is.False);
            Assert.That(progress.RecordResult(NpcDifficulty.Advanced, Win(), PlayerId.PlayerB), Is.False);
            Assert.That(progress.RecordResult(NpcDifficulty.Advanced, null, PlayerId.PlayerA), Is.False);
        }

        [Test]
        public void LockedDirectEntryAndSessionConstructionAreRefused()
        {
            var previous = NpcMatchEntry.Progress;
            NpcMatchEntry.ConfigureProgress(new NpcUnlockProgress(new Store()));
            var setup = new NpcMatchSetup(NpcDifficulty.Strongest, NpcFirstPlayerChoice.Player);
            int calls = 0;
            Func<NpcMatchSetup, NpcStartResult> handler = current =>
            {
                calls++;
                return NpcStartResult.Accepted;
            };
            NpcMatchEntry.Register(handler);
            try
            {
                Assert.That(NpcMatchEntry.TryStart(setup), Is.EqualTo(NpcStartResult.Locked));
                Assert.That(calls, Is.Zero);
                Assert.Throws<InvalidOperationException>(() => new LocalMatchSession(setup, PlayerId.PlayerA));
                NpcMatchEntry.ConfigureProgress(new NpcUnlockProgress(new Store { Value = true }));
                Assert.That(NpcMatchEntry.TryStart(setup), Is.EqualTo(NpcStartResult.Accepted));
                var session = new LocalMatchSession(setup, PlayerId.PlayerA);
                Assert.That(session.Setup.Difficulty, Is.EqualTo(NpcDifficulty.Strongest));
                session.Authority.DealInitialCards();
                session.Authority.Start(0);
                session.Surrender(2, 0);
                Guid old = session.Authority.MatchId;
                session.Restart(PlayerId.PlayerB);
                Assert.That(session.Authority.MatchId, Is.Not.EqualTo(old));
                Assert.That(session.Authority.State.InitialCardsDealt, Is.False);
            }
            finally
            {
                NpcMatchEntry.Unregister(handler);
                NpcMatchEntry.ConfigureProgress(previous ?? new NpcUnlockProgress(new Store()));
            }
        }

        [Test]
        public void SessionRecordsActualAcceptedAdvancedWin()
        {
            var store = new Store();
            var session = new LocalMatchSession(new NpcMatchSetup(NpcDifficulty.Advanced, NpcFirstPlayerChoice.Player),
                PlayerId.PlayerA, new NpcUnlockProgress(store));
            var engine = RuleEngine.FromPosition(new PlayerHands(2, 1), new PlayerHands(3, 0), PlayerId.PlayerA);
            //固定した正式対局で、実際のSubmitの経路を確認する。
            typeof(LocalMatchSession).GetProperty("Authority").SetValue(session, new MatchAuthority(Guid.NewGuid(), engine, 0, 0));
            var action = new AttackAction(HandSide.Left, HandSide.Left);
            Assert.That(session.Submit(PlayerId.PlayerA, action, 99, 0).Accepted, Is.False);
            Assert.That(store.Value, Is.False);
            Assert.That(session.Submit(PlayerId.PlayerA, action, 0, 0).Accepted, Is.True);
            Assert.That(store.Value, Is.True);
            Assert.That(store.Writes, Is.EqualTo(1));
        }
    }
}
