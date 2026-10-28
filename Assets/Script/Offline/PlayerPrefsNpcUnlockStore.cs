using System;
using UnityEngine;
using Waribasi.MatchFlow;

namespace Waribasi.Presentation
{
    public sealed class PlayerPrefsNpcUnlockStore : INpcUnlockStore
    {
        private readonly string key;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        //追加:開発時は全SceneとControllerで同じ起動中の進捗を共有する。
        private static NpcUnlockProgress developmentProgress;

        //追加:正式保存へ触れず、ボタン解放と上級の正式勝利を起動中だけ保持する。
        private sealed class DevelopmentUnlockStore : INpcUnlockStore
        {
            private bool unlocked;

            public bool ReadUnlocked() => unlocked;

            public void WriteUnlocked()
            {
                unlocked = true;
            }
        }

        //追加:Domain Reloadの有無にかかわらずPlay開始／開発版起動の1回だけ再ロックする。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void BeginDevelopmentRun()
        {
            developmentProgress = new NpcUnlockProgress(new DevelopmentUnlockStore());
            NpcMatchEntry.ConfigureProgress(developmentProgress);
        }
#endif

        //追加:開発時の再接続は同じ進捗を返し、通常版だけ既存の正式保存を読み込む。
        public static NpcUnlockProgress CreateRuntimeProgress(string key = NpcUnlockProgress.SaveKey)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return developmentProgress ??= new NpcUnlockProgress(new DevelopmentUnlockStore());
#else
            return new NpcUnlockProgress(new PlayerPrefsNpcUnlockStore(key));
#endif
        }

        //Scene内のAwakeの順序に依存せず、難易度UIが作られる前に読み込む。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeProgress()
        {
            //変更:開発時は起動時に作った進捗を使い、通常版の保存値は開発解放へ混ぜない。
            //削除:NpcMatchEntry.ConfigureProgress(new NpcUnlockProgress(new PlayerPrefsNpcUnlockStore()));
            NpcMatchEntry.ConfigureProgress(CreateRuntimeProgress());
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
