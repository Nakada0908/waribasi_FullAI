using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Waribasi.Rules;

namespace Waribasi.MatchFlow.Tests
{
    public sealed class NpcTurnTimingTests
    {
        [TestCase(0.05, false, 0.55)]
        [TestCase(0.6, true, 0)]
        [TestCase(0.9, true, 0)]
        public void FirstActionWaitsOnlyForRemainderAfterThinking(double calculationSeconds, bool ready, double remaining)
        {
            var timing = new NpcTurnTiming();
            timing.ObserveTurn(1, 10, 0.6);
            timing.RecordThinking(calculationSeconds * 1000);
            double finished = 10 + calculationSeconds;
            Assert.That(timing.CanAct(1, finished), Is.EqualTo(ready));
            Assert.That(Math.Max(0, timing.ActionNotBefore - finished), Is.EqualTo(remaining).Within(0.00001));
            Assert.That(timing.CanAct(2, 100), Is.False);
        }

        [Test]
        public void LongPresentationDoesNotUseUpPostCardCalculationBudgetOrResetTurnCounters()
        {
            var timing = new NpcTurnTiming();
            timing.ObserveTurn(1, 0, 0.6);
            timing.RecordThinking(450);
            timing.RecordWaiting(150);
            timing.CardPublished(0.6, 5);
            timing.ObserveTurn(1, 5.5, 0.6);
            Assert.That(timing.ActionNotBefore, Is.EqualTo(5.6));
            Assert.That(timing.RemainingThinkMilliseconds, Is.EqualTo(550));
            Assert.That(timing.CanAct(1, 5.59), Is.False);
            Assert.That(timing.CanAct(1, 5.6), Is.True);
            timing.RecordThinking(200);
            timing.RecordWaiting(4800);
            Assert.That(timing.ThinkMilliseconds, Is.EqualTo(650));
            Assert.That(timing.WaitMilliseconds, Is.EqualTo(4950));
            Assert.That(timing.RemainingThinkMilliseconds, Is.EqualTo(350));
            timing.ObserveTurn(3, 8, 0.6);
            Assert.That(timing.ThinkMilliseconds, Is.Zero);
            Assert.That(timing.WaitMilliseconds, Is.Zero);
            Assert.That(timing.RemainingThinkMilliseconds, Is.EqualTo(1000));
            Assert.That(timing.CanAct(3, 8.59), Is.False);
        }

        [TestCase(0.01)]
        [TestCase(1.5)]
        public void CardPresentationCanBeTunedAndCannotShortenInitialMinimum(double seconds)
        {
            var timing = new NpcTurnTiming();
            timing.ObserveTurn(1, 10, 0.6);
            timing.CardPublished(10.1, seconds);
            double expected = Math.Max(10.6, 10.1 + seconds);
            Assert.That(timing.CanAct(1, expected - 0.001), Is.False);
            Assert.That(timing.CanAct(1, expected), Is.True);
        }

