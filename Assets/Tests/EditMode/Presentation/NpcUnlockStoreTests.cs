using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Waribasi.MatchFlow;

namespace Waribasi.Presentation.Tests
{
    public sealed class NpcUnlockStoreTests
    {
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
