using System;
using NUnit.Framework;
using Waribasi.Rules;

namespace Waribasi.MatchFlow.Tests
{
    public sealed class NpcGodRevisionTests
    {
        private sealed class Store : INpcUnlockStore
        {
            public bool Value;
            public int Writes;
            public bool Fail;
            public bool ReadUnlocked() => Value;

            public void WriteUnlocked()
            {
                if (Fail)
                {
                    throw new InvalidOperationException("Test save failure.");
                }
                Value = true;
                Writes++;
            }
        }

        [TestCase(NpcDifficulty.Beginner, 0, "初級")]
        [TestCase(NpcDifficulty.Intermediate, 1, "中級")]
        [TestCase(NpcDifficulty.Advanced, 2, "上級")]
        [TestCase(NpcDifficulty.Strongest, 3, "神")]
        public void LabelsKeepTheExistingFourSelectionValues(NpcDifficulty difficulty, int value, string label)
        {
            Assert.That((int)difficulty, Is.EqualTo(value));
            Assert.That(NpcMatchEntry.DifficultyLabel(difficulty), Is.EqualTo(label));
            Assert.That(Enum.GetValues(typeof(NpcDifficulty)).Length, Is.EqualTo(4));
        }

        [Test]
        public void ExistingSaveKeyAndUnlockedProgressRemainCompatible()
        {
            Assert.That(NpcUnlockProgress.SaveKey, Is.EqualTo("waribasi_FullAI.progress.strongest.v1"));
            var store = new Store { Value = true };
            var progress = new NpcUnlockProgress(store);
            Assert.That(progress.IsStrongestUnlocked, Is.True);
            Assert.That(progress.RecordResult(NpcDifficulty.Strongest, null, PlayerId.PlayerA), Is.False);
            Assert.That(store.Writes, Is.Zero);
            Assert.That(new NpcUnlockProgress(store).IsStrongestUnlocked, Is.True);
        }

        [Test]
        public void DevelopmentEntryUsesTheCompilationCondition()
        {
            var method = typeof(NpcUnlockProgress).GetMethod("UnlockForDevelopment");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(method, Is.Not.Null);
#else
            Assert.That(method, Is.Null);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Test]
        public void DevelopmentUnlockPersistsOnceAndCanReload()
        {
            var store = new Store();
            var progress = new NpcUnlockProgress(store);
            Assert.That(progress.UnlockForDevelopment(), Is.True);
            Assert.That(progress.IsStrongestUnlocked, Is.True);
            Assert.That(progress.UnlockForDevelopment(), Is.False);
            Assert.That(store.Writes, Is.EqualTo(1));
            Assert.That(new NpcUnlockProgress(store).IsStrongestUnlocked, Is.True);
            Assert.That(new NpcUnlockProgress(store).UnlockForDevelopment(), Is.False);
            Assert.That(store.Writes, Is.EqualTo(1));
        }

        [Test]
        public void FailedSaveDoesNotClaimAnUnlockAndCanRetry()
        {
            var store = new Store { Fail = true };
            var progress = new NpcUnlockProgress(store);
            Assert.Throws<InvalidOperationException>(() => progress.UnlockForDevelopment());
            Assert.That(progress.IsStrongestUnlocked, Is.False);
            Assert.That(store.Writes, Is.Zero);
            store.Fail = false;
            Assert.That(progress.UnlockForDevelopment(), Is.True);
            Assert.That(store.Writes, Is.EqualTo(1));
        }

        [Test]
        public void DevelopmentUnlockKeepsTheSameSetupAndRematchPath()
        {
            var store = new Store();
            var progress = new NpcUnlockProgress(store);
            var setup = new NpcMatchSetup(NpcDifficulty.Strongest, NpcFirstPlayerChoice.Npc);
            Assert.Throws<InvalidOperationException>(() => new LocalMatchSession(setup, PlayerId.PlayerA, progress));
            progress.UnlockForDevelopment();
            var session = new LocalMatchSession(setup, PlayerId.PlayerA, progress);
            Assert.That(session.Setup, Is.SameAs(setup));
            Assert.That(session.Authority.State.CurrentPlayer, Is.EqualTo(PlayerId.PlayerB));
            session.Authority.DealInitialCards();
            session.Authority.Start(0);
            session.Surrender(session.Authority.State.StateVersion, 0);
            Guid previous = session.Authority.MatchId;
            session.Restart(PlayerId.PlayerA);
            Assert.That(session.Authority.MatchId, Is.Not.EqualTo(previous));
            Assert.That(session.Setup, Is.SameAs(setup));
            Assert.That(session.Authority.State.CurrentPlayer, Is.EqualTo(PlayerId.PlayerB));
            Assert.That(session.Authority.State.InitialCardsDealt, Is.False);
            Assert.That(store.Writes, Is.EqualTo(1));
        }
#endif
    }
}