        private static MatchAuthority Sealed(CardDefinition[] cards = null)
        {
            return new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(1, 1),
                new PlayerHands(0, 4), PlayerId.PlayerB, playerBCards: cards,
                playerBEffects: new PlayerEffects(default, new HandEffects(sealUntilTurn: 1)),
                random: new RuleRandom(123)), 0, 100);
        }

        [Test]
        public void MandatorySkipWaitsAtTurnStartAndIsAppliedExactlyOnce()
        {
            var match = Sealed();
            var timing = new NpcTurnTiming();
            timing.ObserveTurn(match.State.TurnNumber, 100, 0.6);
            Assert.That(match.Tick(100.59, timing.CanAct(match.State.TurnNumber, 100.59)), Is.False);
            Assert.That(match.State.StateVersion, Is.Zero);
            Assert.That(match.Tick(100.6, timing.CanAct(match.State.TurnNumber, 100.6)), Is.True);
            Assert.That(match.State.CurrentPlayer, Is.EqualTo(PlayerId.PlayerA));
            Assert.That(match.LastResult.Events.Count(item => item.Type == RuleEventType.TurnSkipped), Is.EqualTo(1));
            Assert.That(match.Tick(100.9), Is.False);
            Assert.That(match.Tick(101.6), Is.True);
            Assert.That(match.State.StateVersion, Is.EqualTo(1));
        }

        [TestCase(0.8)]
        [TestCase(1.8)]
        public void CardAndAutomaticSkipKeepSeparatePublicationsForConfiguredMinimum(double minimum)
        {
            var match = Sealed(new[] { new CardDefinition(CardKind.DrawTwo) });
            var timing = new NpcTurnTiming();
            timing.ObserveTurn(match.State.TurnNumber, 100, 0.6);
            var request = new MatchRequest(match.MatchId, 1, 0, MatchOperation.UseCard, cardId: 1);
            Assert.That(match.Submit(PlayerId.PlayerB, request, 100.6, false).Accepted, Is.True);
            timing.CardPublished(100.6, minimum);
            var card = MatchMessages.Public(match, 0, 0, 2, 100.6);
            Assert.That(card.events.Any(item => item.type == (int)RuleEventType.CardUsed), Is.True);
            Assert.That(match.Tick(100.6 + minimum - 0.01, false), Is.False);
            Assert.That(match.State.StateVersion, Is.EqualTo(1));
            Assert.That(match.LastResult.Events.Any(item => item.Type == RuleEventType.CardUsed), Is.True);
            double readyAt = Math.Max(101.6, timing.ActionNotBefore);
            Assert.That(match.Tick(readyAt, timing.CanAct(match.State.TurnNumber, readyAt)), Is.True);
            Assert.That(match.State.StateVersion, Is.EqualTo(2));
            Assert.That(match.LastResult.Events.Count(item => item.Type == RuleEventType.TurnSkipped), Is.EqualTo(1));
            Assert.That(card.events.Any(item => item.type == (int)RuleEventType.TurnSkipped), Is.False);
            match.Tick(readyAt + 2);
            Assert.That(match.State.StateVersion, Is.EqualTo(2));
            Assert.That(match.State.CardHistory.Count, Is.EqualTo(1));
        }

        [Test]
        public void NextSealedTurnStartsMinimumAfterPreviousAttackPresentation()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(0, 2), PlayerId.PlayerA,
                playerBEffects: new PlayerEffects(default, new HandEffects(2, 2)));
            var match = new MatchAuthority(Guid.NewGuid(), engine, 0, 100);
            Assert.That(match.Submit(PlayerId.PlayerA,
                new MatchRequest(match.MatchId, 1, 0, MatchOperation.Attack, 0, 1), 100).Accepted, Is.True);
            Assert.That(match.Tick(101, false), Is.True);
            Assert.That(match.Gate, Is.EqualTo(InputGate.Accepting));
            Assert.That(match.State.StateVersion, Is.EqualTo(1));
            var timing = new NpcTurnTiming();
            timing.ObserveTurn(match.State.TurnNumber, 101, 0.6);
            Assert.That(match.Tick(101.59, false), Is.False);
            Assert.That(match.Tick(101.6, timing.CanAct(match.State.TurnNumber, 101.6)), Is.True);
            Assert.That(match.State.TurnNumber, Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SurrenderDuringInitialOrExpiredCardPresentationCancelsPendingSkip(bool afterCard)
        {
            var session = new LocalMatchSession(new NpcMatchSetup(NpcDifficulty.Beginner, NpcFirstPlayerChoice.Npc),
                PlayerId.PlayerB);
            var match = Sealed(afterCard ? new[] { new CardDefinition(CardKind.DrawTwo) } : null);
            typeof(LocalMatchSession).GetProperty("Authority").SetValue(session, match);
            if (afterCard)
            {
                Assert.That(session.Submit(session.Npc, new UseCardAction(1), 0, 100.6).Accepted, Is.True);
            }
            long version = match.State.StateVersion;
            Assert.That(session.Surrender(version, 105).Accepted, Is.True);
            Assert.That(match.State.EndReason, Is.EqualTo(MatchEndReason.Surrender));
            Assert.That(match.State.StateVersion, Is.EqualTo(version + 1));
            Assert.That(match.Tick(200), Is.False);
            Assert.That(match.State.CardHistory.Count, Is.EqualTo(afterCard ? 1 : 0));
            Assert.That(match.LastResult.Events.Any(item => item.Type == RuleEventType.TurnSkipped), Is.False);
        }
    }
}
