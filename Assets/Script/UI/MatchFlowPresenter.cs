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
        private OnlineSessionController session;
        private MatchClientState view;
        private long shownVersion = -1;
        private string shownMatch;
        private float shownAt;
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
            if (view != MatchClientState.Instance)
            {
                if (view != null)
                {
                    view.Changed -= Refresh;
                }
                view = MatchClientState.Instance;
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
                preparationPanel.SetActive(preparing && !(view != null && view.CommunicationInterrupted));
                if (state != null && (shownVersion != state.stateVersion || shownMatch != state.matchId))
                {
                    shownVersion = state.stateVersion;
                    shownMatch = state.matchId;
                    shownAt = Time.unscaledTime;
                    Refresh();
                }
            }
            if (surrenderPending)
            {
                if (view == null || view.CommunicationInterrupted || view.HasConfirmedResult)
                {
                    surrenderPending = false;
                }
                else if (!view.HasPendingRequest && MatchNetworkController.Instance != null && MatchNetworkController.Instance.SendSurrender())
                {
                    surrenderPending = false;
                    CloseMenu();
                }
            }
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
            return presenter == null || (state != null && presenter.shownMatch == state.matchId &&
                presenter.shownVersion == state.stateVersion && Time.unscaledTime - presenter.shownAt >= 0.75f);
        }

        public void OpenMenu()
        {
            MatchHudPresenter.Instance?.ShowHistory(false);
            MatchInteractionController.Instance?.SetModalBlocked(true);
            confirmation = null;
            menuPanel.SetActive(true);
            menuChoices.SetActive(true);
            confirmationChoices.SetActive(false);
            confirmationText.text = SceneManager.GetActiveScene().name == "Game" ? "対戦メニュー\n操作時間は進みます" : "終了メニュー";
            Refresh();
        }

        public void CloseMenu()
        {
            confirmation = null;
            surrenderPending = false;
            menuPanel.SetActive(false);
            MatchInteractionController.Instance?.SetModalBlocked(false);
        }

        public void HandleEscape()
        {
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
            OpenMenu();
            confirmation = action;
            confirmationText.text = text + (IsActiveMatch() ? "\n操作時間は進みます" : "");
            menuChoices.SetActive(false);
            confirmationChoices.SetActive(true);
        }

        private bool IsActiveMatch()
        {
            return view?.Public != null && !view.CommunicationInterrupted && view.Public.matchPhase == (int)MatchPhase.Active;
        }

        public void AcceptConfirmation()
        {
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
                confirmationText.text = "操作の応答を待っています\n操作時間は進みます";
                confirmationChoices.SetActive(false);
            }
            else
            {
                string destination = confirmation;
                CloseMenu();
                OnlineSessionController.Ensure().RequestLeave(destination);
            }
        }

        public void CancelPreparation()
        {
            session?.CancelPreparation();
        }

        public void Rematch()
        {
            session?.RequestRematch(!session.LocalRematch);
        }

        public void ReturnMode()
        {
            if (IsActiveMatch())
            {
                ConfirmLeave();
            }
            else
            {
                OnlineSessionController.Ensure().RequestLeave("StartGame");
            }
        }

        public void ReturnTitle()
        {
            if (IsActiveMatch())
            {
                Ask("Title", "降参してタイトルへ戻りますか？");
            }
            else
            {
                OnlineSessionController.Ensure().RequestLeave("Title");
            }
        }

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
            bool active = IsActiveMatch();
            if (preparationText != null)
            {
                string order = state == null ? "通信と盤面を準備しています" :
                    (state.currentPlayer == view.LocalPlayer ? "自分が先手 ／ 相手が後手" : "相手が先手 ／ 自分が後手");
                preparationText.text = order + "\n" + (state == null ? "" : "毎手番：" + MatchSetupPresenter.TimeLabel(state.turnSeconds)) +
                    "\n" + (state != null && state.initialCardsDealt ? "初期手札を配りました。双方の準備を待っています" : "双方の準備完了後に手札を配ります");
                preparationCancel.interactable = session != null && !session.IsWorking;
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
