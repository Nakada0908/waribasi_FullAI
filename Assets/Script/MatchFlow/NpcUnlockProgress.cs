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
            store.WriteUnlocked();
            IsStrongestUnlocked = true;
            return true;
        }
    }
}
