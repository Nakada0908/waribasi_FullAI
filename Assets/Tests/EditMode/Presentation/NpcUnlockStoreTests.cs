using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Waribasi.MatchFlow;

namespace Waribasi.Presentation.Tests
{
    public sealed class NpcUnlockStoreTests
    {
        //追加:既存キー形式の解放と開発用保存を、実ユーザーのキーを使わず確認する。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [TestCase(false)]
        [TestCase(true)]
        public void ExistingUnlockAndDevelopmentUnlockUseOnlyTheDedicatedStore(bool previouslyUnlocked)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp")
                .GetType("Waribasi.Presentation.PlayerPrefsNpcUnlockStore", true);
            string key = "waribasi_FullAI.tests.strongest." + Guid.NewGuid().ToString("N");
            string sentinel = key + ".unrelated";
            try
            {
                PlayerPrefs.SetInt(sentinel, 53);
                if (previouslyUnlocked)
                {
                    PlayerPrefs.SetInt(key, 1);
                }
                var progress = new NpcUnlockProgress((INpcUnlockStore)Activator.CreateInstance(type, key));
                Assert.That(progress.IsStrongestUnlocked, Is.EqualTo(previouslyUnlocked));
                Assert.That(progress.UnlockForDevelopment(), Is.EqualTo(!previouslyUnlocked));
                Assert.That(PlayerPrefs.GetInt(key), Is.EqualTo(1));
                var reloaded = new NpcUnlockProgress((INpcUnlockStore)Activator.CreateInstance(type, key));
                Assert.That(reloaded.IsStrongestUnlocked, Is.True);
                Assert.That(PlayerPrefs.GetInt(sentinel), Is.EqualTo(53));
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.DeleteKey(sentinel);
                PlayerPrefs.Save();
            }
        }
#endif

        [Test]
        public void DedicatedPlayerPrefsKeyReloadsWithoutTouchingOtherKeys()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp")
                .GetType("Waribasi.Presentation.PlayerPrefsNpcUnlockStore", true);
            string key = "waribasi_FullAI.tests.strongest." + Guid.NewGuid().ToString("N");
            string sentinel = key + ".unrelated";
            try
            {
                PlayerPrefs.SetInt(sentinel, 37);
                var store = (INpcUnlockStore)Activator.CreateInstance(type, key);
                Assert.That(store.ReadUnlocked(), Is.False);
                PlayerPrefs.SetInt(key, 2);
                Assert.That(store.ReadUnlocked(), Is.False);
                store.WriteUnlocked();
                var reloaded = (INpcUnlockStore)Activator.CreateInstance(type, key);
                Assert.That(new NpcUnlockProgress(reloaded).IsStrongestUnlocked, Is.True);
                Assert.That(PlayerPrefs.GetInt(sentinel), Is.EqualTo(37));
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.DeleteKey(sentinel);
                PlayerPrefs.Save();
            }
        }
    }
}
