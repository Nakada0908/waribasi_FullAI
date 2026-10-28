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
        //追加:解放前の行の範囲を保持し、実行中の解放でも同じ4ボタン配置へ揃える。
        private float difficultyStart;
        private float difficultyEnd;
        private float difficultyY;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        //追加:通常リリースでは開発用UIの参照も作らない。
        private Button developmentUnlockButton;
#endif
        public SetupPage Page { get; private set; }
        public bool SettingsVisible => Page == SetupPage.Waiting && isActiveAndEnabled && pages[(int)SetupPage.Waiting].activeInHierarchy;
        public MatchSetup SelectedOnlineSetup => new MatchSetup(MatchMode.Room, (FirstTurnSetting)first, MatchSetup.TimeOptions[timeIndex]);

        private void Awake()
        {
            Instance = this;
            session = OnlineSessionController.Ensure();
            session.Changed += Refresh;
            //追加:既存の難易度ボタンを複製し、4Sceneや素材を増やさず最強を追加する。
            AddStrongestOption();
            //追加:EditorとDevelopment BuildのNPC設定だけに強制解放操作を追加する。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AddDevelopmentUnlockOption();
#endif
            BindOptions(firstButtons, value => first = value);
            BindOptions(timeButtons, value => timeIndex = value);
            BindOptions(difficultyButtons, value => difficulty = value);
            BindOptions(npcFirstButtons, value => npcFirst = value);
            //変更:前回設定の復元を独立させ、解放済みの神も従来の値で復元する。
            RestoreNpcSetup();
            SetPage(NpcMatchEntry.ReturnToPreparation ? SetupPage.Npc : SetupPage.Mode);
            NpcMatchEntry.ReturnToPreparation = false;
        }

        //追加:未解放時だけ上級へ戻し、解放済みなら難易度と先後手を保持する。
        private void RestoreNpcSetup()
        {
            if (NpcMatchEntry.LastSetup != null)
            {
                //変更:保存領域を消した後に前回の最強選択を復元しない。
                difficulty = NpcMatchEntry.LastSetup.Difficulty == NpcDifficulty.Strongest && !NpcMatchEntry.IsStrongestUnlocked
                    ? (int)NpcDifficulty.Advanced : (int)NpcMatchEntry.LastSetup.Difficulty;
                npcFirst = (int)NpcMatchEntry.LastSetup.First;
            }
        }

        //追加:ロック中は既存3ボタンの配置を維持し、解放後だけ同じ行へ4つ並べる。
        private void AddStrongestOption()
        {
            var original = difficultyButtons;
            var strongest = Instantiate(original[2], original[2].transform.parent);
            strongest.name = "Difficulty3";
            //変更:HUDと同じ名称を使い、内部のStrongest値を維持する。
            //削除:strongest.GetComponentInChildren<TMP_Text>(true).text = "最強";
            strongest.GetComponentInChildren<TMP_Text>(true).text = NpcMatchEntry.DifficultyLabel(NpcDifficulty.Strongest);
            difficultyButtons = new[] { original[0], original[1], original[2], strongest };
            //変更:再配置前の範囲を一度だけ取得し、実行中の解放に共用する。
            var left = (RectTransform)original[0].transform;
            var right = (RectTransform)original[2].transform;
            difficultyStart = left.anchoredPosition.x - left.rect.width / 2;
            difficultyEnd = right.anchoredPosition.x + right.rect.width / 2;
            difficultyY = left.anchoredPosition.y;
            RefreshStrongestOption();
        }

        //追加:保存済み解放と開発用解放の両方で、神の表示と配置を反映する。
        private void RefreshStrongestOption()
        {
            difficultyButtons[(int)NpcDifficulty.Strongest].gameObject.SetActive(NpcMatchEntry.IsStrongestUnlocked);
            if (NpcMatchEntry.IsStrongestUnlocked)
            {
                //変更:元の3ボタンの範囲を使い、繰り返し更新で幅を縮めない。
                float cell = (difficultyEnd - difficultyStart) / 4;
                for (int index = 0; index < difficultyButtons.Length; index++)
                {
                    var rect = (RectTransform)difficultyButtons[index].transform;
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, cell - 10);
                    rect.anchoredPosition = new Vector2(difficultyStart + cell * (index + 0.5f), difficultyY);
                }
            }
        }

        //追加:既存の入力対応ボタンを使い、設定画面の戻るボタンの右へ配置する。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void AddDevelopmentUnlockOption()
        {
            developmentUnlockButton = Instantiate(difficultyButtons[0], difficultyButtons[0].transform.parent);
            developmentUnlockButton.name = "DevelopmentUnlockStrongest";
            developmentUnlockButton.onClick = new Button.ButtonClickedEvent();
            developmentUnlockButton.onClick.AddListener(UnlockStrongestForDevelopment);
            developmentUnlockButton.GetComponentInChildren<TMP_Text>(true).text = "開発用：神を解放";
            var rect = (RectTransform)developmentUnlockButton.transform;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 400);
            rect.anchoredPosition = new Vector2(400, -280);
        }

        private void UnlockStrongestForDevelopment()
        {
            var local = LocalMatchController.Instance;
            if (Page != SetupPage.Npc || session == null || session.IsWorking || session.HasSession ||
                (local != null && (local.Active || local.IsWorking)) || NpcMatchEntry.Progress == null)
            {
                return;
            }
            NpcMatchEntry.Progress.UnlockForDevelopment();
            difficulty = (int)NpcDifficulty.Strongest;
            RefreshStrongestOption();
            Refresh();
        }
#endif

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
                //追加:ロック中の開始拒否を表示する。
                result == NpcStartResult.Locked ? "上級に勝利すると解放されます" :
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
            //追加:解放後は開発用操作を隠し、通常の難易度選択へ戻す。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (developmentUnlockButton != null)
            {
                developmentUnlockButton.gameObject.SetActive(!NpcMatchEntry.IsStrongestUnlocked);
                var local = LocalMatchController.Instance;
                developmentUnlockButton.interactable = !session.IsWorking && !session.HasSession &&
                    (local == null || (!local.Active && !local.IsWorking));
            }
#endif
            //追加:実装済みの難易度と、T07へ渡す上級の準備状況を表示する。
            //変更:通常3難易度のオフライン開始を表示する。
            if (Page == SetupPage.Npc)
            {
                //削除:上級未実装時の表示を保存する。
                //npcMessage.text = difficulty == (int)NpcDifficulty.Advanced ? "上級は準備中です" : "ネット接続なしで遊べます";
                npcMessage.text = "ネット接続なしで遊べます";
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
