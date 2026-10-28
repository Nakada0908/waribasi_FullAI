using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Presentation.Tests
{
    public sealed class NpcGodUiTests
    {
        private sealed class Store : INpcUnlockStore
        {
            public bool Value;
            public int Writes;
            public bool ReadUnlocked() => Value;
            public void WriteUnlocked()
            {
                Value = true;
                Writes++;
            }
        }

        private GameObject root;
        private Component presenter;
        private NpcUnlockProgress previousProgress;
        private NpcMatchSetup previousSetup;
        private Store store;

        private static Type RuntimeType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp")
                .GetType(name, true);
        }

        private static object Call(object target, string method, params object[] args)
        {
            return target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(target, args);
        }

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static T Get<T>(object target, string field)
        {
            return (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private TMP_Text Text(string name)
        {
            var owner = new GameObject(name, typeof(RectTransform));
            owner.transform.SetParent(root.transform, false);
            return owner.AddComponent<TextMeshProUGUI>();
        }

        [SetUp]
        public void Prepare()
        {
            previousProgress = NpcMatchEntry.Progress;
            previousSetup = NpcMatchEntry.LastSetup;
            store = new Store();
            NpcMatchEntry.ConfigureProgress(new NpcUnlockProgress(store));
            root = new GameObject("GodSetupTest", typeof(RectTransform));
            root.SetActive(false);
            presenter = root.AddComponent(RuntimeType("Waribasi.Presentation.MatchSetupPresenter"));
            var session = root.AddComponent(RuntimeType("Waribasi.Networking.OnlineSessionController"));
            Set(presenter, "session", session);
            var buttons = new Button[3];
            for (int index = 0; index < buttons.Length; index++)
            {
                var owner = new GameObject("Difficulty" + index, typeof(RectTransform), typeof(Image), typeof(Button));
                owner.transform.SetParent(root.transform, false);
                var rect = (RectTransform)owner.transform;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 250);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 56);
                rect.anchoredPosition = new Vector2(-266 + 266 * index, 130);
                var label = Text("Label");
                label.transform.SetParent(owner.transform, false);
                buttons[index] = owner.GetComponent<Button>();
            }
            Set(presenter, "difficultyButtons", buttons);
            Set(presenter, "firstButtons", Array.Empty<Button>());
            Set(presenter, "timeButtons", Array.Empty<Button>());
            Set(presenter, "npcFirstButtons", Array.Empty<Button>());
            foreach (string field in new[] { "statusText", "codeText", "settingsText", "npcMessage" })
            {
                Set(presenter, field, Text(field));
            }
            Set(presenter, "retryButton", buttons[0]);
            Set(presenter, "cancelButton", buttons[1]);
            Call(presenter, "AddStrongestOption");
        }

        [TearDown]
        public void Cleanup()
        {
            UnityEngine.Object.DestroyImmediate(root);
            NpcMatchEntry.ConfigureProgress(previousProgress ?? new NpcUnlockProgress(new Store()));
            typeof(NpcMatchEntry).GetProperty("LastSetup").SetValue(null, previousSetup);
        }

        [Test]
        public void LockedGodIsHiddenAndTheOriginalThreeButtonsKeepTheirLayout()
        {
            var buttons = Get<Button[]>(presenter, "difficultyButtons");
            Assert.That(buttons.Length, Is.EqualTo(4));
            Assert.That(buttons[3].gameObject.activeSelf, Is.False);
            Assert.That(buttons[3].GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("神"));
            for (int index = 0; index < 3; index++)
            {
                var rect = (RectTransform)buttons[index].transform;
                Assert.That(rect.rect.width, Is.EqualTo(250));
                Assert.That(rect.anchoredPosition.x, Is.EqualTo(-266 + 266 * index));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PreviousGodSettingRestoresOnlyWhenUnlocked(bool unlocked)
        {
            store.Value = unlocked;
            NpcMatchEntry.ConfigureProgress(new NpcUnlockProgress(store));
            typeof(NpcMatchEntry).GetProperty("LastSetup").SetValue(null,
                new NpcMatchSetup(NpcDifficulty.Strongest, NpcFirstPlayerChoice.Npc));
            Call(presenter, "RestoreNpcSetup");
            Assert.That(Get<int>(presenter, "difficulty"), Is.EqualTo((int)(unlocked ? NpcDifficulty.Strongest : NpcDifficulty.Advanced)));
            Assert.That(Get<int>(presenter, "npcFirst"), Is.EqualTo((int)NpcFirstPlayerChoice.Npc));
        }

        [Test]
        public void GodHudUsesTheSameNameDuringTheMatchAndResult()
        {
            var display = RuntimeType("Waribasi.Presentation.MatchHudDisplay");
            var state = new PublicMatchMessage { matchPhase = (int)MatchPhase.Active, currentPlayer = 0 };
            object Turn() => display.GetMethod("Turn").Invoke(null, new object[]
            {
                state, 0, false, NpcMatchEntry.DifficultyLabel(NpcDifficulty.Strongest)
            });
            Assert.That(Turn(), Is.EqualTo("神 ／ 自分の手番"));
            state.matchPhase = (int)MatchPhase.Finished;
            state.winner = 0;
            Assert.That(Turn(), Is.EqualTo("神 ／ 勝利"));
        }

        [Test]
        public void DevelopmentControlUsesTheCompilationCondition()
        {
            var method = presenter.GetType().GetMethod("UnlockStrongestForDevelopment", BindingFlags.Instance | BindingFlags.NonPublic);
            var add = presenter.GetType().GetMethod("AddDevelopmentUnlockOption", BindingFlags.Instance | BindingFlags.NonPublic);
            var field = presenter.GetType().GetField("developmentUnlockButton", BindingFlags.Instance | BindingFlags.NonPublic);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(method, Is.Not.Null);
            Assert.That(add, Is.Not.Null);
            Assert.That(field, Is.Not.Null);
#else
            Assert.That(method, Is.Null);
            Assert.That(add, Is.Null);
            Assert.That(field, Is.Null);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        //追加:実際の起動中進捗へボタンを接続し、画面復帰と次回起動の表示を確認する。
        [TestCase(false)]
        [TestCase(true)]
        public void RuntimeButtonReturnAndNextStartupPreserveTheReleaseSave(bool savedUnlock)
        {
            var storeType = RuntimeType("Waribasi.Presentation.PlayerPrefsNpcUnlockStore");
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var reset = storeType.GetMethod("BeginDevelopmentRun", flags);
            var initialize = storeType.GetMethod("InitializeProgress", flags);
            var create = storeType.GetMethod("CreateRuntimeProgress");
            var field = storeType.GetField("developmentProgress", flags);
            object oldDevelopmentProgress = field.GetValue(null);
            var handler = typeof(NpcMatchEntry).GetField("handler", flags);
            object oldHandler = handler.GetValue(null);
            string key = "waribasi_FullAI.tests.t17.ui." + Guid.NewGuid().ToString("N");
            string sentinel = key + ".unrelated";
            try
            {
                PlayerPrefs.SetInt(key, savedUnlock ? 1 : 0);
                PlayerPrefs.SetInt(sentinel, 91);
                reset.Invoke(null, null);
                var runtime = (NpcUnlockProgress)create.Invoke(null, new object[] { key });
                NpcMatchEntry.ConfigureProgress(runtime);
                Call(presenter, "AddDevelopmentUnlockOption");
                presenter.GetType().GetProperty("Page").SetValue(presenter,
                    Enum.Parse(RuntimeType("Waribasi.Presentation.SetupPage"), "Npc"));
                Set(presenter, "difficulty", (int)NpcDifficulty.Strongest);
                Call(presenter, "Refresh");
                var button = Get<Button>(presenter, "developmentUnlockButton");
                var strongest = Get<Button[]>(presenter, "difficultyButtons")[3];
                Assert.That(button.gameObject.activeSelf, Is.True);
                Assert.That(strongest.gameObject.activeSelf, Is.False);
                Assert.That(Get<int>(presenter, "difficulty"), Is.EqualTo((int)NpcDifficulty.Advanced));
                button.onClick.Invoke();
                Assert.That(runtime.IsStrongestUnlocked, Is.True);
                Assert.That(button.gameObject.activeSelf, Is.False);
                Assert.That(strongest.gameObject.activeSelf, Is.True);
                Assert.That(Get<int>(presenter, "difficulty"), Is.EqualTo((int)NpcDifficulty.Strongest));
                NpcMatchEntry.Register(_ => NpcStartResult.Accepted);
                Assert.That(NpcMatchEntry.TryStart(new NpcMatchSetup(NpcDifficulty.Strongest, NpcFirstPlayerChoice.Npc)),
                    Is.EqualTo(NpcStartResult.Accepted));
                initialize.Invoke(null, null);
                NpcMatchEntry.ConfigureProgress((NpcUnlockProgress)create.Invoke(null, new object[] { key }));
                Call(presenter, "RestoreNpcSetup");
                Call(presenter, "Refresh");
                Assert.That(NpcMatchEntry.Progress, Is.SameAs(runtime));
                Assert.That(Get<int>(presenter, "difficulty"), Is.EqualTo((int)NpcDifficulty.Strongest));
                Assert.That(Get<int>(presenter, "npcFirst"), Is.EqualTo((int)NpcFirstPlayerChoice.Npc));
                Assert.That(button.gameObject.activeSelf, Is.False);
                Assert.That(strongest.gameObject.activeSelf, Is.True);
                reset.Invoke(null, null);
                initialize.Invoke(null, null);
                Call(presenter, "Refresh");
                Assert.That(button.gameObject.activeSelf, Is.True);
                Assert.That(strongest.gameObject.activeSelf, Is.False);
                Assert.That(Get<int>(presenter, "difficulty"), Is.EqualTo((int)NpcDifficulty.Advanced));
                Assert.That(NpcMatchEntry.LastSetup.Difficulty, Is.EqualTo(NpcDifficulty.Advanced));
                Assert.That(PlayerPrefs.GetInt(key), Is.EqualTo(savedUnlock ? 1 : 0));
                Assert.That(PlayerPrefs.GetInt(sentinel), Is.EqualTo(91));
            }
            finally
            {
                handler.SetValue(null, oldHandler);
                field.SetValue(null, oldDevelopmentProgress);
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.DeleteKey(sentinel);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void DevelopmentButtonSavesAndSelectsGodWithoutStartingAMatch()
        {
            Call(presenter, "AddDevelopmentUnlockOption");
            presenter.GetType().GetProperty("Page").SetValue(presenter, Enum.Parse(RuntimeType("Waribasi.Presentation.SetupPage"), "Npc"));
            var button = Get<Button>(presenter, "developmentUnlockButton");
            button.onClick.Invoke();
            Assert.That(store.Writes, Is.EqualTo(1));
            Assert.That(new NpcUnlockProgress(store).IsStrongestUnlocked, Is.True);
            Assert.That(Get<int>(presenter, "difficulty"), Is.EqualTo((int)NpcDifficulty.Strongest));
            Assert.That(button.gameObject.activeSelf, Is.False);
            var buttons = Get<Button[]>(presenter, "difficultyButtons");
            Assert.That(buttons[3].gameObject.activeSelf, Is.True);
            var positions = buttons.Select(item => ((RectTransform)item.transform).anchoredPosition.x).ToArray();
            Call(presenter, "RefreshStrongestOption");
            Assert.That(buttons.Select(item => ((RectTransform)item.transform).anchoredPosition.x), Is.EqualTo(positions));
            Assert.That(buttons.Select(item => ((RectTransform)item.transform).rect.width).Distinct().Count(), Is.EqualTo(1));
            button.onClick.Invoke();
            Assert.That(store.Writes, Is.EqualTo(1));
            Assert.That(NpcMatchEntry.LastSetup, Is.SameAs(previousSetup));
        }

        [Test]
        public void DevelopmentControlCannotUnlockOutsideTheNpcPage()
        {
            Call(presenter, "UnlockStrongestForDevelopment");
            Assert.That(store.Writes, Is.Zero);
            Assert.That(NpcMatchEntry.IsStrongestUnlocked, Is.False);
        }

        [Test]
        public void DevelopmentControlCannotUnlockWhileOnlineConnectionIsWorking()
        {
            presenter.GetType().GetProperty("Page").SetValue(presenter, Enum.Parse(RuntimeType("Waribasi.Presentation.SetupPage"), "Npc"));
            Set(Get<object>(presenter, "session"), "connecting", true);
            Call(presenter, "UnlockStrongestForDevelopment");
            Assert.That(store.Writes, Is.Zero);
            Assert.That(NpcMatchEntry.IsStrongestUnlocked, Is.False);
        }
#endif
    }
}
