using System;

namespace Waribasi.MatchFlow
{
    public enum NpcDifficulty
    {
        Beginner,
        Intermediate,
        Advanced
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
        Busy
    }

    public sealed class NpcMatchSetup
    {
        public NpcDifficulty Difficulty { get; }
        public NpcFirstPlayerChoice First { get; }
        public int TurnSeconds => 0;
        public string ReturnScene => "StartGame";

        public NpcMatchSetup(NpcDifficulty difficulty, NpcFirstPlayerChoice first)
        {
            if (difficulty < NpcDifficulty.Beginner || difficulty > NpcDifficulty.Advanced ||
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
