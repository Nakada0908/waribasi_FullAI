using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Waribasi.MatchFlow;
using Waribasi.Networking;
using Waribasi.Rules;

namespace Waribasi.Presentation
{
    public sealed class MatchFlowPresenter : MonoBehaviour
    {
        public static MatchFlowPresenter Instance { get; private set; }
        [SerializeField] private GameObject preparationPanel;
        [SerializeField] private TMP_Text preparationText;
        [SerializeField] private Button preparationCancel;
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private GameObject menuChoices;
        [SerializeField] private GameObject confirmationChoices;
        [SerializeField] private TMP_Text confirmationText;
        [SerializeField] private Button surrenderChoice;
        [SerializeField] private Button menuButton;
        [SerializeField] private TMP_Text resultDetail;
        [SerializeField] private TMP_Text rematchStatus;
        [SerializeField] private Button rematchButton;
        [SerializeField] private TMP_Text rematchLabel;
        //追加:開始前と終了時の読める最低時間を別々に設定する。
        [Header("T16 Text Display")]
        [SerializeField, Min(0)] private float minimumPreparationDisplaySeconds = 2.5f;
        [SerializeField, Min(0)] private float minimumResultDisplaySeconds = 2.5f;
        [SerializeField] private TMP_Text resultText;
        private readonly ReadableDisplayTiming preparationTiming = new ReadableDisplayTiming();
        private readonly ReadableDisplayTiming resultTiming = new ReadableDisplayTiming();
        private bool transitionObscured;
        private bool applicationPaused;
        private bool applicationFocused = true;
        private string resultShownMatch;
        private OnlineSessionController session;
        private MatchClientState view;
        private long shownVersion = -1;
        private string shownMatch;
        //削除:状態受信時点を表示開始として数えていた時刻。
        //private float shownAt;
        private string confirmation;
        private bool surrenderPending;
        public bool ModalOpen => menuPanel != null && menuPanel.activeSelf;

        private void Awake()
        {
            Instance = this;
            session = OnlineSessionController.Instance;
            if (session != null)
            {
                session.Changed += Refresh;
            }
            menuPanel.SetActive(false);
        }

        private void Update()
        {
            //追加:パネル自体が外部から無効化された場合も盤面の遮断を残さない。
            var input = MatchInteractionController.Instance;
            if (!ModalOpen && input != null && input.ModalBlocked)
            {
                input.SetModalBlocked(false);
            }
            //変更:ローカルとオンラインで現在の表示状態を切り替える。
            if (view != MatchSessionAccess.State)
            {
                if (view != null)
                {
                    view.Changed -= Refresh;
                }
                //変更:現在のモードの確定状態を購読する。
                view = MatchSessionAccess.State;
                if (view != null)
                {
                    view.Changed += Refresh;
                }
                Refresh();
            }
            var state = view?.Public;
            if (preparationPanel != null)
            {
                bool preparing = state == null || state.matchPhase == (int)MatchPhase.Preparing;
                //変更:準備パネルの表示をフェードへ通し、正式開始後の入力は遮らない。
                //削除:preparationPanel.SetActive(preparing && !(view != null && view.CommunicationInterrupted));
                bool visible = preparing && !(view != null && view.CommunicationInterrupted);
                var fade = PanelFade.For(preparationPanel);
                if (visible)
                {
                    fade.ShowContent(preparationText != null ? preparationText.text : "preparing");
                }
                else
                {
                    fade.SetVisible(false, blockWhileHiding: false);
                }
                if (state != null && (shownVersion != state.stateVersion || shownMatch != state.matchId))
                {
                    shownVersion = state.stateVersion;
                    shownMatch = state.matchId;
                    //変更:時刻はCanvasの描画段階で、表示できる文字を確認してから記録する。
                    //削除:shownAt = Time.unscaledTime;
                    Refresh();
                }
            }
            //追加:勝敗は一度だけフェードし、通常の状態再通知では表示起点を戻さない。
            if (resultText != null && view != null && (view.HasConfirmedResult || view.CommunicationInterrupted ||
                state?.matchPhase == (int)MatchPhase.Interrupted))
            {
                PanelFade.For(resultText.gameObject).ShowContent((state?.matchId ?? "connection-interrupted") + ":result");
            }
            if (surrenderPending)
            {
                if (view == null || view.CommunicationInterrupted || view.HasConfirmedResult)
                {
                    surrenderPending = false;
                }
                //変更:降参を各モードの正式な終了入口へ渡す。
                else if (!view.HasPendingRequest && MatchSessionAccess.Surrender())
                {
                    surrenderPending = false;
                    CloseMenu();
                }
            }
        }

