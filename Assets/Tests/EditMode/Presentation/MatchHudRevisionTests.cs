using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Presentation.Tests
{
    public sealed class MatchHudRevisionTests
    {
        private static object Display(string method, params object[] args)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("Waribasi.Presentation.MatchHudDisplay"))
                .First(value => value != null);
            return type.GetMethod(method).Invoke(null, args);
        }

        private static PublicMatchMessage Active()
        {
            return new PublicMatchMessage
            {
                matchPhase = (int)MatchPhase.Active, currentPlayer = 0,
                gate = (int)InputGate.Accepting, turnSeconds = 30, hasDeadline = true, deadline = 130
            };
        }

        [TestCase(0, -1, 0)]
        [TestCase(0, 0, 0)]
        [TestCase(0, 1, 1)]
        [TestCase(0, 2, 2)]
        [TestCase(0, 3, 3)]
        [TestCase(0, 4, 4)]
        [TestCase(0, 5, 5)]
        [TestCase(0, 6, 5)]
        [TestCase(1, 3, 3)]
        [TestCase(-1, 3, 0)]
        public void OpponentBacksUseOnlyTheOtherSeatsPublicCount(int seat, int count, int expected)
        {
            var state = new PublicMatchMessage { playerACardCount = seat == 1 ? count : 5, playerBCardCount = seat == 0 ? count : 5 };
            Assert.That(Display("OpponentCardCount", state, seat), Is.EqualTo(expected));
            Assert.That(Display("OpponentCardCount", null, seat), Is.Zero);
        }

        [TestCase(0, "自分の手番")]
        [TestCase(1, "相手の手番")]
        public void TurnShowsTheCurrentActorAndDifficultyWithoutRepeatingInitialOrder(int seat, string expected)
        {
            var state = Active();
            Assert.That(Display("Turn", state, seat, false, null), Is.EqualTo(expected));
            state.gate = (int)InputGate.Presenting;
            Assert.That(Display("Turn", state, seat, false, "上級"), Is.EqualTo("上級 ／ " + expected));
            Assert.That(Display("Turn", state, seat, true, null), Is.EqualTo("通信中断"));
        }

        [TestCase(0, "あなたは先手", "あなたは後手")]
        [TestCase(1, "あなたは後手", "あなたは先手")]
        public void ARematchUsesItsNewInitialOrder(int seat, string first, string rematch)
        {
            Assert.That(Display("InitialOrder", new PublicMatchMessage { currentPlayer = 0 }, seat), Is.EqualTo(first));
            Assert.That(Display("InitialOrder", new PublicMatchMessage { currentPlayer = 1 }, seat), Is.EqualTo(rematch));
        }

        [TestCase(100, "30秒", false)]
        [TestCase(119.99, "11秒", false)]
        [TestCase(120, "! 10秒", true)]
        [TestCase(129.1, "! 1秒", true)]
        [TestCase(130, "! 0秒", true)]
        [TestCase(131, "! 0秒", true)]
        public void CountdownUsesTheHostDeadlineAndEmphasizesTheLastTenSeconds(double now, string text, bool urgent)
        {
            var state = Active();
            Assert.That(Display("Timer", state, now), Is.EqualTo(text));
            Assert.That(Display("TimerUrgent", state, now), Is.EqualTo(urgent));
        }

        [TestCase(true, false, 30, MatchPhase.Active, false)]
        [TestCase(false, false, 0, MatchPhase.Active, false)]
        [TestCase(false, true, 30, MatchPhase.Active, false)]
        [TestCase(false, false, 30, MatchPhase.Preparing, false)]
        [TestCase(false, false, 30, MatchPhase.Finished, false)]
        [TestCase(false, false, 30, MatchPhase.Active, true)]
        public void NpcUnlimitedInterruptedAndNonActiveMatchesHaveNoCountdown(bool local, bool interrupted, int seconds, MatchPhase phase, bool expected)
        {
            var state = Active();
            state.turnSeconds = seconds;
            state.matchPhase = (int)phase;
            Assert.That(Display("TimerVisible", state, local, interrupted), Is.EqualTo(expected));
            Assert.That(Display("TimerVisible", null, local, interrupted), Is.False);
        }

        [Test]
        public void PresentationIsDistinctFromTheRunningClock()
        {
            var state = Active();
            state.gate = (int)InputGate.Presenting;
            state.hasDeadline = false;
            Assert.That(Display("Timer", state, 129d), Is.EqualTo("反映中"));
            Assert.That(Display("TimerUrgent", state, 129d), Is.False);
        }

        [Test]
        public void ACardDoesNotExtendTheDeadlineAndTheFallbackClockKeepsMoving()
        {
            var authority = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1),
                PlayerId.PlayerA, playerACards: new[] { new CardDefinition(CardKind.SelfSingle, 2) }), 30, 100);
            var result = authority.Submit(PlayerId.PlayerA, new MatchRequest(authority.MatchId, 1, 0, MatchOperation.UseCard,
                target: (int)HandSide.Left, cardId: 1), 110);
            Assert.That(result.Accepted, Is.True);
            Assert.That(authority.Deadline, Is.EqualTo(130));
            var state = MatchMessages.Public(authority, 0, 7, 3, 110);
            Assert.That(Display("Timer", state, 110d), Is.EqualTo("20秒"));
            double later = (double)Display("EstimatedServerTime", 110d, 10d, 15d);
            Assert.That(later, Is.EqualTo(115));
            Assert.That(Display("Timer", state, later), Is.EqualTo("15秒"));
            Assert.That(Display("EstimatedServerTime", 110d, 10d, 9d), Is.EqualTo(110));
        }

        [TestCase(RuleEventType.HandChanged)]
        [TestCase(RuleEventType.HandRevived)]
        [TestCase(RuleEventType.EffectApplied)]
        [TestCase(RuleEventType.ChangeBlocked)]
        public void ResultMarkersFollowThePublicEventTarget(RuleEventType kind)
        {
            var state = new PublicMatchMessage { events = new[] { new RuleEventMessage { type = (int)kind, player = 1, hand = 0 } } };
            Assert.That(Display("ChangedHand", state, PlayerId.PlayerB, HandSide.Left), Is.True);
            Assert.That(Display("ChangedHand", state, PlayerId.PlayerA, HandSide.Left), Is.False);
            Assert.That(Display("ChangedHand", state, PlayerId.PlayerB, HandSide.Right), Is.False);
            Assert.That(Display("ChangedHand", null, PlayerId.PlayerB, HandSide.Left), Is.False);
            state.events[0].type = (int)RuleEventType.CardUsed;
            Assert.That(Display("ChangedHand", state, PlayerId.PlayerB, HandSide.Left), Is.False);
        }
    }
}
