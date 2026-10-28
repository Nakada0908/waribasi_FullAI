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
    public sealed class MatchDisplayBindingTests
    {
        private GameObject root;
        private GameObject panel;
        private Component presenter;
        private Component view;
        private TMP_Text result;
        private MatchAuthority authority;

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

        private object Field(string name)
        {
            return presenter.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(presenter);
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(root.transform, false);
            return child;
        }

        private TMP_Text Text(string name)
        {
            var text = Child(name).AddComponent<TextMeshProUGUI>();
            text.color = new Color(1, 1, 1, 1);
            return text;
        }

        private bool CanPrepare(PublicMatchMessage message)
        {
            return (bool)presenter.GetType().GetMethod("CanAcknowledgePreparation").Invoke(null, new object[] { message });
        }

        private bool CanFinish()
        {
            return (bool)presenter.GetType().GetMethod("CanLeaveResult").Invoke(null, new object[] { view });
        }

        private void Observe()
        {
            Call(presenter, "ObserveReadableDisplay");
        }

        private PublicMatchMessage Receive(MatchPhase phase, long version = 0, int winner = 0)
        {
            var message = MatchMessages.Public(authority, 0, 7, 1, 100);
            message.matchPhase = (int)phase;
            message.winner = winner;
            message.stateVersion = version;
            message.initialCardsDealt = version > 0;
            message.sequence = version + 1;
            Assert.That(Call(view, "ReceivePublic", message, 0UL), Is.True);
            Set(presenter, "shownMatch", message.matchId);
            Set(presenter, "shownVersion", version);
            Call(presenter, "Refresh");
            return message;
        }

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("ReadableDisplay", typeof(RectTransform));
            root.SetActive(false);
            root.AddComponent<Canvas>();
            view = Child("State").AddComponent(RuntimeType("Waribasi.Networking.MatchClientState"));
            presenter = Child("Flow").AddComponent(RuntimeType("Waribasi.Presentation.MatchFlowPresenter"));
            panel = Child("Preparation");
            Set(presenter, "preparationPanel", panel);
            Set(presenter, "preparationText", Text("PreparationText"));
            Set(presenter, "preparationCancel", Child("Cancel").AddComponent<Button>());
            Set(presenter, "menuPanel", Child("Menu"));
            Set(presenter, "menuChoices", Child("Choices"));
            Set(presenter, "confirmationChoices", Child("Confirmation"));
            Set(presenter, "confirmationText", Text("ConfirmationText"));
            Set(presenter, "surrenderChoice", Child("Surrender").AddComponent<Button>());
            result = Text("Result");
            Set(presenter, "resultText", result);
            root.SetActive(true);
            //管理コードだけの代替環境はUnityのAwakeを実行しないため、必要な場合だけ呼ぶ。
            if (!ReferenceEquals(presenter.GetType().GetProperty("Instance").GetValue(null), presenter))
            {
                Call(presenter, "Awake");
            }
            Set(presenter, "view", view);
            authority = new MatchAuthority(Guid.NewGuid(), PlayerId.PlayerA, 60);
        }

        [TearDown]
        public void TearDown()
        {
            Call(presenter, "OnDisable");
            Call(presenter, "OnDestroy");
            UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public void DefaultsAreSeparateAndMissingPresenterCannotAcknowledge()
        {
            Assert.That(Field("minimumPreparationDisplaySeconds"), Is.EqualTo(2.5f));
            Assert.That(Field("minimumResultDisplaySeconds"), Is.EqualTo(2.5f));
            var state = Receive(MatchPhase.Preparing);
            Assert.That(CanPrepare(state), Is.False);
            Call(presenter, "OnDestroy");
            Assert.That(CanPrepare(state), Is.False);
        }

        [Test]
        public void PreparationNeedsRenderedStageAndResetsAfterDealOrHiddenPanel()
        {
            Set(presenter, "minimumPreparationDisplaySeconds", 0f);
            var first = Receive(MatchPhase.Preparing);
            Assert.That(CanPrepare(first), Is.False);
            Observe();
            Assert.That(CanPrepare(first), Is.True);
            var dealt = Receive(MatchPhase.Preparing, 1);
            Assert.That(CanPrepare(dealt), Is.False);
            Observe();
            Assert.That(CanPrepare(dealt), Is.True);
            panel.SetActive(false);
            Observe();
            Assert.That(CanPrepare(dealt), Is.False);
            panel.SetActive(true);
            Assert.That(CanPrepare(dealt), Is.False);
            Observe();
            Assert.That(CanPrepare(dealt), Is.True);
            Assert.That(authority.Deadline, Is.Null);
        }

        [TestCase("勝利", 0)]
        [TestCase("敗北", 1)]
        [TestCase("初級 ／ 勝利", 0)]
        [TestCase("中級 ／ 勝利", 0)]
        [TestCase("上級 ／ 勝利", 0)]
        [TestCase("神 ／ 勝利", 0)]
        public void ResultStartsAtMatchingVisibleTextAndDisconnectKeepsConfirmedWinner(string text, int winner)
        {
            Set(presenter, "minimumResultDisplaySeconds", 0f);
            Receive(MatchPhase.Finished, winner: winner);
            result.text = "相手の手番";
            Observe();
            Assert.That(CanFinish(), Is.False);
            result.text = text;
            Assert.That(CanFinish(), Is.False);
            Observe();
            Assert.That(CanFinish(), Is.True);
            Call(view, "InterruptLocally");
            Assert.That(CanFinish(), Is.True);
        }

        [Test]
        public void InterruptionBeforePublicStateDoesNotWaitForever()
        {
            Set(presenter, "minimumResultDisplaySeconds", 0f);
            Call(view, "InterruptLocally");
            result.text = "通信中断";
            Assert.That(CanFinish(), Is.False);
            Observe();
            Assert.That(CanFinish(), Is.True);
        }

        [Test]
        public void CanvasTextAndParentFadeAreExcludedAndT20MustResumeAfterFade()
        {
            Set(presenter, "minimumResultDisplaySeconds", 0f);
            Receive(MatchPhase.Finished);
            result.text = "勝利";
            Observe();
            Assert.That(CanFinish(), Is.True);
            var group = root.AddComponent<CanvasGroup>();
            group.alpha = 0.5f;
            Observe();
            Assert.That(CanFinish(), Is.False);
            group.alpha = 1;
            Assert.That(CanFinish(), Is.False);
            Observe();
            Assert.That(CanFinish(), Is.True);
            result.enabled = false;
            Observe();
            Assert.That(CanFinish(), Is.False);
            result.enabled = true;
            Observe();
            Call(presenter, "SetTransitionObscured", true);
            Assert.That(CanFinish(), Is.False);
            Call(presenter, "SetTransitionObscured", false);
            Assert.That(CanFinish(), Is.False);
            Observe();
            Assert.That(CanFinish(), Is.True);
        }

        [TestCase("OnApplicationPause", true, false)]
        [TestCase("OnApplicationFocus", false, true)]
        public void BackgroundCallbacksExcludeHiddenRenderingUntilResumed(string callback, bool hidden, bool resumed)
        {
            Set(presenter, "minimumResultDisplaySeconds", 0f);
            Receive(MatchPhase.Finished);
            result.text = "勝利";
            Observe();
            Assert.That(CanFinish(), Is.True);
            Call(presenter, callback, hidden);
            Observe();
            Assert.That(CanFinish(), Is.False);
            Call(presenter, callback, resumed);
            Assert.That(CanFinish(), Is.False);
            Observe();
            Assert.That(CanFinish(), Is.True);
        }

        [Test]
        public void MenuAndDisabledPresenterCannotCompleteDisplay()
        {
            Set(presenter, "minimumResultDisplaySeconds", 0f);
            Receive(MatchPhase.Finished);
            result.text = "勝利";
            Observe();
            Assert.That(CanFinish(), Is.True);
            ((GameObject)Field("menuPanel")).SetActive(true);
            Observe();
            Assert.That(CanFinish(), Is.False);
            ((GameObject)Field("menuPanel")).SetActive(false);
            Assert.That(CanFinish(), Is.False);
            Observe();
            Assert.That(CanFinish(), Is.True);
            Call(presenter, "OnDisable");
            Assert.That(CanFinish(), Is.False);
        }
    }
}
