using System;
using System.Collections.Generic;

namespace Waribasi.MatchFlow
{
    public enum MatchMode
    {
        Room,
        Random,
        Npc
    }

    public enum FirstTurnSetting
    {
        Random,
        PlayerA,
        PlayerB
    }

    public sealed class MatchSetup
    {
        public static IReadOnlyList<int> TimeOptions { get; } = Array.AsReadOnly(new[] { 15, 30, 60, 120, 180, 300, 600, 0 });
        public MatchMode Mode { get; }
        public FirstTurnSetting First { get; }
        public int TurnSeconds { get; }

        public MatchSetup(MatchMode mode, FirstTurnSetting first = FirstTurnSetting.Random, int turnSeconds = 30)
        {
            if (mode < MatchMode.Room || mode > MatchMode.Npc || first < FirstTurnSetting.Random || first > FirstTurnSetting.PlayerB ||
                !MatchAuthority.IsAllowedTime(turnSeconds))
            {
                throw new ArgumentException("Invalid match settings.");
            }
            Mode = mode;
            First = mode == MatchMode.Random ? FirstTurnSetting.Random : first;
            TurnSeconds = mode == MatchMode.Random ? 30 : mode == MatchMode.Npc ? 0 : turnSeconds;
        }
    }
}
