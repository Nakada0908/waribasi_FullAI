using System;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Presentation
{
    public static class MatchText
    {
        public static string Count(int count)
        {
            return count == 0 ? "脱落" : count + "本";
        }

        public static string Card(CardDefinition card)
        {
            if (card.IsNumber)
            {
                return (card.IsSelf ? "自分" : "相手") + (card.IsBoth
                    ? $"の両手を {card.First}・{card.Second}本に変更" : $"の片手を {card.First}本に変更");
            }
            switch (card.Kind)
            {
                case CardKind.Defense: return "自分の片手を防御";
                //変更:封印の新しい操作制限を札の説明にも反映する。
                //削除:case CardKind.Seal: return "相手の片手の攻撃を封印";
                case CardKind.Seal: return "相手の片手を封印（本人の操作を制限）";
                case CardKind.DrawTwo: return "2枚補充（上限5枚）";
                case CardKind.DrawThree: return "3枚補充（上限5枚）";
                case CardKind.DrawRandom: return "1〜3枚をランダム補充";
                default: return "復活（敗北直前に自動発動）";
            }
        }

        public static string Option(GameAction action, CardDefinition card)
        {
            if (action is RedistributeAction distribution)
            {
                return $"左 {Count(distribution.Left)} ／ 右 {Count(distribution.Right)}";
            }
            if (action is UseCardAction use)
            {
                if (use.Target.HasValue)
                {
                    return (card.IsSelf ? "自分" : "相手") + "の" + (use.Target == HandSide.Left ? "左手" : "右手");
                }
                if (card.IsBoth)
                {
                    return $"左 {(use.SwapValues ? card.Second : card.First)}本 ／ 右 {(use.SwapValues ? card.First : card.Second)}本";
                }
                return "補充する";
            }
            return "攻撃";
        }

        public static string History(CardUseMessage record, int localPlayer)
        {
            var card = new CardDefinition((CardKind)record.kind, record.first, record.second);
            string owner = record.player == localPlayer ? "自分" : "相手";
            string detail = card.IsDraw ? $" → {record.requestedDrawCount}枚抽選、実際に{record.drawnCount}枚補充" :
                card.IsBoth ? $"（左{(record.swapValues ? card.Second : card.First)}・右{(record.swapValues ? card.First : card.Second)}）" :
                record.targetHand >= 0 ? (record.targetHand == (int)HandSide.Left ? "（対象の左手）" : "（対象の右手）") : "";
            return $"{record.turnNumber}手目 {owner}：{Card(card)}{detail}{(record.automatic ? "［自動発動］" : "")}";
        }

        public static string Rejection(RequestResponseMessage response)
        {
            //追加:v1.1の拒否を通常の送信失敗と区別する。
            if (response.ruleRejection == (int)ActionRejection.OwnHandSealed)
            {
                return "封印中の手は自分のカードや手の変更で操作できません";
            }
            if (response.ruleRejection == (int)ActionRejection.SingleHandSwap)
            {
                return "片手を移すだけの左右交換はできません";
            }
            if (response.ruleRejection == (int)ActionRejection.SkipNotAllowed)
            {
                return "最後の生存手が封印された場合だけスキップできます";
            }
            if (response.ruleRejection == (int)ActionRejection.CardUnchanged)
            {
                return "本数が変わらないため使用しませんでした（カード未消費）";
            }
            if (response.ruleRejection == (int)ActionRejection.SourceHandSealed)
            {
                return "封印中の手からは攻撃できません";
            }
            if (response.ruleRejection == (int)ActionRejection.StaleState)
            {
                return "対戦状態が更新されました。選び直してください";
            }
            return "操作は成立しませんでした。現在の手番・対象を確認してください";
        }

        //追加:正式進行が分けたカード結果とスキップを、各表示区間で公開する。
        public static string Presentation(PublicMatchMessage state, int localPlayer)
        {
            if (state == null || state.gate != (int)InputGate.Presenting || state.events == null)
            {
                return null;
            }
            foreach (var item in state.events)
            {
                if (item.type == (int)RuleEventType.TurnSkipped)
                {
                    return (item.player == localPlayer ? "自分：" : "相手：") + "スキップ！";
                }
                if (item.type == (int)RuleEventType.CardUsed && item.cardUse != null && !item.cardUse.automatic)
                {
                    return History(item.cardUse, localPlayer);
                }
            }
            return null;
        }

        public static int RemainingSeconds(double deadline, double serverTime)
        {
            return (int)Math.Ceiling(Math.Max(0, deadline - serverTime));
        }
    }
}
