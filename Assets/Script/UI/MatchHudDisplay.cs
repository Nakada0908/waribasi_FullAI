using System;
using System.Linq;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Presentation
{
    public static class MatchHudDisplay
    {
        public static int OpponentCardCount(PublicMatchMessage state, int localPlayer)
        {
            if (state == null || (localPlayer != 0 && localPlayer != 1))
            {
                return 0;
            }
            return Math.Max(0, Math.Min(5, localPlayer == 0 ? state.playerBCardCount : state.playerACardCount));
        }

        public static string Turn(PublicMatchMessage state, int localPlayer, bool interrupted, string npcLabel)
        {
            string label = interrupted || state?.matchPhase == (int)MatchPhase.Interrupted ? "通信中断" :
                state == null || state.matchPhase == (int)MatchPhase.Preparing ? "対戦準備中" :
                state.matchPhase == (int)MatchPhase.Finished ? (state.winner == localPlayer ? "勝利" : "敗北") :
                state.currentPlayer == localPlayer ? "自分の手番" : "相手の手番";
            return string.IsNullOrEmpty(npcLabel) ? label : npcLabel + " ／ " + label;
        }

        public static string InitialOrder(PublicMatchMessage state, int localPlayer)
        {
            return state.currentPlayer == localPlayer ? "あなたは先手" : "あなたは後手";
        }

        public static bool TimerVisible(PublicMatchMessage state, bool local, bool interrupted)
        {
            return !local && !interrupted && state != null && state.turnSeconds > 0 &&
                state.matchPhase == (int)MatchPhase.Active;
        }

        public static bool TimerUrgent(PublicMatchMessage state, double now)
        {
            return state != null && state.hasDeadline && state.gate == (int)InputGate.Accepting && state.deadline - now <= 10;
        }

        public static string Timer(PublicMatchMessage state, double now)
        {
            if (state.gate == (int)InputGate.Presenting)
            {
                return "反映中";
            }
            if (!state.hasDeadline)
            {
                return "開始待ち";
            }
            int seconds = MatchText.RemainingSeconds(state.deadline, now);
            return (TimerUrgent(state, now) ? "! " : "") + seconds + "秒";
        }

        public static double EstimatedServerTime(double receivedServerTime, double receivedAt, double now)
        {
            return receivedServerTime + Math.Max(0, now - receivedAt);
        }

        public static bool ChangedHand(PublicMatchMessage state, PlayerId player, HandSide side)
        {
            return state?.events != null && state.events.Any(item => item.player == (int)player && item.hand == (int)side &&
                (item.type == (int)RuleEventType.HandChanged || item.type == (int)RuleEventType.HandRevived ||
                item.type == (int)RuleEventType.EffectApplied || item.type == (int)RuleEventType.ChangeBlocked));
        }
    }
}
