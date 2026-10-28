using System;
using System.Collections.Generic;
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
    public sealed class CardInteractionRevisionTests
    {
        private GameObject root;
        private Component view;
        private Component interaction;
        private Component drawing;
        private MatchAuthority authority;
        private Button[] choices;
        private Button[] slots;
        private Button confirm;
        private Button change;
        private Button skip;
        private Action refreshCards;

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
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static T Read<T>(object target, string property)
        {
            return (T)target.GetType().GetProperty(property).GetValue(target);
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(root.transform, false);
            return child;
        }

        private Button Button(string name)
        {
            var owner = Child(name);
            owner.AddComponent<Image>();
            var button = owner.AddComponent<Button>();
            button.targetGraphic = owner.GetComponent<Image>();
            var label = Child(name + "Label");
            label.transform.SetParent(owner.transform, false);
            label.AddComponent<TextMeshProUGUI>();
            return button;
        }

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("CardRevision");
            root.SetActive(false);
            view = Child("State").AddComponent(RuntimeType("Waribasi.Networking.MatchClientState"));
            interaction = Child("Interaction").AddComponent(RuntimeType("Waribasi.Presentation.MatchInteractionController"));
            choices = Enumerable.Range(0, 5).Select(i => Button("Option" + i)).ToArray();
            slots = Enumerable.Range(0, 5).Select(i => Button("Card" + i)).ToArray();
            confirm = Button("Confirm");
            change = Button("Change");
            skip = Button("Skip");
            Set(interaction, "selectionPanel", Child("Panel"));
            Set(interaction, "selectionText", Child("Description").AddComponent<TextMeshProUGUI>());
            Set(interaction, "optionButtons", choices);
            Set(interaction, "optionTexts", choices.Select(b => b.GetComponentInChildren<TMP_Text>(true)).ToArray());
            Set(interaction, "confirmButton", confirm);
            Set(interaction, "cancelButton", Button("Cancel"));
            Set(interaction, "attackCancelButton", Button("AttackCancel"));
            Set(interaction, "redistributeButton", change);
            Set(interaction, "swapButton", skip);
            Call(interaction, "Awake");
            drawing = Child("Drawing").AddComponent(RuntimeType("CardButtonDraw"));
            Set(drawing, "buttons", slots);
            refreshCards = () => Call(drawing, "DrawOwnHand", Read<IReadOnlyList<HandCard>>(view, "OwnHand"));
            interaction.GetType().GetEvent("Changed").AddEventHandler(interaction, refreshCards);
        }

        [TearDown]
        public void TearDown()
        {
            interaction.GetType().GetEvent("Changed").RemoveEventHandler(interaction, refreshCards);
            Call(interaction, "Bind", (object)null);
            Call(interaction, "OnDestroy");
            UnityEngine.Object.DestroyImmediate(root);
        }

        private void Position(PlayerHands own, CardDefinition card, PlayerEffects effects = default)
        {
            authority = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(own, new PlayerHands(2, 3), PlayerId.PlayerA,
                playerACards: new[] { card }, playerAEffects: effects), 30, 100);
            Call(view, "ReceivePublic", MatchMessages.Public(authority, 0, 7, 3, 100), 0UL);
            Call(view, "ReceiveOwn", MatchMessages.OwnHand(authority, PlayerId.PlayerA));
            Call(interaction, "Bind", view);
        }

        [TestCase(CardKind.SelfBoth, 4, 4, 2, 2)]
        [TestCase(CardKind.SelfBoth, 1, 4, 1, 4)]
        [TestCase(CardKind.SelfSingle, 4, 0, 4, 2)]
        [TestCase(CardKind.SelfSingle, 4, 0, 0, 2)]
        [TestCase(CardKind.DrawTwo, 0, 0, 2, 2)]
        public void SingleLegalChoiceNeedsOnlyConfirmationAndCancellation(CardKind kind, int first, int second, int left, int right)
        {
            Position(new PlayerHands(left, right), new CardDefinition(kind, first, second));
            Call(interaction, "SelectCard", 0);
            Assert.That(Read<bool>(interaction, "HasSelection"), Is.True);
            Assert.That(confirm.interactable, Is.True);
            Assert.That(choices.Any(b => b.gameObject.activeSelf), Is.False);
            Assert.That(Read<bool>(view, "HasPendingRequest"), Is.False);
            Assert.That(authority.State.StateVersion, Is.Zero);
            Assert.That(authority.GetObservation(PlayerId.PlayerA).OwnHand.Count, Is.EqualTo(1));
            Call(interaction, "CancelSelection");
            Assert.That(Read<bool>(interaction, "HasSelection"), Is.False);
            Assert.That(authority.State.StateVersion, Is.Zero);
        }

        [TestCase(CardKind.SelfBoth, 1, 4)]
        [TestCase(CardKind.SelfSingle, 4, 0)]
        public void MeaningfulMultipleChoicesStillRequireASelection(CardKind kind, int first, int second)
        {
            Position(new PlayerHands(2, 2), new CardDefinition(kind, first, second));
            Call(interaction, "SelectCard", 0);
            Assert.That(choices.Count(b => b.gameObject.activeSelf), Is.EqualTo(2));
            Assert.That(confirm.interactable, Is.False);
            Call(interaction, "SelectOption", 1);
            Assert.That(confirm.interactable, Is.True);
            Assert.That(authority.State.StateVersion, Is.Zero);
        }

        [TestCase(CardKind.Revival, 0, 0, false)]
        [TestCase(CardKind.SelfBoth, 2, 2, false)]
        [TestCase(CardKind.SelfSingle, 2, 0, false)]
        [TestCase(CardKind.SelfBoth, 1, 4, true)]
        public void UnusableCardsAreDimmedAndCannotBeSelected(CardKind kind, int first, int second, bool sealedHand)
        {
            var effects = sealedHand ? new PlayerEffects(new HandEffects(sealUntilTurn: 1), default) : default;
            Position(new PlayerHands(2, 2), new CardDefinition(kind, first, second), effects);
            Assert.That(Call(interaction, "CanUseCard", 1L), Is.False);
            Assert.That(slots[0].interactable, Is.False);
            Assert.That(slots[0].GetComponent<CanvasGroup>().alpha, Is.EqualTo(0.45f).Within(0.001));
            Assert.That(slots[0].GetComponent<CanvasGroup>().interactable, Is.False);
            Assert.That(slots[0].colors.disabledColor, Is.EqualTo(slots[0].colors.normalColor));
            Call(interaction, "SelectCard", 0);
            Assert.That(Read<bool>(interaction, "HasSelection"), Is.False);
            Assert.That(authority.State.StateVersion, Is.Zero);
        }

        [TestCase("used")]
        [TestCase("opponent")]
        [TestCase("presenting")]
        [TestCase("finished")]
        [TestCase("stale")]
        public void PublicStateLocksCardsAndTheNextOwnTurnRestoresThem(string reason)
        {
            Position(new PlayerHands(2, 2), new CardDefinition(CardKind.Defense));
            Call(interaction, "SelectCard", 0);
            var state = MatchMessages.Public(authority, 0, 7, 3, 100);
            state.sequence++;
            if (reason == "used")
            {
                state.turnPhase = (int)TurnPhase.NormalActionOnly;
            }
            if (reason == "opponent")
            {
                state.currentPlayer = 1;
            }
            if (reason == "presenting")
            {
                state.gate = (int)InputGate.Presenting;
            }
            if (reason == "finished")
            {
                state.matchPhase = (int)MatchPhase.Finished;
            }
            if (reason == "stale")
            {
                state.stateVersion++;
            }
            Call(view, "ReceivePublic", state, 0UL);
            Assert.That(Read<bool>(interaction, "HasSelection"), Is.False);
            Assert.That(slots[0].interactable, Is.False);
            Assert.That(slots[0].GetComponent<CanvasGroup>().alpha, Is.LessThan(1));
            Call(interaction, "SelectCard", 0);
            Call(interaction, "ConfirmSelection");
            Assert.That(Read<bool>(view, "HasPendingRequest"), Is.False);
            if (reason == "finished")
            {
                return;
            }
            var restored = MatchMessages.Public(authority, 0, 7, 3, 100);
            restored.sequence = state.sequence + 1;
            restored.stateVersion = state.stateVersion;
            Call(view, "ReceivePublic", restored, 0UL);
            var own = MatchMessages.OwnHand(authority, PlayerId.PlayerA);
            own.stateVersion = restored.stateVersion;
            Call(view, "ReceiveOwn", own);
            Assert.That(slots[0].interactable, Is.True);
            Assert.That(slots[0].GetComponent<CanvasGroup>().alpha, Is.EqualTo(1));
        }

        [TestCase("history")]
        [TestCase("modal")]
        [TestCase("pending")]
        [TestCase("interrupted")]
        public void LocalGatesRejectDirectCardCallsAndConfirmation(string reason)
        {
            Position(new PlayerHands(2, 2), new CardDefinition(CardKind.DrawTwo));
            Call(interaction, "SelectCard", 0);
            if (reason == "history")
            {
                Call(interaction, "SetHistoryOpen", true);
            }
            if (reason == "modal")
            {
                Call(interaction, "SetModalBlocked", true);
            }
            if (reason == "pending")
            {
                Call(view, "CreateRequest", MatchOperation.UseCard, -1, -1, 0, 0, 1L, false);
            }
            if (reason == "interrupted")
            {
                Call(view, "InterruptLocally");
            }
            refreshCards();
            Call(interaction, "SelectCard", 0);
            Call(interaction, "ConfirmSelection");
            Assert.That(slots[0].interactable, Is.False);
            Assert.That(Call(interaction, "CanUseCard", 1L), Is.False);
            Assert.That(authority.State.StateVersion, Is.Zero);
            Assert.That(Read<bool>(view, "HasPendingRequest"), Is.EqualTo(reason == "pending"));
        }

        [Test]
        public void RedistributionIncludesSwapAndTheSeparateButtonIsOnlyForSkip()
        {
            Position(new PlayerHands(1, 3), new CardDefinition(CardKind.DrawTwo));
            Assert.That(change.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("手を変更"));
            Assert.That(skip.gameObject.activeSelf, Is.False);
            Call(interaction, "OpenRedistribution");
            var legal = Read<IReadOnlyList<GameAction>>(interaction, "LegalActions").OfType<RedistributeAction>().ToArray();
            Assert.That(legal.Any(a => a.Left == 3 && a.Right == 1), Is.True);
            Assert.That(choices.Count(b => b.gameObject.activeSelf), Is.EqualTo(legal.Length));
        }

        [Test]
        public void SealedLastHandRetainsTheCardOrSkipDecision()
        {
            Position(new PlayerHands(0, 2), new CardDefinition(CardKind.DrawTwo),
                new PlayerEffects(default, new HandEffects(sealUntilTurn: 1)));
            Assert.That(change.interactable, Is.False);
            Assert.That(skip.gameObject.activeSelf && skip.interactable, Is.True);
            Assert.That(skip.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("カードを使わない"));
            Assert.That(slots[0].interactable, Is.True);
            Call(interaction, "OpenRedistribution");
            Assert.That(Read<bool>(interaction, "HasSelection"), Is.False);
            Call(interaction, "SelectCard", 0);
            Assert.That(confirm.interactable, Is.True);
            Call(interaction, "CancelSelection");
            Assert.That(skip.interactable, Is.True);
            Assert.That(authority.State.StateVersion, Is.Zero);
        }
    }
}
