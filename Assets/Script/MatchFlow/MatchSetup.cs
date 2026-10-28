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
        //追加:ルーム初期値とランダム固定値の共通定義。明示した30秒等はTimeOptionsで維持する。
        public const int DefaultTurnSeconds = 60;
        public const int DefaultTimeIndex = 2;
        public static IReadOnlyList<int> TimeOptions { get; } = Array.AsReadOnly(new[] { 15, 30, 60, 120, 180, 300, 600, 0 });
        public MatchMode Mode { get; }
        public FirstTurnSetting First { get; }
        public int TurnSeconds { get; }

        //変更:省略時の毎手番を1分へ統一する。
        //削除:public MatchSetup(MatchMode mode, FirstTurnSetting first = FirstTurnSetting.Random, int turnSeconds = 30)
        public MatchSetup(MatchMode mode, FirstTurnSetting first = FirstTurnSetting.Random, int turnSeconds = DefaultTurnSeconds)
        {
            if (mode < MatchMode.Room || mode > MatchMode.Npc || first < FirstTurnSetting.Random || first > FirstTurnSetting.PlayerB ||
                !MatchAuthority.IsAllowedTime(turnSeconds))
            {
                throw new ArgumentException("Invalid match settings.");
            }
            Mode = mode;
            First = mode == MatchMode.Random ? FirstTurnSetting.Random : first;
            //変更:ランダムは1分固定、NPCは制限なし、ルームは選択値を保持する。
            //削除:TurnSeconds = mode == MatchMode.Random ? 30 : mode == MatchMode.Npc ? 0 : turnSeconds;
            TurnSeconds = mode == MatchMode.Random ? DefaultTurnSeconds : mode == MatchMode.Npc ? 0 : turnSeconds;
        }
    }
}
