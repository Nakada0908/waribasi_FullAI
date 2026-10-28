using System.Text;
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
        [SerializeField] private TMP_Text fingersText;
        //変更:旧Messageは必要な操作結果だけを短時間表示する。
        [SerializeField] private TMP_Text messageText;
        //追加:相手の裏札は公開枚数だけで表示し、中身を受け取らない。
        [SerializeField] private GameObject[] opponentCardBacks;
        [SerializeField] private float opponentCardSpacing = 86;
        [SerializeField, Min(0.5f)] private float notificationSeconds = 3;
        [SerializeField] private GameObject historyPanel;
        [SerializeField] private TMP_Text historyText;
        [SerializeField] private Button historyButton;
        [SerializeField] private Button historyCloseButton;
        [SerializeField] private ScrollRect historyScroll;
        [SerializeField] private Color urgentColor = new Color(0.85f, 0.08f, 0.08f);
        //追加:現在の公開版と通知の期限を分け、札の選択で時計をリセットしない。
        private string renderedMatch;
        private long renderedVersion = -1;
        private int renderedHistory = -1;
        //削除:HUDの再描画時刻を受信時刻として扱っていた代替時計。
        //private PublicMatchMessage clockState;
        //private double clockReceivedAt;
        private string notification;
        //変更:受信時刻ではなく可読な描画時刻から通知の期限を数える。
        //削除:private float notificationUntil;
        private double notificationUntil = -1;
        private long notificationVersion = -1;
        private bool transitionObscured;
        private bool applicationPaused;
        private bool applicationFocused = true;

        private void Awake()
        {
            Instance = this;
            historyPanel.SetActive(false);
            historyButton.onClick.AddListener(() => ShowHistory(true));
            historyCloseButton.onClick.AddListener(() => ShowHistory(false));
            //追加:通常リリースでは本数の開発表示を起動時から無効化する。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            fingersText.gameObject.SetActive(true);
#else
            fingersText.gameObject.SetActive(false);
#endif
            interaction.Changed += Refresh;
            //追加:失敗通知は常設説明と分離して購読する。
            interaction.Notification += ShowNotification;
            Refresh();
        }

        //追加:Canvasの描画段階で結果表示を確認し、計算前の配信だけを起点にしない。
        private void OnEnable()
        {
            //追加:Sceneロード中に生成されても可読時間を先に開始しない。
            SetTransitionObscured(PresentationVisibility.Obscured);
            Canvas.willRenderCanvases += ObserveReadableDisplay;
            if (interaction != null)
            {
                Refresh();
            }
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= ObserveReadableDisplay;
            MatchSessionAccess.Local?.ObserveCardResultDisplay(interaction?.ClientState?.Public, false);
            notification = null;
            notificationUntil = -1;
            notificationVersion = -1;
            renderedVersion = -1;
            //追加:Controller無効化で履歴と入力遮断を残さない。
            if (historyPanel != null)
            {
                historyPanel.SetActive(false);
            }
            if (interaction != null)
            {
                interaction.SetHistoryOpen(false);
            }
            if (messageText != null)
            {
                messageText.text = "";
                messageText.gameObject.SetActive(false);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            InvalidateReadableDisplay();
        }

        private void OnApplicationFocus(bool focused)
        {
            applicationFocused = focused;
            InvalidateReadableDisplay();
        }

        //追加:T20はフェード開始前にtrue、完全に読める状態へ戻してからfalseを渡す。
        public void SetTransitionObscured(bool obscured)
        {
            //追加:同一フラグの再通知でカードの描画起点を繰り返し破棄しない。
            if (transitionObscured == obscured)
            {
                return;
            }
            transitionObscured = obscured;
            InvalidateReadableDisplay();
        }

        private void InvalidateReadableDisplay()
        {
            notificationUntil = -1;
            MatchSessionAccess.Local?.ObserveCardResultDisplay(interaction?.ClientState?.Public, false);
        }

        private bool NotificationReadable()
        {
            if (!isActiveAndEnabled || transitionObscured || applicationPaused || !applicationFocused ||
                interaction == null || interaction.HistoryOpen || MatchFlowPresenter.Instance?.ModalOpen == true ||
                messageText == null || !messageText.isActiveAndEnabled || messageText.color.a < 0.999f ||
                messageText.canvas == null || !messageText.canvas.isActiveAndEnabled ||
                messageText.canvasRenderer.GetAlpha() < 0.999f)
            {
                return false;
            }
            for (var parent = messageText.transform; parent != null; parent = parent.parent)
            {
                foreach (var group in parent.GetComponents<CanvasGroup>())
                {
                    if (group.enabled && group.alpha < 0.999f)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private void ObserveReadableDisplay()
        {
            var view = interaction?.ClientState;
            var state = view?.Public;
            bool readable = NotificationReadable() && !string.IsNullOrEmpty(notification) &&
                messageText.text.Contains(notification) && view != null && !view.CommunicationInterrupted &&
                state?.matchPhase == (int)MatchPhase.Active;
            if (readable && notificationUntil < 0)
            {
                //追加:InspectorでNPC待機を延ばしても通知が先に消えない。
                double seconds = MatchSessionAccess.Local?.IsWaitingForCardResult(state) == true
                    ? System.Math.Max(notificationSeconds, MatchSessionAccess.Local.MinimumCardResultSeconds) : notificationSeconds;
                notificationUntil = Time.realtimeSinceStartupAsDouble + seconds;
            }
            else if (!readable && !string.IsNullOrEmpty(notification))
            {
                notificationUntil = -1;
            }
            MatchSessionAccess.Local?.ObserveCardResultDisplay(state, readable && notificationVersion == state?.stateVersion);
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
                //追加:Scene離脱後に古い通知先を残さない。
                interaction.Notification -= ShowNotification;
            }
        }

        public void ShowHistory(bool visible)
        {
            //追加:履歴・メニューへ隠れる前に待機起点を無効化する。
            InvalidateReadableDisplay();
            //変更:閉じ終わるまでは履歴の遮断を維持する。通常の正式時計は止めない。
            //削除:historyPanel.SetActive(visible);
            //削除:historyButton.gameObject.SetActive(!visible);
            //削除:interaction.SetHistoryOpen(visible);
            if (visible && SceneFade.InputBlocked)
            {
                return;
            }
            if (visible)
            {
                historyButton.gameObject.SetActive(false);
                interaction.SetHistoryOpen(true);
            }
            PanelFade.For(historyPanel).SetVisible(visible, () =>
            {
                if (this != null && interaction != null && historyPanel != null && !historyPanel.activeSelf)
                {
                    historyButton.gameObject.SetActive(true);
                    interaction.SetHistoryOpen(false);
                }
            });
        }

        //変更:常設の操作説明・枚数・先後手を描かず、現在の状態と必要な結果を表示する。
        private void Refresh()
        {
            //追加:無効化中の購読通知で古い表示を再生成しない。
            if (!isActiveAndEnabled)
            {
                return;
            }
            var view = interaction.ClientState;
            var state = view?.Public;
            string npcLabel = MatchSessionAccess.IsLocal
                ? NpcMatchEntry.DifficultyLabel(MatchSessionAccess.Local.Session.Setup.Difficulty) : null;
            turnText.text = MatchHudDisplay.Turn(state, view?.LocalPlayer ?? -1, view?.CommunicationInterrupted ?? false, npcLabel);
            //変更:時計標本はMatchClientStateが実受信時に保持する。HUDの再描画では更新しない。
            //削除:if (clockState != state)
            //{
            //    clockState = state;
            //    clockReceivedAt = Time.unscaledTimeAsDouble;
            //}
            if (state == null || renderedMatch != state.matchId)
            {
                renderedMatch = state?.matchId;
                renderedVersion = -1;
                renderedHistory = -1;
                notification = null;
                notificationVersion = -1;
                notificationUntil = -1;
            }
            //追加:終了・切断後には前のカード通知を残さない。
            if (view?.CommunicationInterrupted == true || (state != null && state.matchPhase != (int)MatchPhase.Active))
            {
                notification = null;
                notificationVersion = -1;
            }
            //変更:本人札が古い場合も可否と半透明表示を再評価する。
            CardButtonDraw.Instance?.DrawOwnHand(view?.OwnHand ?? System.Array.Empty<HandCard>());
            DrawOpponentCards(MatchHudDisplay.OpponentCardCount(state, view?.LocalPlayer ?? -1));
            if (state == null)
            {
                fingersText.text = "";
                UpdateTimer();
                UpdateNotification();
                return;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            //変更:本数の文字列も開発条件内だけで組み立てる。
            int me = view.LocalPlayer * 2;
            int opponent = (1 - view.LocalPlayer) * 2;
            fingersText.text = $"自分 左 {MatchText.Count(state.hands[me].count)}・右 {MatchText.Count(state.hands[me + 1].count)}\n相手 左 {MatchText.Count(state.hands[opponent].count)}・右 {MatchText.Count(state.hands[opponent + 1].count)}";
#endif
            //追加:使用済みの公開イベントから札の結果を一度だけ通知する。
            if (renderedVersion != state.stateVersion)
            {
                renderedVersion = state.stateVersion;
                //変更:次の行動・手番の更新だけでは読んでいるカード通知を消さない。
                //削除:notification = null;
                if (notificationVersion < 0)
                {
                    notification = null;
                }
                if (state.events != null && state.matchPhase == (int)MatchPhase.Active && !view.CommunicationInterrupted)
                {
                    foreach (var item in state.events)
                    {
                        if (item.type == (int)RuleEventType.CardUsed && item.cardUse != null)
                        {
                            //追加:新しい札だけは前のカード本文と混ぜず更新する。
                            notification = null;
                            notificationVersion = -1;
                            ShowNotification(MatchText.History(item.cardUse, view.LocalPlayer));
                            notificationVersion = state.stateVersion;
                        }
                    }
                }
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
            historyButton.GetComponentInChildren<TMP_Text>(true).text = view.History.Count < state.historyCount
                ? "履歴（取得中）" : "公開履歴 " + view.History.Count;
            UpdateTimer();
            UpdateNotification();
        }

        //追加:裏札の表示には枚数以外の入力を渡さない。
        private void DrawOpponentCards(int count)
        {
            if (opponentCardBacks == null)
            {
                return;
            }
            for (int index = 0; index < opponentCardBacks.Length; index++)
            {
                var card = opponentCardBacks[index];
                if (card == null)
                {
                    continue;
                }
                card.SetActive(index < count);
                if (index < count)
                {
                    ((RectTransform)card.transform).anchoredPosition = new Vector2((index - (count - 1) * 0.5f) * opponentCardSpacing, 0);
                }
            }
        }

        //追加:失敗と公開された札の結果を表示し、期限後には表示領域ごと隠す。
        private void ShowNotification(string text)
        {
            //変更:結果待ち中の失敗通知はカード本文に添え、結果を上書きしない。
            if (!isActiveAndEnabled)
            {
                return;
            }
            bool keepingCard = MatchSessionAccess.Local?.IsWaitingForCardResult(interaction.ClientState?.Public) == true &&
                notificationVersion >= 0 && !string.IsNullOrEmpty(notification);
            notification = keepingCard ? notification + "\n" + text : text;
            if (!keepingCard)
            {
                notificationVersion = -1;
                //削除:notificationUntil = Time.unscaledTime + notificationSeconds;
                notificationUntil = -1;
            }
            UpdateNotification();
        }

        private void UpdateNotification()
        {
            var view = interaction.ClientState;
            string text = MatchText.Presentation(view?.Public, view?.LocalPlayer ?? -1);
            //変更:カード結果は次の状態でも期限まで残し、スキップ通知と同時に読めるようにする。
            //削除:if (text == null && Time.unscaledTime < notificationUntil)
            bool pendingCard = MatchSessionAccess.Local?.IsWaitingForCardResult(view?.Public) == true;
            if (!string.IsNullOrEmpty(notification) && (notificationUntil < 0 ||
                Time.realtimeSinceStartupAsDouble < notificationUntil || pendingCard))
            {
                text = text == null || notification.Contains(text) ? notification : text + "\n" + notification;
            }
            else
            {
                notification = null;
                notificationVersion = -1;
            }
            //変更:カード画像のCanvasGroupには触れず、通知本文だけをフェードする。
            //削除:messageText.text = text ?? "";
            //削除:messageText.gameObject.SetActive(!string.IsNullOrEmpty(text));
            var fade = PanelFade.For(messageText.gameObject);
            if (!string.IsNullOrEmpty(text))
            {
                messageText.text = text;
                fade.ShowContent(text);
            }
            else
            {
                fade.SetVisible(false, () =>
                {
                    //追加:閉じる間は本文を保持し、非表示になってから空にする。
                    if (this != null && messageText != null && !messageText.gameObject.activeSelf)
                    {
                        messageText.text = "";
                    }
                });
            }
        }

        private void Update()
        {
            //追加:履歴パネルの外部無効化でも閉じる操作と同じ入力状態へ戻す。
            if (historyPanel != null && !historyPanel.activeSelf && interaction != null && interaction.HistoryOpen)
            {
                historyButton.gameObject.SetActive(true);
                interaction.SetHistoryOpen(false);
            }
            //追加:Controllerの再有効化時、現在のカード待機に必要な本文を再生成する。
            var state = interaction.ClientState?.Public;
            if (MatchSessionAccess.Local?.IsWaitingForCardResult(state) == true && notificationVersion != state.stateVersion)
            {
                renderedVersion = -1;
                Refresh();
            }
            UpdateTimer();
            //追加:通知は状態配信がなくても指定時間で消す。
            UpdateNotification();
        }

        //変更:同期時計を毎フレーム参照し、演出中・NPC・制限なしを区別する。
        private void UpdateTimer()
        {
            var view = interaction.ClientState;
            var state = view?.Public;
            bool visible = MatchHudDisplay.TimerVisible(state, MatchSessionAccess.IsLocal, view?.CommunicationInterrupted ?? false);
            timerText.gameObject.SetActive(visible);
            if (!visible)
            {
                timerText.text = "";
                return;
            }
            var manager = NetworkManager.Singleton;
            //変更:接続中は正式期限と同じServerTimeだけを使用し、経過時間を二重加算しない。
            //代替時計の経過は受信時と同じrealtime基準で一度だけ加算する。
            //削除:double now = manager != null && manager.IsListening ? manager.ServerTime.Time :
            //    MatchHudDisplay.EstimatedServerTime(state.serverTime, clockReceivedAt, Time.unscaledTimeAsDouble);
            double now = manager != null && manager.IsListening ? manager.ServerTime.Time :
                MatchHudDisplay.EstimatedServerTime(state.serverTime, view.PublicReceivedAt, Time.realtimeSinceStartupAsDouble);
            timerText.text = MatchHudDisplay.Timer(state, now);
            timerText.color = MatchHudDisplay.TimerUrgent(state, now) ? urgentColor : new Color(0.12f, 0.18f, 0.23f);
        }
    }
}

//削除:T12で撤去した常設説明・先後手の繰返しと旧時計を参照用に残す。
//        [SerializeField] private TMP_Text cardCountsText;
//        [SerializeField] private TMP_Text cardPhaseText;
//        private string initialOrder;
//        private string orderMatch;
//        private void Refresh()
//        {
//            var view = interaction.ClientState;
//            if (view?.Public == null)
//            {
//                turnText.text = "対戦準備中";
//                timerText.text = "";
//                cardCountsText.text = "手札：準備中";
//                fingersText.text = "";
//                cardPhaseText.text = "";
//                messageText.text = "接続と盤面の準備を待っています";
//                return;
//            }
//            var state = view.Public;
//            if (orderMatch != state.matchId)
//            {
//                orderMatch = state.matchId;
//                initialOrder = state.currentPlayer == view.LocalPlayer ? "自分が先手" : "相手が先手";
//                renderedHistory = -1;
//            }
//            turnText.text = initialOrder + " ／ " + (view.CommunicationInterrupted || state.matchPhase == (int)MatchPhase.Interrupted
//                ? "通信中断" : state.matchPhase == (int)MatchPhase.Finished ? (state.winner == view.LocalPlayer ? "勝利" : "敗北") :
//                state.matchPhase == (int)MatchPhase.Preparing ? "対戦準備中" : state.gate == (int)InputGate.Presenting ? "行動を反映中" :
//                state.currentPlayer == view.LocalPlayer ? "自分の手番" : "相手の手番");
//            //追加:最強のゲーム画面には難易度名だけを示す。
//            if (MatchSessionAccess.IsLocal)
//            {
//                turnText.text = NpcMatchEntry.DifficultyLabel(MatchSessionAccess.Local.Session.Setup.Difficulty) + " ／ " + turnText.text;
//            }
//            cardCountsText.text = $"自分の手札 { (view.LocalPlayer == 0 ? state.playerACardCount : state.playerBCardCount)}枚 ／ 相手 { (view.LocalPlayer == 0 ? state.playerBCardCount : state.playerACardCount)}枚";
//            int me = view.LocalPlayer * 2;
//            int opponent = (1 - view.LocalPlayer) * 2;
//            fingersText.text = $"自分　左 {MatchText.Count(state.hands[me].count)} ・ 右 {MatchText.Count(state.hands[me + 1].count)}\n相手　左 {MatchText.Count(state.hands[opponent].count)} ・ 右 {MatchText.Count(state.hands[opponent + 1].count)}";
//            //変更:NPCの手番はネットワーク応答待ちと区別する。
//            messageText.text = MatchSessionAccess.IsLocal && state.matchPhase == (int)MatchPhase.Active &&
//                state.currentPlayer != view.LocalPlayer ? "NPCが考えています" :
//                view.HasPendingRequest ? "操作の応答を待っています" : interaction.Message.Split('\n')[0];
//            cardPhaseText.text = state.matchPhase == (int)MatchPhase.Active && state.currentPlayer == view.LocalPlayer
//                ? state.turnPhase == (int)TurnPhase.CardOrNormalAction ? "カード任意で1枚 → 攻撃か振り分け1回" : "カード使用済み → 攻撃か振り分け1回" : "公開履歴はいつでも確認できます";
//            //追加:カード結果の表示区間とスキップ表示を、NPC思考や通常説明より優先する。
//            string presentation = MatchText.Presentation(state, view.LocalPlayer);
//            if (presentation != null)
//            {
//                messageText.text = presentation;
//            }
//            else if (view.CanAct && interaction.LegalActions.Any(action => action is SkipAction))
//            {
//                messageText.text = "片手が封印中です。カードを使うか「カードを使わない」を選択";
//                cardPhaseText.text = "カード判断後、通常行動をスキップします";
//            }
//            //変更:本人札の到着待ちでも旧表示を操作不能へ更新し、古い可否を残さない。
//            //削除:if (CardButtonDraw.Instance != null && view.HandIsCurrent)
//            if (CardButtonDraw.Instance != null)
//            {
//                CardButtonDraw.Instance.DrawOwnHand(view.OwnHand);
//            }
//            if (renderedHistory != view.History.Count)
//            {
//                renderedHistory = view.History.Count;
//                var builder = new StringBuilder();
//                foreach (var record in view.History)
//                {
//                    builder.AppendLine(MatchText.History(record, view.LocalPlayer));
//                    builder.AppendLine();
//                }
//                historyText.text = builder.Length > 0 ? builder.ToString() : "まだカードは使われていません";
//            }
//            if (view.History.Count < state.historyCount)
//            {
//                historyButton.GetComponentInChildren<TMP_Text>(true).text = "履歴（取得中）";
//            }
//            else
//            {
//                historyButton.GetComponentInChildren<TMP_Text>(true).text = "公開履歴 " + view.History.Count;
//            }
//            UpdateTimer();
//        }
//
//
//        private void UpdateTimer()
//        {
//            var view = interaction.ClientState;
//            var state = view?.Public;
//            if (state == null)
//            {
//                return;
//            }
//            timerText.color = new Color(0.12f, 0.18f, 0.23f);
//            if (state.matchPhase != (int)MatchPhase.Active || view.CommunicationInterrupted)
//            {
//                timerText.text = state.turnSeconds == 0 ? "制限なし" : $"毎手番 {state.turnSeconds}秒";
//            }
//            else if (state.gate == (int)InputGate.Presenting)
//            {
//                timerText.text = "演出後に次の手番を開始";
//            }
//            else if (!state.hasDeadline)
//            {
//                timerText.text = "制限なし";
//            }
//            else
//            {
//                double now = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : state.serverTime;
//                int seconds = MatchText.RemainingSeconds(state.deadline, now);
//                timerText.text = seconds == 0 ? "残り 0秒（ホスト判定待ち）" : $"残り {seconds}秒";
//                if (state.deadline - now <= 10)
//                {
//                    timerText.color = urgentColor;
//                    timerText.text = "! " + timerText.text;
//                }
//            }
//        }
