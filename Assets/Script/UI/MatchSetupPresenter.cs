using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Waribasi.MatchFlow;
using Waribasi.Networking;

namespace Waribasi.Presentation
{
    public enum SetupPage
    {
        Mode,
        Room,
        Host,
        Join,
        Random,
        Npc,
        Waiting
    }

    public sealed class MatchSetupPresenter : MonoBehaviour
    {
        public static MatchSetupPresenter Instance { get; private set; }
        [SerializeField] private GameObject[] pages;
        [SerializeField] private Button[] firstButtons;
        [SerializeField] private Button[] timeButtons;
        [SerializeField] private Button[] difficultyButtons;
        [SerializeField] private Button[] npcFirstButtons;
        [SerializeField] private TMP_InputField joinInput;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text codeText;
        [SerializeField] private TMP_Text settingsText;
        [SerializeField] private TMP_Text npcMessage;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button cancelButton;
        private OnlineSessionController session;
        private int first;
        private int timeIndex = 1;
        private int difficulty;
        private int npcFirst;
        private int lastStart;
        private bool cancelPending;
        public SetupPage Page { get; private set; }
        public bool SettingsVisible => Page == SetupPage.Waiting && isActiveAndEnabled && pages[(int)SetupPage.Waiting].activeInHierarchy;
        public MatchSetup SelectedOnlineSetup => new MatchSetup(MatchMode.Room, (FirstTurnSetting)first, MatchSetup.TimeOptions[timeIndex]);

        private void Awake()
        {
            Instance = this;
            session = OnlineSessionController.Ensure();
            session.Changed += Refresh;
            BindOptions(firstButtons, value => first = value);
            BindOptions(timeButtons, value => timeIndex = value);
            BindOptions(difficultyButtons, value => difficulty = value);
            BindOptions(npcFirstButtons, value => npcFirst = value);
            if (NpcMatchEntry.LastSetup != null)
            {
                difficulty = (int)NpcMatchEntry.LastSetup.Difficulty;
                npcFirst = (int)NpcMatchEntry.LastSetup.First;
            }
            SetPage(NpcMatchEntry.ReturnToPreparation ? SetupPage.Npc : SetupPage.Mode);
            NpcMatchEntry.ReturnToPreparation = false;
        }

        private void BindOptions(Button[] buttons, Action<int> select)
        {
            for (int index = 0; index < buttons.Length; index++)
            {
                int value = index;
                buttons[index].onClick.AddListener(() =>
                {
                    if (!session.IsWorking && !session.HasSession)
                    {
                        select(value);
                        Refresh();
                    }
                });
            }
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.Changed -= Refresh;
            }
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void SetPage(SetupPage page)
        {
            Page = page;
            for (int index = 0; index < pages.Length; index++)
            {
                pages[index].SetActive(index == (int)page);
            }
            Refresh();
        }

        public void ShowMode() => SetPage(SetupPage.Mode);
        public void ShowRoom() => SetPage(SetupPage.Room);
        public void ShowHost() => SetPage(SetupPage.Host);
        public void ShowJoin() => SetPage(SetupPage.Join);
        public void ShowRandom() => SetPage(SetupPage.Random);
        public void ShowNpc() => SetPage(SetupPage.Npc);

        public void StartHost()
        {
            if (session.IsWorking || session.HasSession)
            {
                return;
            }
            lastStart = 0;
            SetPage(SetupPage.Waiting);
            session.StartRoomHost(SelectedOnlineSetup);
        }

        public void StartClient()
        {
            if (session.IsWorking || session.HasSession)
            {
                return;
            }
            lastStart = 1;
            SetPage(SetupPage.Waiting);
            session.StartRoomClient(joinInput.text);
        }

        public void StartRandom()
        {
            if (session.IsWorking || session.HasSession)
            {
                return;
            }
            lastStart = 2;
            SetPage(SetupPage.Waiting);
            session.StartRandom();
        }

        public void Retry()
        {
            if (lastStart == 0)
            {
                StartHost();
            }
            else if (lastStart == 1)
            {
                StartClient();
            }
            else
            {
                StartRandom();
            }
        }

        public void StartNpc()
        {
            var result = NpcMatchEntry.TryStart(new NpcMatchSetup((NpcDifficulty)difficulty, (NpcFirstPlayerChoice)npcFirst));
            npcMessage.text = result == NpcStartResult.Accepted ? "対戦を準備しています" :
                result == NpcStartResult.UnsupportedDifficulty ? "この難易度は準備中です" :
                result == NpcStartResult.Busy ? "対戦を準備しています" : "NPC対戦は準備中です";
        }

        public async void CancelWaiting()
        {
            if (cancelPending)
            {
                return;
            }
            cancelPending = true;
            Refresh();
            await session.CancelOnlineAsync();
            if (this != null)
            {
                cancelPending = false;
                SetPage(SetupPage.Mode);
            }
        }

        public void Back()
        {
            if (Page == SetupPage.Waiting)
            {
                CancelWaiting();
            }
            else if (Page == SetupPage.Host || Page == SetupPage.Join)
            {
                ShowRoom();
            }
            else if (Page == SetupPage.Mode)
            {
                ReturnTitle();
            }
            else
            {
                ShowMode();
            }
        }

        public void ReturnTitle()
        {
            session.RequestLeave("Title");
        }

        public static string SettingsLabel(MatchSetup setup)
        {
            if (setup == null)
            {
                return "相手の設定を待っています";
            }
            string order = setup.First == FirstTurnSetting.Random ? "ランダム" : setup.First == FirstTurnSetting.PlayerA ? "ホスト先手" : "ゲスト先手";
            return "先後手：" + order + "　／　毎手番：" + TimeLabel(setup.TurnSeconds);
        }

        public static string TimeLabel(int seconds)
        {
            return seconds == 0 ? "制限なし" : seconds < 60 ? seconds + "秒" : (seconds / 60) + "分";
        }

        private void Refresh()
        {
            if (session == null)
            {
                return;
            }
            statusText.text = session.Status;
            codeText.text = session.Settings?.Mode == MatchMode.Room && !string.IsNullOrEmpty(session.RoomCode) ? "合言葉：" + session.RoomCode : "";
            settingsText.text = SettingsLabel(session.Settings ?? (lastStart == 0 ? SelectedOnlineSetup : lastStart == 2 ? new MatchSetup(MatchMode.Random) : null));
            retryButton.gameObject.SetActive(!session.IsWorking && !session.HasSession);
            cancelButton.interactable = !cancelPending && !session.Loading;
            Highlight(firstButtons, first);
            Highlight(timeButtons, timeIndex);
            Highlight(difficultyButtons, difficulty);
            Highlight(npcFirstButtons, npcFirst);
            //追加:実装済みの難易度と、T07へ渡す上級の準備状況を表示する。
            if (Page == SetupPage.Npc)
            {
                npcMessage.text = difficulty == (int)NpcDifficulty.Advanced ? "上級は準備中です" : "ネット接続なしで遊べます";
            }
        }

        private static void Highlight(Button[] buttons, int selected)
        {
            for (int index = 0; index < buttons.Length; index++)
            {
                buttons[index].GetComponent<Image>().color = index == selected ? new Color(0.42f, 0.76f, 1) : new Color(0.83f, 0.92f, 1);
            }
        }
    }
}