        //追加:描画される段階を表示起点にし、非表示・破棄中は計測を残さない。
        private void OnEnable()
        {
            //追加:新しいSceneのPresenterも、常駐フェードの現在状態を引き継ぐ。
            SetTransitionObscured(PresentationVisibility.Obscured);
            Canvas.willRenderCanvases += ObserveReadableDisplay;
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= ObserveReadableDisplay;
            preparationTiming.Invalidate();
            resultTiming.Invalidate();
            //追加:Controller無効化でも、別オブジェクトのTweenとモーダル遮断を残さない。
            if (preparationPanel != null)
            {
                PanelFade.For(preparationPanel).Finish();
            }
            if (resultText != null)
            {
                PanelFade.For(resultText.gameObject).Finish();
            }
            if (menuPanel != null)
            {
                menuPanel.SetActive(false);
            }
            var input = MatchInteractionController.Instance;
            if (input != null)
            {
                input.SetModalBlocked(false);
            }
        }

        //追加:バックグラウンドに隠れていた時間も読み取り時間へ含めない。
        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (paused)
            {
                preparationTiming.Invalidate();
                resultTiming.Invalidate();
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            applicationFocused = focused;
            if (!focused)
            {
                preparationTiming.Invalidate();
                resultTiming.Invalidate();
            }
        }

        //追加:T20はフェード開始前にtrue、読み終えられる状態になってからfalseを渡す。
        public void SetTransitionObscured(bool obscured)
        {
            //追加:同じ通知でT16の可読時間を毎回リセットしない。
            if (transitionObscured == obscured)
            {
                return;
            }
            transitionObscured = obscured;
            if (obscured)
            {
                preparationTiming.Invalidate();
                resultTiming.Invalidate();
            }
        }

        private void ObserveReadableDisplay()
        {
            var state = view?.Public;
            double now = Time.realtimeSinceStartupAsDouble;
            preparationTiming.Observe(state?.matchId, state?.stateVersion ?? -1, PreparationReadable(state), now);
            //追加:公開盤面の受信前に切断されても、中断文字の表示を同じ経路で完了できる。
            resultTiming.Observe(state?.matchId ?? "connection-interrupted", state?.stateVersion ?? -1, ResultReadable(view), now);
        }

