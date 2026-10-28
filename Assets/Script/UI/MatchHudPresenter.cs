using System.Text;
//追加:共通ルールのスキップ候補から判断待ちを表示する。
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Waribasi.MatchFlow;
using Waribasi.Networking;
using Waribasi.Rules;

namespace Waribasi.Presentation
{
    public sealed class MatchHudPresenter : MonoBehaviour
    {
        public static MatchHudPresenter Instance { get; private set; }
        [SerializeField] private MatchInteractionController interaction;
        [SerializeField] private TMP_Text turnText;
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private TMP_Text cardCountsText;
        [SerializeField] private TMP_Text fingersText;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private TMP_Text cardPhaseText;
        [SerializeField] private GameObject historyPanel;
        [SerializeField] private TMP_Text historyText;
        [SerializeField] private Button historyButton;
        [SerializeField] private Button historyCloseButton;
        [SerializeField] private ScrollRect historyScroll;
        [SerializeField] private Color urgentColor = new Color(0.85f, 0.08f, 0.08f);
        private string initialOrder;
        private string orderMatch;
        private int renderedHistory = -1;

        private void Awake()
        {
            Instance = this;
            historyPanel.SetActive(false);
            historyButton.onClick.AddListener(() => ShowHistory(true));
            historyCloseButton.onClick.AddListener(() => ShowHistory(false));
            interaction.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            if (interaction != null)
            {
                interaction.Changed -= Refresh;
            }
        }

        public void ShowHistory(bool visible)
        {
            historyPanel.SetActive(visible);
            historyButton.gameObject.SetActive(!visible);
            interaction.SetHistoryOpen(visible);
        }

        private void Refresh()
        {
            var view = interaction.ClientState;
            if (view?.Public == null)
            {
                turnText.text = "対戦準備中";
                timerText.text = "";
                cardCountsText.text = "手札：準備中";
                fingersText.text = "";
                cardPhaseText.text = "";
                messageText.text = "接続と盤面の準備を待っています";
                return;
            }
            var state = view.Public;
            if (orderMatch != state.matchId)
            {
                orderMatch = state.matchId;
                initialOrder = state.currentPlayer == view.LocalPlayer ? "自分が先手" : "相手が先手";
                renderedHistory = -1;
            }
            turnText.text = initialOrder + " ／ " + (view.CommunicationInterrupted || state.matchPhase == (int)MatchPhase.Interrupted
                ? "通信中断" : state.matchPhase == (int)MatchPhase.Finished ? (state.winner == view.LocalPlayer ? "勝利" : "敗北") :
                state.matchPhase == (int)MatchPhase.Preparing ? "対戦準備中" : state.gate == (int)InputGate.Presenting ? "行動を反映中" :
                state.currentPlayer == view.LocalPlayer ? "自分の手番" : "相手の手番");
            //追加:最強のゲーム画面には難易度名だけを示す。
            if (MatchSessionAccess.IsLocal)
            {
                turnText.text = NpcMatchEntry.DifficultyLabel(MatchSessionAccess.Local.Session.Setup.Difficulty) + " ／ " + turnText.text;
            }
            cardCountsText.text = $"自分の手札 { (view.LocalPlayer == 0 ? state.playerACardCount : state.playerBCardCount)}枚 ／ 相手 { (view.LocalPlayer == 0 ? state.playerBCardCount : state.playerACardCount)}枚";
            int me = view.LocalPlayer * 2;
            int opponent = (1 - view.LocalPlayer) * 2;
            fingersText.text = $"自分　左 {MatchText.Count(state.hands[me].count)} ・ 右 {MatchText.Count(state.hands[me + 1].count)}\n相手　左 {MatchText.Count(state.hands[opponent].count)} ・ 右 {MatchText.Count(state.hands[opponent + 1].count)}";
            //変更:NPCの手番はネットワーク応答待ちと区別する。
            messageText.text = MatchSessionAccess.IsLocal && state.matchPhase == (int)MatchPhase.Active &&
                state.currentPlayer != view.LocalPlayer ? "NPCが考えています" :
                view.HasPendingRequest ? "操作の応答を待っています" : interaction.Message.Split('\n')[0];
            cardPhaseText.text = state.matchPhase == (int)MatchPhase.Active && state.currentPlayer == view.LocalPlayer
                ? state.turnPhase == (int)TurnPhase.CardOrNormalAction ? "カード任意で1枚 → 攻撃か振り分け1回" : "カード使用済み → 攻撃か振り分け1回" : "公開履歴はいつでも確認できます";
            //追加:カード結果の表示区間とスキップ表示を、NPC思考や通常説明より優先する。
            string presentation = MatchText.Presentation(state, view.LocalPlayer);
            if (presentation != null)
            {
                messageText.text = presentation;
            }
            else if (view.CanAct && interaction.LegalActions.Any(action => action is SkipAction))
            {
                messageText.text = "片手が封印中です。カードを使うか「カードを使わない」を選択";
                cardPhaseText.text = "カード判断後、通常行動をスキップします";
            }
            //変更:本人札の到着待ちでも旧表示を操作不能へ更新し、古い可否を残さない。
            //削除:if (CardButtonDraw.Instance != null && view.HandIsCurrent)
            if (CardButtonDraw.Instance != null)
            {
                CardButtonDraw.Instance.DrawOwnHand(view.OwnHand);
            }
            if (renderedHistory != view.History.Count)
            {
                renderedHistory = view.History.Count;
                var builder = new StringBuilder();
                foreach (var record in view.History)
                {
                    builder.AppendLine(MatchText.History(record, view.LocalPlayer));
                    builder.AppendLine();
                }
                historyText.text = builder.Length > 0 ? builder.ToString() : "まだカードは使われていません";
            }
            if (view.History.Count < state.historyCount)
            {
                historyButton.GetComponentInChildren<TMP_Text>(true).text = "履歴（取得中）";
            }
            else
            {
                historyButton.GetComponentInChildren<TMP_Text>(true).text = "公開履歴 " + view.History.Count;
            }
            UpdateTimer();
        }

        private void Update()
        {
            UpdateTimer();
        }

        private void UpdateTimer()
        {
            var view = interaction.ClientState;
            var state = view?.Public;
            if (state == null)
            {
                return;
            }
            timerText.color = new Color(0.12f, 0.18f, 0.23f);
            if (state.matchPhase != (int)MatchPhase.Active || view.CommunicationInterrupted)
            {
                timerText.text = state.turnSeconds == 0 ? "制限なし" : $"毎手番 {state.turnSeconds}秒";
            }
            else if (state.gate == (int)InputGate.Presenting)
            {
                timerText.text = "演出後に次の手番を開始";
            }
            else if (!state.hasDeadline)
            {
                timerText.text = "制限なし";
            }
            else
            {
                double now = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : state.serverTime;
                int seconds = MatchText.RemainingSeconds(state.deadline, now);
                timerText.text = seconds == 0 ? "残り 0秒（ホスト判定待ち）" : $"残り {seconds}秒";
                if (state.deadline - now <= 10)
                {
                    timerText.color = urgentColor;
                    timerText.text = "! " + timerText.text;
                }
            }
        }
    }
}

//削除:T06の共通入力・ローカル接続へ移行する前の箇所を参照用に保存する。
//            messageText.text = view.HasPendingRequest ? "操作の応答を待っています" : interaction.Message.Split('\n')[0];
