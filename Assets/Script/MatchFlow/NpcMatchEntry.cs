using System;

namespace Waribasi.MatchFlow
{
    public enum NpcDifficulty
    {
        Beginner,
        Intermediate,
        Advanced,
        //追加:解放式の追加難易度。既存3難易度の値は維持する。
        Strongest
    }

    public enum NpcFirstPlayerChoice
    {
        Random,
        Player,
        Npc
    }

    public enum NpcStartResult
    {
        Accepted,
        NotConnected,
        UnsupportedDifficulty,
        InvalidSettings,
        Busy,
        //追加:未解放の直接開始も拒否する。
        Locked
    }

    public sealed class NpcMatchSetup
    {
        public NpcDifficulty Difficulty { get; }
        public NpcFirstPlayerChoice First { get; }
        public int TurnSeconds => 0;
        public string ReturnScene => "StartGame";

        public NpcMatchSetup(NpcDifficulty difficulty, NpcFirstPlayerChoice first)
        {
            if (difficulty < NpcDifficulty.Beginner || difficulty > NpcDifficulty.Strongest ||
                first < NpcFirstPlayerChoice.Random || first > NpcFirstPlayerChoice.Npc)
            {
                throw new ArgumentException("Invalid NPC settings.");
            }
            Difficulty = difficulty;
            First = first;
        }
    }

    public static class NpcMatchEntry
    {
        private static Func<NpcMatchSetup, NpcStartResult> handler;
        private static bool starting;
        public static NpcMatchSetup LastSetup { get; private set; }
        public static bool ReturnToPreparation { get; set; }
        //追加:保存実装はUnity側から接続し、通常の入口と直接構築に同じ解放状態を使う。
        public static NpcUnlockProgress Progress { get; private set; }
        public static bool IsStrongestUnlocked => Progress?.IsStrongestUnlocked == true;

        public static void ConfigureProgress(NpcUnlockProgress progress)
        {
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
        }

        public static string DifficultyLabel(NpcDifficulty difficulty)
        {
            switch (difficulty)
            {
                case NpcDifficulty.Beginner: return "初級";
                case NpcDifficulty.Intermediate: return "中級";
                case NpcDifficulty.Advanced: return "上級";
                //変更:既存の選択値を維持し、画面上の名称だけ神へ揃える。
                //削除:case NpcDifficulty.Strongest: return "最強";
                case NpcDifficulty.Strongest: return "神";
                default: throw new ArgumentOutOfRangeException(nameof(difficulty));
            }
        }

        public static void Register(Func<NpcMatchSetup, NpcStartResult> start)
        {
            handler = start ?? throw new ArgumentNullException(nameof(start));
        }

        public static void Unregister(Func<NpcMatchSetup, NpcStartResult> start)
        {
            if (handler == start)
            {
                handler = null;
            }
        }

        public static NpcStartResult TryStart(NpcMatchSetup setup)
        {
            if (setup == null)
            {
                return NpcStartResult.InvalidSettings;
            }
            if (starting)
            {
                return NpcStartResult.Busy;
            }
            //追加:UIを経由しない開始要求も解放を確認する。
            if (setup.Difficulty == NpcDifficulty.Strongest && !IsStrongestUnlocked)
            {
                return NpcStartResult.Locked;
            }
            LastSetup = setup;
            if (handler == null)
            {
                return NpcStartResult.NotConnected;
            }
            starting = true;
            try
            {
                return handler(setup);
            }
            finally
            {
                starting = false;
            }
        }
    }
}