        private bool TextReadable(TMP_Text text)
        {
            if (!isActiveAndEnabled || applicationPaused || !applicationFocused || transitionObscured || ModalOpen ||
                (MatchInteractionController.Instance != null && MatchInteractionController.Instance.HistoryOpen) ||
                text == null || !text.isActiveAndEnabled ||
                text.color.a < 0.999f || text.canvas == null || !text.canvas.isActiveAndEnabled ||
                text.canvasRenderer.GetAlpha() < 0.999f)
            {
                return false;
            }
            //追加:親のCanvasGroupがフェード中なら、その時間を最低表示時間へ含めない。
            for (var parent = text.transform; parent != null; parent = parent.parent)
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

        private bool PreparationReadable(PublicMatchMessage state)
        {
            return state != null && view?.Public == state && state.matchPhase == (int)MatchPhase.Preparing &&
                shownMatch == state.matchId && shownVersion == state.stateVersion &&
                preparationPanel != null && preparationPanel.activeInHierarchy && TextReadable(preparationText) &&
                !string.IsNullOrEmpty(preparationText.text) &&
                preparationText.text.Contains(MatchHudDisplay.InitialOrder(state, view.LocalPlayer)) &&
                preparationText.text.Contains(state.initialCardsDealt ? "初期手札を配りました" : "手札を配ります");
        }

        private bool ResultReadable(MatchClientState current)
        {
            if (current == null || current != view ||
                (!current.HasConfirmedResult && !current.CommunicationInterrupted &&
                current.Public?.matchPhase != (int)MatchPhase.Interrupted) || !TextReadable(resultText))
            {
                return false;
            }
            string expected = current.HasConfirmedResult ? (current.Public.winner == current.LocalPlayer ? "勝利" : "敗北") : "通信中断";
            return resultText.text?.EndsWith(expected, System.StringComparison.Ordinal) == true;
        }

        //追加:結果受信・最後の動きとは別に、実際の勝敗文字の表示完了を照会する。
        public static bool CanLeaveResult(MatchClientState current)
        {
            var presenter = Instance;
            var state = current?.Public;
            return presenter != null && presenter.ResultReadable(current) &&
                presenter.resultTiming.IsComplete(state?.matchId ?? "connection-interrupted", state?.stateVersion ?? -1, Time.realtimeSinceStartupAsDouble,
                    presenter.minimumResultDisplaySeconds);
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.Changed -= Refresh;
            }
            if (view != null)
            {
                view.Changed -= Refresh;
            }
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public static bool CanAcknowledgePreparation(PublicMatchMessage state)
        {
            var presenter = Instance;
            //変更:未生成・非表示の時間を含めず、各準備段階の文字が読めてから応答する。
            //削除:return presenter == null || (state != null && presenter.shownMatch == state.matchId &&
            //    presenter.shownVersion == state.stateVersion && Time.unscaledTime - presenter.shownAt >= 0.75f);
            return presenter != null && presenter.PreparationReadable(state) &&
                presenter.preparationTiming.IsComplete(state.matchId, state.stateVersion, Time.realtimeSinceStartupAsDouble,
                    presenter.minimumPreparationDisplaySeconds);
        }

        public void OpenMenu()
        {
            //追加:フェード中のEscapeや直接呼出しでも二重遷移を始めない。
            if (SceneFade.InputBlocked)
            {
                return;
            }
            MatchHudPresenter.Instance?.ShowHistory(false);
            MatchInteractionController.Instance?.SetModalBlocked(true);
            confirmation = null;
            //変更:既存メニューの配置・参照を保って表示する。
            //削除:menuPanel.SetActive(true);
            PanelFade.For(menuPanel).SetVisible(true);
            menuChoices.SetActive(true);
            confirmationChoices.SetActive(false);
            //変更:時間制限のないNPC戦には操作時計の説明を表示しない。
            confirmationText.text = SceneManager.GetActiveScene().name == "Game" ? (MatchSessionAccess.IsLocal ? "対戦メニュー" : "対戦メニュー\n操作時間は進みます") : "終了メニュー";
            Refresh();
        }

        public void CloseMenu()
        {
            confirmation = null;
            surrenderPending = false;
            //変更:閉じるフェード中もモーダルを維持し、完了後に盤面入力を戻す。
            //削除:menuPanel.SetActive(false);
            //削除:MatchInteractionController.Instance?.SetModalBlocked(false);
            PanelFade.For(menuPanel).SetVisible(false, () =>
            {
                var input = MatchInteractionController.Instance;
                if (this != null && input != null && !ModalOpen)
                {
                    input.SetModalBlocked(false);
                }
            });
        }

        public void HandleEscape()
        {
            //追加:暗転中はメニューを開閉しない。
            if (SceneFade.InputBlocked)
            {
                return;
            }
            if (ModalOpen)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
            }
        }

        public void ConfirmSurrender() => Ask("Surrender", "降参しますか？\n降参すると敗北になります");
        public void ConfirmLeave() => Ask("StartGame", IsActiveMatch() ? "降参して退出しますか？\n降参すると敗北になります" : "モード選択へ戻りますか？");
        public void ConfirmQuit() => Ask("Quit", IsActiveMatch() ? "降参してゲームを終了しますか？" : "ゲームを終了しますか？");

        private void Ask(string action, string text)
        {
            //追加:遷移中の直接呼出しも同じガードへ通す。
            if (SceneFade.InputBlocked)
            {
                return;
            }
            OpenMenu();
            confirmation = action;
            //変更:NPCの確認画面も時間制限なしの表示に揃える。
            confirmationText.text = text + (IsActiveMatch() && !MatchSessionAccess.IsLocal ? "\n操作時間は進みます" : "");
            menuChoices.SetActive(false);
            confirmationChoices.SetActive(true);
        }

