using System;

namespace Waribasi.MatchFlow
{
    public sealed class NpcTurnTiming
    {
        public long TurnNumber { get; private set; } = -1;
        public double ActionNotBefore { get; private set; }
        public double ThinkMilliseconds { get; private set; }
        public double WaitMilliseconds { get; private set; }
        //追加:結果の公開だけでは待機を満たさず、可読な描画を待つ。
        public bool AwaitingCardDisplay { get; private set; }
        public double RemainingThinkMilliseconds => Math.Max(1, 1000 - ThinkMilliseconds);

        public void ObserveTurn(long turnNumber, double now, double minimumSeconds)
        {
            if (TurnNumber == turnNumber)
            {
                return;
            }
            TurnNumber = turnNumber;
            ActionNotBefore = now + Math.Max(0, minimumSeconds);
            ThinkMilliseconds = WaitMilliseconds = 0;
            //追加:別手番へ古いカードの描画待ちを持ち越さない。
            AwaitingCardDisplay = false;
        }

        //追加:公開直後・非表示中は実際の描画まで正式適用を保留する。
        public void AwaitCardDisplay()
        {
            AwaitingCardDisplay = true;
        }

        //追加:同じ可読区間の再描画では起点を更新しない。
        public void ObserveCardDisplay(bool readable, double now, double minimumSeconds)
        {
            if (!readable)
            {
                AwaitCardDisplay();
            }
            else if (AwaitingCardDisplay)
            {
                CardPublished(now, minimumSeconds);
            }
        }

        //変更:呼出し時刻は配信時ではなく、可読なCanvas描画の開始時刻とする。
        public void CardPublished(double now, double minimumSeconds)
        {
            ActionNotBefore = Math.Max(ActionNotBefore, now + Math.Max(0, minimumSeconds));
            AwaitingCardDisplay = false;
        }

        public bool CanAct(long turnNumber, double now)
        {
            //変更:計算が長くても、描画前だけの時間でカード待機を満たさない。
            //削除:return TurnNumber == turnNumber && now >= ActionNotBefore;
            return !AwaitingCardDisplay && TurnNumber == turnNumber && now >= ActionNotBefore;
        }

        public void RecordThinking(double milliseconds)
        {
            ThinkMilliseconds += Math.Max(0, milliseconds);
        }

        public void RecordWaiting(double milliseconds)
        {
            WaitMilliseconds += Math.Max(0, milliseconds);
        }
    }
}
