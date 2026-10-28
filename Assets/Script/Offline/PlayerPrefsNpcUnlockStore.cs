using System;
using UnityEngine;
using Waribasi.MatchFlow;

namespace Waribasi.Presentation
{
    public sealed class PlayerPrefsNpcUnlockStore : INpcUnlockStore
    {
        private readonly string key;

        //Scene内のAwakeの順序に依存せず、難易度UIが作られる前に読み込む。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeProgress()
        {
            NpcMatchEntry.ConfigureProgress(new NpcUnlockProgress(new PlayerPrefsNpcUnlockStore()));
        }

        public PlayerPrefsNpcUnlockStore(string key = NpcUnlockProgress.SaveKey)
        {
            this.key = string.IsNullOrEmpty(key) ? throw new ArgumentException(nameof(key)) : key;
        }

        public bool ReadUnlocked()
        {
            return PlayerPrefs.GetInt(key, 0) == 1;
        }

        public void WriteUnlocked()
        {
            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();
        }
    }
}