        private bool IsActiveMatch()
        {
            return view?.Public != null && !view.CommunicationInterrupted && view.Public.matchPhase == (int)MatchPhase.Active;
        }

        public void AcceptConfirmation()
        {
            //追加:遷移中の直接呼出しも同じガードへ通す。
            if (SceneFade.InputBlocked)
            {
                return;
            }
            if (confirmation == null)
            {
                return;
            }
            if (confirmation == "Surrender")
            {
                if (!IsActiveMatch())
                {
                    CloseMenu();
                    return;
                }
                surrenderPending = true;
                //変更:ローカル処理とオンラインの応答待ちを区別する。
                confirmationText.text = (MatchSessionAccess.IsLocal ? "操作を確定しています" : "操作の応答を待っています\n操作時間は進みます");
                confirmationChoices.SetActive(false);
            }
            else
            {
                string destination = confirmation;
                CloseMenu();
                //変更:NPCはオンライン退出を経由しない。
                MatchSessionAccess.Leave(destination);
            }
        }

        public void CancelPreparation()
        {
            //追加:遷移中の直接呼出しも同じガードへ通す。
            if (SceneFade.InputBlocked)
            {
                return;
            }
            //変更:NPC準備は勝敗を付けずに設定へ戻す。
            if (MatchSessionAccess.IsLocal)
            {
                MatchSessionAccess.Local.Leave("StartGame");
            }
            else
            {
                session?.CancelPreparation();
            }
        }

        public void Rematch()
        {
            //追加:遷移中の直接呼出しも同じガードへ通す。
            if (SceneFade.InputBlocked)
            {
                return;
            }
            //変更:NPCの再戦は人間の操作だけで開始する。
            if (MatchSessionAccess.IsLocal)
            {
                MatchSessionAccess.Local.Rematch();
            }
            else
            {
                session?.RequestRematch(!session.LocalRematch);
            }
        }

        public void ReturnMode()
        {
            //追加:遷移中の直接呼出しも同じガードへ通す。
            if (SceneFade.InputBlocked)
            {
                return;
            }
            if (IsActiveMatch())
            {
                ConfirmLeave();
            }
            else
            {
                //変更:現在の対戦モードから準備へ戻る。
                MatchSessionAccess.Leave("StartGame");
            }
        }

        //削除:Endのタイトル操作を撤去し、モード選択への戻りへ統一する。
        //public void ReturnTitle()
        //{
        //    if (IsActiveMatch())
        //    {
        //        Ask("Title", "降参してタイトルへ戻りますか？");
        //    }
        //    else
        //    {
        //        //変更:現在の対戦モードを終了してタイトルへ戻る。
        //        MatchSessionAccess.Leave("Title");
        //    }
        //}

        public static string ResultReason(MatchClientState current)
        {
            if (current == null || !current.HasConfirmedResult)
            {
                return "通信が中断されたため、勝敗は確定していません";
            }
            switch ((MatchEndReason)current.Public.endReason)
            {
                case MatchEndReason.HandsEliminated:
                    return "両手の脱落で決着しました";
                case MatchEndReason.Surrender:
                    return current.Public.winner == current.LocalPlayer ? "相手が降参しました" : "降参しました";
                case MatchEndReason.Timeout:
                    return current.Public.winner == current.LocalPlayer ? "相手の操作時間が切れました" : "操作時間が切れました";
                case MatchEndReason.Disconnected:
                    return "相手との接続が終了しました";
                default:
                    return "対戦が終了しました";
            }
        }

