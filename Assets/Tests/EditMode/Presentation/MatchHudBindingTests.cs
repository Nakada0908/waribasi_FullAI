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
            Call(hud, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
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
            Set(hud, "notificationUntil", -1f);
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

        [Test]
        public void SelectionRefreshDoesNotRestartTheClockBetweenPublicSnapshots()
        {
            Set(hud, "clockReceivedAt", Time.unscaledTimeAsDouble - 5);
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
