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
    public sealed class MatchHudBindingTests
    {
        private GameObject root;
        private Component view;
        private Component interaction;
        private Component hud;
        private PublicMatchMessage state;
        private TMP_Text message;
        private TMP_Text fingers;
        private TMP_Text timer;
        private GameObject[] backs;

        private static Type RuntimeType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp").GetType(name, true);
        }

        private static object Call(object target, string method, params object[] args)
        {
            return target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args);
        }

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(root.transform, false);
            return child;
        }

        private TMP_Text Text(string name)
        {
            return Child(name).AddComponent<TextMeshProUGUI>();
        }

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("HudBinding");
            root.SetActive(false);
            view = Child("State").AddComponent(RuntimeType("Waribasi.Networking.MatchClientState"));
            interaction = Child("Interaction").AddComponent(RuntimeType("Waribasi.Presentation.MatchInteractionController"));
            hud = Child("Hud").AddComponent(RuntimeType("Waribasi.Presentation.MatchHudPresenter"));
            var authority = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1),
                PlayerId.PlayerA), 30, 100);
            state = MatchMessages.Public(authority, 0, 7, 3, 100);
            Call(view, "ReceivePublic", state, 0UL);
            Call(view, "ReceiveOwn", MatchMessages.OwnHand(authority, PlayerId.PlayerA));
            Call(interaction, "Bind", view);
            Set(hud, "interaction", interaction);
            Set(hud, "turnText", Text("Turn"));
            timer = Text("Timer");
            fingers = Text("Fingers");
            message = Text("Notification");
            Set(hud, "timerText", timer);
            Set(hud, "fingersText", fingers);
            Set(hud, "messageText", message);
            Set(hud, "historyPanel", Child("History"));
            Set(hud, "historyText", Text("HistoryText"));
            var history = Child("HistoryButton").AddComponent<Button>();
            Text("HistoryLabel").transform.SetParent(history.transform, false);
            Set(hud, "historyButton", history);
            Set(hud, "historyCloseButton", Child("CloseHistory").AddComponent<Button>());
            backs = Enumerable.Range(0, 5).Select(i => Child("Back" + i)).ToArray();
            Set(hud, "opponentCardBacks", backs);
            //変更:可視状態の条件も検証するため、参照を設定してから有効にする。
            root.AddComponent<Canvas>();
            message.color = Color.white;
            root.SetActive(true);
            if (!ReferenceEquals(hud.GetType().GetProperty("Instance").GetValue(null), hud))
            {
                Call(hud, "Awake");
                //追加:代替環境でも有効化時の購読を再現する。
                Call(hud, "OnEnable");
            }
        }

        [TearDown]
        public void TearDown()
        {
            //追加:代替環境でもCanvasと入力通知の購読を先に解除する。
            Call(hud, "OnDisable");
            Call(hud, "OnDestroy");
            Call(interaction, "Bind", (object)null);
            Call(interaction, "OnDestroy");
            Call(view, "OnDestroy");
            UnityEngine.Object.DestroyImmediate(root);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void PublicCountsDriveCenteredBackSlotsWithoutAnOpponentHand(int count)
        {
            state.playerBCardCount = count;
            Call(hud, "Refresh");
            Assert.That(backs.Count(card => card.activeSelf), Is.EqualTo(count));
            Assert.That(backs.Take(count).Sum(card => ((RectTransform)card.transform).anchoredPosition.x), Is.Zero);
            Assert.That(message.gameObject.activeSelf, Is.False, "Idle instructions must not reserve a HUD row.");
        }

        [Test]
        public void RejectionsRemainVisibleThenTheirWholeRegionDisappears()
        {
            Call(interaction, "OnResponse", new RequestResponseMessage { accepted = false, ruleRejection = (int)ActionRejection.CardUnchanged });
            Assert.That(message.gameObject.activeSelf, Is.True);
            Assert.That(message.text, Does.Contain("カード未消費"));
            //変更:未描画を表す-1ではなく、描画後に期限切れとなった時刻を使う。
            //削除:Set(hud, "notificationUntil", -1f);
            Set(hud, "notificationUntil", Time.realtimeSinceStartupAsDouble - 1d);
            Call(hud, "Update");
            Assert.That(message.gameObject.activeSelf, Is.False);
            Assert.That(message.text, Is.Empty);
        }

        [Test]
        public void SkipPresentationWinsOverAFailureAndEndsWhenInputReopens()
        {
            Call(interaction, "OnResponse", new RequestResponseMessage { accepted = false });
            state.stateVersion++;
            state.gate = (int)InputGate.Presenting;
            state.events = new[] { new RuleEventMessage { type = (int)RuleEventType.TurnSkipped, player = 1 } };
            Call(hud, "Refresh");
            Assert.That(message.text, Is.EqualTo("相手：スキップ！"));
            Assert.That(timer.text, Is.EqualTo("反映中"));
            state.gate = (int)InputGate.Accepting;
            Call(hud, "Refresh");
            Assert.That(message.gameObject.activeSelf, Is.False);
        }

        //追加:次の状態・スキップでカード本文を失わず、描画・寿命・非表示を確認する。
        [Test]
        public void CardNotificationSurvivesNextActionAndSkipUntilItsRenderedDeadline()
        {
            state.stateVersion++;
            state.events = new[]
            {
                new RuleEventMessage { type = (int)RuleEventType.CardUsed,
                    cardUse = new CardUseMessage { player = 1, kind = (int)CardKind.DrawTwo, turnNumber = 1 } }
            };
            Call(hud, "Refresh");
            string card = message.text;
            var deadline = hud.GetType().GetField("notificationUntil", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(deadline.GetValue(hud), Is.EqualTo(-1d));
            Call(hud, "ObserveReadableDisplay");
            double expires = (double)deadline.GetValue(hud);
            Assert.That(expires, Is.GreaterThan(Time.realtimeSinceStartupAsDouble));
            state.stateVersion++;
            state.gate = (int)InputGate.Presenting;
            state.events = new[] { new RuleEventMessage { type = (int)RuleEventType.TurnSkipped, player = 1 } };
            Call(hud, "Refresh");
            Assert.That(message.text, Does.Contain(card));
            Assert.That(message.text, Does.Contain("スキップ！"));
            Assert.That(deadline.GetValue(hud), Is.EqualTo(expires));
            state.gate = (int)InputGate.Accepting;
            Call(hud, "Refresh");
            Assert.That(message.text, Is.EqualTo(card));
            Call(hud, "SetTransitionObscured", true);
            Call(hud, "ObserveReadableDisplay");
            Assert.That(deadline.GetValue(hud), Is.EqualTo(-1d));
            Call(hud, "SetTransitionObscured", false);
            Call(hud, "ObserveReadableDisplay");
            Assert.That(deadline.GetValue(hud), Is.GreaterThan(Time.realtimeSinceStartupAsDouble));
            Call(hud, "OnDisable");
            Assert.That(message.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void SelectionRefreshDoesNotRestartTheClockBetweenPublicSnapshots()
        {
            //変更:HUD内部ではなく、受信状態の時計標本から取消後の残秒も計算する。
            //削除:Set(hud, "clockReceivedAt", Time.unscaledTimeAsDouble - 5);
            Set(view, "<PublicReceivedAt>k__BackingField", Time.realtimeSinceStartupAsDouble - 5.25);
            Call(interaction, "OpenRedistribution");
            Assert.That(timer.text, Is.EqualTo("25秒"));
            Call(interaction, "CancelSelection");
            Assert.That(timer.text, Is.EqualTo("25秒"));
            Assert.That(state.deadline, Is.EqualTo(130));
        }

        [Test]
        public void ResetHidesOldNotificationsBacksAndClock()
        {
            state.playerBCardCount = 5;
            Call(hud, "Refresh");
            Call(interaction, "OnResponse", new RequestResponseMessage { accepted = false });
            Call(view, "ResetForNewMatch");
            Assert.That(message.gameObject.activeSelf, Is.False);
            Assert.That(timer.gameObject.activeSelf, Is.False);
            Assert.That(backs.All(card => !card.activeSelf), Is.True);
        }

        //追加:遅れてHUDを描いても、実受信後の経過を捨てたり再描画で満額に戻したりしない。
        [Test]
        public void LateHudBindingUsesReceiptTimeAndLocalControlsNeverResetIt()
        {
            double receivedAt = Time.realtimeSinceStartupAsDouble - 10.25;
            Set(view, "<PublicReceivedAt>k__BackingField", receivedAt);
            Call(hud, "Refresh");
            Assert.That(timer.text, Is.EqualTo("20秒"));
            Call(interaction, "SetModalBlocked", true);
            Call(interaction, "SetModalBlocked", false);
            Call(interaction, "SetHistoryOpen", true);
            Call(interaction, "SetHistoryOpen", false);
            Call(interaction, "CancelSelection");
            Call(hud, "Refresh");
            Assert.That(view.GetType().GetProperty("PublicReceivedAt").GetValue(view), Is.EqualTo(receivedAt));
            Assert.That(state.deadline, Is.EqualTo(130));
            Assert.That(timer.text, Is.EqualTo("20秒"));
        }

        //追加:旧配信や本人札では受信基点を変えず、再戦の初期化だけが標本を破棄する。
        [Test]
        public void RejectedPublicAndPrivateUpdatesPreserveReceiptSample()
        {
            Set(view, "<PublicReceivedAt>k__BackingField", -123d);
            Assert.That(Call(view, "ReceivePublic", state, 0UL), Is.False);
            Call(view, "ReceiveHistory", new HistoryPageMessage { matchId = state.matchId, offset = 0, records = Array.Empty<CardUseMessage>() });
            Assert.That(view.GetType().GetProperty("PublicReceivedAt").GetValue(view), Is.EqualTo(-123d));
            var next = MatchMessages.Public(new MatchAuthority(Guid.ParseExact(state.matchId, "N"),
                RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerA), 30, 100), 0, 7, 3, 100);
            next.sequence = state.sequence + 1;
            bool observed = false;
            Action changed = () => observed = (double)view.GetType().GetProperty("PublicReceivedAt").GetValue(view) != -123;
            var changedEvent = view.GetType().GetEvent("Changed");
            changedEvent.AddEventHandler(view, changed);
            try
            {
                Assert.That(Call(view, "ReceivePublic", next, 0UL), Is.True);
                Assert.That(observed, Is.True);
                Call(view, "ResetForNewMatch");
                Assert.That(view.GetType().GetProperty("PublicReceivedAt").GetValue(view), Is.EqualTo(0d));
            }
            finally
            {
                changedEvent.RemoveEventHandler(view, changed);
            }
        }

        //追加:Scene破棄中に履歴UIが先に失われても、入力遮断解除でHUDを更新しない。
        [TestCase("historyButton")]
        [TestCase("historyText")]
        [TestCase("messageText")]
        public void MissingHudChildCannotBeAccessedByModalRelease(string field)
        {
            Set(hud, field, null);
            Assert.DoesNotThrow(() => Call(interaction, "SetModalBlocked", false));
        }

        //追加:無効化で通知を切り、再有効化を繰り返しても購読を一つだけ戻す。
        [Test]
        public void DisabledHudUnsubscribesAndReenabledHudSubscribesOnce()
        {
            Call(hud, "OnDisable");
            foreach (string name in new[] { "Changed", "Notification" })
            {
                var handler = (Delegate)interaction.GetType().GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(interaction);
                Assert.That(handler == null || handler.GetInvocationList().All(item => !ReferenceEquals(item.Target, hud)), Is.True);
            }
            Call(hud, "OnEnable");
            Call(hud, "OnEnable");
            foreach (string name in new[] { "Changed", "Notification" })
            {
                var handler = (Delegate)interaction.GetType().GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(interaction);
                Assert.That(handler.GetInvocationList().Count(item => ReferenceEquals(item.Target, hud)), Is.EqualTo(1));
            }
            Call(interaction, "OnResponse", new RequestResponseMessage { accepted = false, ruleRejection = (int)ActionRejection.CardUnchanged });
            Assert.That(message.text, Does.Contain("カード未消費"));
        }

        [Test]
        public void FingerVisibilityUsesTheCompilationCondition()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(fingers.gameObject.activeSelf, Is.True);
#else
            Assert.That(fingers.gameObject.activeSelf, Is.False);
#endif
        }
    }
}
