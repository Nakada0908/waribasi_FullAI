using System;
using Waribasi.Rules;

namespace Waribasi.MatchFlow
{
    public interface INpcUnlockStore
    {
        bool ReadUnlocked();
        void WriteUnlocked();
    }

    public sealed class NpcUnlockProgress
    {
        public const string SaveKey = "waribasi_FullAI.progress.strongest.v1";
        private readonly INpcUnlockStore store;
        public bool IsStrongestUnlocked { get; private set; }

        public NpcUnlockProgress(INpcUnlockStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            IsStrongestUnlocked = store.ReadUnlocked();
        }

        public bool RecordResult(NpcDifficulty difficulty, GameState state, PlayerId human)
        {
            if (IsStrongestUnlocked || difficulty != NpcDifficulty.Advanced || state == null ||
                state.MatchPhase != MatchPhase.Finished || state.Winner != human ||
                state.EndReason != MatchEndReason.HandsEliminated || !state.InitialCardsDealt)
            {
                return false;
            }
            //変更:通常解放と開発用解放を同じ保存処理へ集約する。
            return SaveUnlock();
        }

        //追加:操作だけでなく強制解放の入口も通常リリースから除外する。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool UnlockForDevelopment()
        {
            return SaveUnlock();
        }
#endif

        //追加:保存成功後に解放状態を更新し、繰り返しても書き込まない。
        private bool SaveUnlock()
        {
            if (IsStrongestUnlocked)
            {
                return false;
            }
            store.WriteUnlocked();
            IsStrongestUnlocked = true;
            return true;
        }
    }
}
