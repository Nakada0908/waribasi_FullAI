using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Presentation.Tests
{
    public sealed class MatchInteractionTests
    {
        private GameObject owner;
        private GameObject selectionOwner;
        private Component view;
        private Component selection;
        private MatchAuthority match;

        private static Type RuntimeType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp").GetType(name, true);
        }

        private static object Call(object target, string method, params object[] args)
        {
            return target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args);
        }

        private static T Read<T>(object target, string property)
        {
            return (T)target.GetType().GetProperty(property).GetValue(target);
        }

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("PresentationState");
            view = owner.AddComponent(RuntimeType("Waribasi.Networking.MatchClientState"));
            match = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(2, 2), new PlayerHands(3, 4), PlayerId.PlayerA,
                playerACards: new[] { new CardDefinition(CardKind.SelfBoth, 1, 4), new CardDefinition(CardKind.DrawTwo) }), 30, 100);
            Call(view, "ReceivePublic", MatchMessages.Public(match, 0, 7, 3, 100), 0UL);
            Call(view, "ReceiveOwn", MatchMessages.OwnHand(match, PlayerId.PlayerA));
            selectionOwner = new GameObject("Selection");
            selectionOwner.SetActive(false);
            selection = selectionOwner.AddComponent(RuntimeType("Waribasi.Presentation.MatchInteractionController"));
            Call(selection, "Bind", view);
        }

        [TearDown]
        public void TearDown()
        {
            Call(selection, "Bind", (object)null);
            UnityEngine.Object.DestroyImmediate(selectionOwner);
            UnityEngine.Object.DestroyImmediate(owner);
        }

        [Test]
        public void CancellationDoesNotSendOrConsumeAnything()
        {
            Call(selection, "SelectCard", 0);
            Assert.That(Read<bool>(selection, "HasSelection"), Is.True);
            Call(selection, "SelectOption", 1);
            Call(selection, "CancelSelection");
            Assert.That(Read<bool>(selection, "HasSelection"), Is.False);
            Assert.That(Read<bool>(view, "HasPendingRequest"), Is.False);
            Assert.That(Read<System.Collections.Generic.IReadOnlyList<HandCard>>(view, "OwnHand").Count, Is.EqualTo(2));
            Assert.That(match.State.StateVersion, Is.EqualTo(0));
        }

        [Test]
        public void HistoryAndDuplicatePrivateDeliveryPreserveTheSelection()
        {
            Call(selection, "SelectCard", 0);
            Call(view, "ReceiveHistory", MatchMessages.History(match, 0));
            Call(view, "ReceiveOwn", MatchMessages.OwnHand(match, PlayerId.PlayerA));
            Assert.That(Read<HandCard>(selection, "SelectedCard").Id, Is.EqualTo(1));
        }

        [TestCase("version")]
        [TestCase("gate")]
        [TestCase("interrupt")]
        public void StateOrGateChangesAndDisconnectCancelSelections(string change)
        {
            Call(selection, "OpenRedistribution");
            if (change == "interrupt")
            {
                Call(view, "InterruptLocally");
            }
            else
            {
                var state = MatchMessages.Public(match, 0, 7, 3, 100);
                state.sequence++;
                if (change == "version")
                {
                    state.stateVersion++;
                }
                else
                {
                    state.gate = (int)InputGate.Presenting;
                }
                Call(view, "ReceivePublic", state, 0UL);
            }
            Assert.That(Read<bool>(selection, "HasSelection"), Is.False);
            Assert.That(Read<bool>(selection, "CanInteract"), Is.False);
        }

        [Test]
        public void HistoryStopsGameSelectionWithoutSendingARequest()
        {
            Call(selection, "SelectCard", 0);
            Call(selection, "SetHistoryOpen", true);
            Call(selection, "SelectCard", 1);
            Assert.That(Read<bool>(selection, "CanInteract"), Is.False);
            Assert.That(Read<bool>(selection, "HasSelection"), Is.False);
            Assert.That(Read<bool>(view, "HasPendingRequest"), Is.False);
            Call(selection, "SetHistoryOpen", false);
            Assert.That(Read<bool>(selection, "CanInteract"), Is.True);
        }

        [Test]
        public void PendingRequestStopsFurtherSelectionAndConfirmation()
        {
            Call(view, "CreateRequest", MatchOperation.UseCard, -1, -1, 0, 0, 2L, false);
            Call(selection, "SelectCard", 0);
            Call(selection, "OpenRedistribution");
            Call(selection, "ConfirmSelection");
            Assert.That(Read<bool>(selection, "HasSelection"), Is.False);
            Assert.That(Read<bool>(view, "HasPendingRequest"), Is.True);
        }

        [Test]
        public void ADrawCardDoesNotHighlightAnyBoardHand()
        {
            Call(selection, "SelectCard", 1);
            var owner = new GameObject("UntargetedHand");
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Unity.Netcode.NetworkObject")).First(t => t != null);
                var hand = owner.AddComponent(type);
                Assert.That(Call(selection, "IsHandSelected", hand), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [TestCase(-1, 0)]
        [TestCase(0, 0)]
        [TestCase(0.01, 1)]
        [TestCase(9.01, 10)]
        public void ClockRoundsUpAndNeverReturnsNegativeSeconds(double difference, int expected)
        {
            var type = RuntimeType("Waribasi.Presentation.MatchText");
            Assert.That(type.GetMethod("RemainingSeconds").Invoke(null, new object[] { 100 + difference, 100.0 }), Is.EqualTo(expected));
        }

        [Test]
        public void PointerGateAcceptsOneGestureAndRejectsMouseEchoAndOtherTouches()
        {
            var systemOwner = new GameObject("PointerGate", typeof(EventSystem));
            try
            {
                var router = systemOwner.AddComponent(RuntimeType("Waribasi.Presentation.GameInputRouter"));
                var system = systemOwner.GetComponent<EventSystem>();
                var first = new ExtendedPointerEventData(system) { pointerId = 10, pointerType = UIPointerType.Touch };
                var second = new ExtendedPointerEventData(system) { pointerId = 11, pointerType = UIPointerType.Touch };
                long token = (long)Call(router, "Begin", first);
                Assert.That(token, Is.GreaterThan(0));
                Assert.That(Call(router, "Begin", second), Is.EqualTo(0L));
                Call(router, "End", first, token);
                Assert.That(Call(router, "Consume", token), Is.True);
                Assert.That(Call(router, "Consume", token), Is.False);
                Call(router, "End", second, 0L);
                var mouse = new ExtendedPointerEventData(system) { pointerId = 1, pointerType = UIPointerType.MouseOrPen };
                Assert.That(Call(router, "Begin", mouse), Is.EqualTo(0L));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(systemOwner);
            }
        }
    }
}