        private void Refresh()
        {
            var state = view?.Public;
            //追加:NPC用の待機と再戦表示を選ぶ。
            bool local = MatchSessionAccess.IsLocal;
            bool active = IsActiveMatch();
            //追加:決着後に残ったメニューや履歴で勝敗文字を隠さない。切断通知で同じ結果の待ちを再開しない。
            if (view != null && (view.HasConfirmedResult || view.CommunicationInterrupted ||
                state?.matchPhase == (int)MatchPhase.Interrupted) && resultShownMatch != (state?.matchId ?? "connection-interrupted"))
            {
                resultShownMatch = state?.matchId ?? "connection-interrupted";
                CloseMenu();
                MatchHudPresenter.Instance?.ShowHistory(false);
            }
            if (preparationText != null)
            {
                //変更:先後手は開始時の主表示に集約し、同じ準備経路で再戦にも表示する。
                //削除:string order = state == null ? (local ? "盤面を準備しています" : "通信と盤面を準備しています") :
                //    (state.currentPlayer == view.LocalPlayer ? "自分が先手 ／ 相手が後手" : "相手が先手 ／ 自分が後手");
                string order = state == null ? (local ? "盤面を準備しています" : "通信と盤面を準備しています") :
                    "<size=180%><b>" + MatchHudDisplay.InitialOrder(state, view.LocalPlayer) + "</b></size>";
                //変更:NPC戦の開始表示から操作時計の行も除く。
                //削除:preparationText.text = order + "\n" + (state == null ? "" : "毎手番：" + MatchSetupPresenter.TimeLabel(state.turnSeconds)) +
                preparationText.text = order + "\n" + (state == null || local ? "" : "毎手番：" + MatchSetupPresenter.TimeLabel(state.turnSeconds)) +
                    "\n" + (state != null && state.initialCardsDealt ? (local ? "初期手札を配りました。対戦を開始します" : "初期手札を配りました。双方の準備を待っています") : (local ? "これから初期手札を配ります" : "双方の準備完了後に手札を配ります"));
                //変更:NPC準備中の取消も有効にする。
                preparationCancel.interactable = local || (session != null && !session.IsWorking);
            }
            surrenderChoice.gameObject.SetActive(active);
            if (menuButton != null)
            {
                menuButton.interactable = session == null || !session.IsWorking;
            }
            if (view != null && (view.HasConfirmedResult || view.CommunicationInterrupted) && confirmation == "Surrender")
            {
                CloseMenu();
            }
            if (resultDetail != null)
            {
                resultDetail.text = ResultReason(view);
                //変更:NPCの結果には相手の接続や合意を要求しない。
                if (local)
                {
                    rematchButton.interactable = MatchSessionAccess.Local.CanRematch;
                    rematchLabel.text = "同じ設定で再戦";
                    rematchStatus.text = MatchSessionAccess.Local.IsWorking ? "次の対戦を準備しています" : "自分の操作だけで再戦できます";
                    return;
                }
                bool can = session != null && session.CanRematch;
                rematchButton.interactable = can;
                rematchLabel.text = session != null && session.LocalRematch ? "再戦待ちを取り消す" : "同じ相手と再戦";
                rematchStatus.text = session == null || session.OpponentLeft ? "相手との接続は終了しました" :
                    session.Loading ? "次の対戦を準備しています" :
                    session.LocalRematch ? "相手の再戦選択を待っています" :
                    session.OpponentRematch ? "相手が再戦を希望しています" :
                    can ? "双方が再戦を選ぶと次の試合を始めます" : "接続と結果の確認を待っています";
            }
        }
    }
}

//削除:T06の共通入力・ローカル接続へ移行する前の箇所を参照用に保存する。
//view != MatchClientState.Instance
//view = MatchClientState.Instance;
//!view.HasPendingRequest && MatchNetworkController.Instance != null && MatchNetworkController.Instance.SendSurrender()
//OnlineSessionController.Ensure().RequestLeave(destination);
//OnlineSessionController.Ensure().RequestLeave("StartGame");
//OnlineSessionController.Ensure().RequestLeave("Title");
//            session?.CancelPreparation();
//            session?.RequestRematch(!session.LocalRematch);
//"対戦メニュー\n操作時間は進みます"
//(IsActiveMatch() ? "\n操作時間は進みます" : "")
//"操作の応答を待っています\n操作時間は進みます"
//state == null ? "通信と盤面を準備しています"
//"初期手札を配りました。双方の準備を待っています" : "双方の準備完了後に手札を配ります"
//preparationCancel.interactable = session != null && !session.IsWorking;
