using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Presentation.Tests
{
    public sealed class NpcDevelopmentRunTests
    {
        private Type storeType;
        private string key;
        private string sentinel;
        private NpcUnlockProgress previousProgress;
        private NpcMatchSetup previousSetup;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private object previousDevelopmentProgress;
#endif

        private static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private NpcUnlockProgress RuntimeProgress()
        {
            return (NpcUnlockProgress)Method(storeType, "CreateRuntimeProgress").Invoke(null, new object[] { key });
        }

        [SetUp]
        public void Prepare()
        {
            storeType = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp")
                .GetType("Waribasi.Presentation.PlayerPrefsNpcUnlockStore", true);
            key = "waribasi_FullAI.tests.t17." + Guid.NewGuid().ToString("N");
            sentinel = key + ".unrelated";
            PlayerPrefs.SetInt(sentinel, 73);
            previousProgress = NpcMatchEntry.Progress;
            previousSetup = NpcMatchEntry.LastSetup;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            previousDevelopmentProgress = storeType.GetField("developmentProgress", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
#endif
        }

        [TearDown]
        public void Cleanup()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            storeType.GetField("developmentProgress", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, previousDevelopmentProgress);
#endif
            if (previousProgress != null)
            {
                NpcMatchEntry.ConfigureProgress(previousProgress);
            }
            else
            {
                typeof(NpcMatchEntry).GetProperty("Progress").SetValue(null, null);
            }
            typeof(NpcMatchEntry).GetProperty("LastSetup").SetValue(null, previousSetup);
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.DeleteKey(sentinel);
            PlayerPrefs.Save();
        }

        [Test]
        public void StartupResetExistsOnlyInDevelopmentAndPrecedesSceneInitialization()
        {
            var reset = Method(storeType, "BeginDevelopmentRun");
            var initialize = Method(storeType, "InitializeProgress");
            Assert.That(initialize.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>().loadType,
                Is.EqualTo(RuntimeInitializeLoadType.BeforeSceneLoad));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(reset, Is.Not.Null);
            Assert.That(reset.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>().loadType,
                Is.EqualTo(RuntimeInitializeLoadType.SubsystemRegistration));
#else
            Assert.That(reset, Is.Null);
            Assert.That(storeType.GetNestedType("DevelopmentUnlockStore", BindingFlags.NonPublic), Is.Null);
            Assert.That(storeType.GetField("developmentProgress", BindingFlags.Static | BindingFlags.NonPublic), Is.Null);
#endif
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FormalAdvancedWinUsesRuntimeStorageAndRetainsUnrelatedData(bool savedUnlock)
        {
            PlayerPrefs.SetInt(key, savedUnlock ? 1 : 0);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Method(storeType, "BeginDevelopmentRun").Invoke(null, null);
#endif
            var progress = RuntimeProgress();
            NpcMatchEntry.ConfigureProgress(progress);
            var session = new LocalMatchSession(new NpcMatchSetup(NpcDifficulty.Advanced, NpcFirstPlayerChoice.Player), PlayerId.PlayerA);
            var engine = RuleEngine.FromPosition(new PlayerHands(2, 1), new PlayerHands(3, 0), PlayerId.PlayerA);
            typeof(LocalMatchSession).GetProperty("Authority").SetValue(session, new MatchAuthority(Guid.NewGuid(), engine, 0, 0));
            var action = new AttackAction(HandSide.Left, HandSide.Left);
            Assert.That(session.Submit(PlayerId.PlayerA, action, 99, 0).Accepted, Is.False);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(progress.IsStrongestUnlocked, Is.False);
#else
            Assert.That(progress.IsStrongestUnlocked, Is.EqualTo(savedUnlock));
#endif
            Assert.That(session.Submit(PlayerId.PlayerA, action, 0, 0).Accepted, Is.True);
            Assert.That(progress.IsStrongestUnlocked, Is.True);
            Assert.That(RuntimeProgress().IsStrongestUnlocked, Is.True);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(PlayerPrefs.GetInt(key), Is.EqualTo(savedUnlock ? 1 : 0));
            Method(storeType, "BeginDevelopmentRun").Invoke(null, null);
            Assert.That(RuntimeProgress().IsStrongestUnlocked, Is.False);
#else
            Assert.That(PlayerPrefs.GetInt(key), Is.EqualTo(1));
            NpcMatchEntry.ConfigureProgress(RuntimeProgress());
            Assert.That(NpcMatchEntry.IsStrongestUnlocked, Is.True);
#endif
            Assert.That(PlayerPrefs.GetInt(sentinel), Is.EqualTo(73));
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void UnlockReturnRematchAndNextRunNeverOverwritePersistentState(int savedValue)
        {
            if (savedValue != -1)
            {
                PlayerPrefs.SetInt(key, savedValue);
            }
            Method(storeType, "BeginDevelopmentRun").Invoke(null, null);
            var progress = RuntimeProgress();
            Assert.That(progress.IsStrongestUnlocked, Is.False);
            Assert.That(NpcMatchEntry.Progress, Is.SameAs(progress));
            var setup = new NpcMatchSetup(NpcDifficulty.Strongest, NpcFirstPlayerChoice.Npc);
            Assert.That(NpcMatchEntry.TryStart(setup), Is.EqualTo(NpcStartResult.Locked));
            Assert.Throws<InvalidOperationException>(() => new LocalMatchSession(setup, PlayerId.PlayerA));
            Assert.That(progress.UnlockForDevelopment(), Is.True);
            Assert.That(progress.UnlockForDevelopment(), Is.False);
            Method(storeType, "InitializeProgress").Invoke(null, null);
            NpcMatchEntry.ConfigureProgress(RuntimeProgress());
            Assert.That(NpcMatchEntry.Progress, Is.SameAs(progress));
            Assert.That(NpcMatchEntry.IsStrongestUnlocked, Is.True);
            var session = new LocalMatchSession(setup, PlayerId.PlayerA);
            session.Authority.DealInitialCards();
            session.Authority.Start(0);
            Assert.That(session.Surrender(session.Authority.State.StateVersion, 0).Accepted, Is.True);
            Guid previousMatch = session.Authority.MatchId;
            session.Restart(PlayerId.PlayerB);
            Assert.That(session.Authority.MatchId, Is.Not.EqualTo(previousMatch));
            Assert.That(session.Setup.Difficulty, Is.EqualTo(NpcDifficulty.Strongest));
            Assert.That(NpcMatchEntry.IsStrongestUnlocked, Is.True);
            Method(storeType, "BeginDevelopmentRun").Invoke(null, null);
            Assert.That(RuntimeProgress(), Is.Not.SameAs(progress));
            Assert.That(NpcMatchEntry.IsStrongestUnlocked, Is.False);
            Assert.That(NpcMatchEntry.TryStart(setup), Is.EqualTo(NpcStartResult.Locked));
            Assert.That(PlayerPrefs.GetInt(key, -1), Is.EqualTo(savedValue));
            Assert.That(PlayerPrefs.GetInt(sentinel), Is.EqualTo(73));
            var persistent = (INpcUnlockStore)Activator.CreateInstance(storeType, key);
            Assert.That(persistent.ReadUnlocked(), Is.EqualTo(savedValue == 1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StartupResetsRetainedOrReloadedStaticsAndNormalizesOldGodSelection(bool domainReload)
        {
            Method(storeType, "BeginDevelopmentRun").Invoke(null, null);
            var previousRun = RuntimeProgress();
            previousRun.UnlockForDevelopment();
            typeof(NpcMatchEntry).GetProperty("LastSetup").SetValue(null,
                new NpcMatchSetup(NpcDifficulty.Strongest, NpcFirstPlayerChoice.Npc));
            if (domainReload)
            {
                storeType.GetField("developmentProgress", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            }
            Method(storeType, "BeginDevelopmentRun").Invoke(null, null);
            Method(storeType, "InitializeProgress").Invoke(null, null);
            Assert.That(NpcMatchEntry.Progress, Is.Not.SameAs(previousRun));
            Assert.That(NpcMatchEntry.IsStrongestUnlocked, Is.False);
            Assert.That(NpcMatchEntry.LastSetup.Difficulty, Is.EqualTo(NpcDifficulty.Advanced));
            Assert.That(NpcMatchEntry.LastSetup.First, Is.EqualTo(NpcFirstPlayerChoice.Npc));
            RuntimeProgress().UnlockForDevelopment();
            Method(storeType, "InitializeProgress").Invoke(null, null);
            Assert.That(NpcMatchEntry.IsStrongestUnlocked, Is.True);
        }
#else
        [TestCase(false)]
        [TestCase(true)]
        public void ReleaseReconstructionReadsExistingSaveAndPreservesTheSelectedGod(bool savedUnlock)
        {
            PlayerPrefs.SetInt(key, savedUnlock ? 1 : 0);
            typeof(NpcMatchEntry).GetProperty("LastSetup").SetValue(null,
                new NpcMatchSetup(NpcDifficulty.Strongest, NpcFirstPlayerChoice.Npc));
            NpcMatchEntry.ConfigureProgress(RuntimeProgress());
            Assert.That(NpcMatchEntry.IsStrongestUnlocked, Is.EqualTo(savedUnlock));
            Assert.That(NpcMatchEntry.LastSetup.Difficulty, Is.EqualTo(savedUnlock ? NpcDifficulty.Strongest : NpcDifficulty.Advanced));
            Assert.That(NpcMatchEntry.LastSetup.First, Is.EqualTo(NpcFirstPlayerChoice.Npc));
            Assert.That(PlayerPrefs.GetInt(key), Is.EqualTo(savedUnlock ? 1 : 0));
            Assert.That(PlayerPrefs.GetInt(sentinel), Is.EqualTo(73));
        }
#endif
    }
}
