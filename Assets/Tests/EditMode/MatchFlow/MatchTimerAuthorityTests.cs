using System;
using NUnit.Framework;
using Waribasi.Rules;

namespace Waribasi.MatchFlow.Tests
{
    public sealed class MatchTimerAuthorityTests
    {
        [Test]
        public void DefaultsAndExplicitChoicesAreIndependent()
        {
            Assert.That(new MatchSetup(MatchMode.Room).TurnSeconds, Is.EqualTo(60));
            Assert.That(new MatchSetup(MatchMode.Random).TurnSeconds, Is.EqualTo(60));
            Assert.That(MatchSetup.TimeOptions[MatchSetup.DefaultTimeIndex], Is.EqualTo(60));
            Assert.That(MatchSetup.TimeOptions, Is.EqualTo(new[] { 15, 30, 60, 120, 180, 300, 600, 0 }));
            Assert.That(new MatchSetup(MatchMode.Room, turnSeconds: 30).TurnSeconds, Is.EqualTo(30));
            Assert.That(new MatchSetup(MatchMode.Npc).TurnSeconds, Is.Zero);
        }

        [TestCase(30, -0.001, false)]
        [TestCase(30, 0, true)]
        [TestCase(30, 0.001, true)]
        [TestCase(60, -0.001, false)]
        [TestCase(60, 0, true)]
        [TestCase(60, 0.001, true)]
        public void InitialAndRematchClocksStartAfterPreparation(int seconds, double offset, bool timeout)
        {
            var previousId = Guid.Empty;
            for (int round = 0; round < 2; round++)
            {
                var match = new MatchAuthority(Guid.NewGuid(), PlayerId.PlayerA, seconds);
                Assert.That(match.MatchId, Is.Not.EqualTo(previousId));
                previousId = match.MatchId;
                double start = 500 + round * 1000;
                Assert.That(match.Tick(start - 100), Is.False);
                Assert.That(match.DealInitialCards(), Is.True);
                Assert.That(match.Tick(start - 1), Is.False);
                Assert.That(match.Deadline, Is.Null);
                Assert.That(match.Start(start), Is.True);
                Assert.That(match.Deadline, Is.EqualTo(start + seconds));
                Assert.That(match.Tick(start + seconds + offset), Is.EqualTo(timeout));
                Assert.That(match.State.MatchPhase, Is.EqualTo(timeout ? MatchPhase.Finished : MatchPhase.Active));
                if (timeout)
                {
                    Assert.That(match.State.EndReason, Is.EqualTo(MatchEndReason.Timeout));
                    long version = match.State.StateVersion;
                    Assert.That(match.Tick(start + seconds + 1), Is.False);
                    Assert.That(match.State.StateVersion, Is.EqualTo(version));
                }
            }
        }

        [TestCase(30, -0.001, false)]
        [TestCase(30, 0, true)]
        [TestCase(30, 0.001, true)]
        [TestCase(60, -0.001, false)]
        [TestCase(60, 0, true)]
        [TestCase(60, 0.001, true)]
        public void NextTurnGetsItsFullTimeWhenPresentationEnds(int seconds, double offset, bool timeout)
        {
            var match = Active(seconds);
            var attack = new MatchRequest(match.MatchId, 1, match.State.StateVersion, MatchOperation.Attack, 0, 0);
            Assert.That(match.Submit(PlayerId.PlayerA, attack, 110).Accepted, Is.True);
            Assert.That(match.Gate, Is.EqualTo(InputGate.Presenting));
            Assert.That(match.Deadline, Is.Null);
            Assert.That(match.Tick(110.999), Is.False);
            //遅いフレームでも演出終了予定時刻へ遡って時計を開始しない。
            Assert.That(match.Tick(115), Is.True);
            Assert.That(match.State.CurrentPlayer, Is.EqualTo(PlayerId.PlayerB));
            Assert.That(match.Deadline, Is.EqualTo(115 + seconds));
            Assert.That(match.Tick(115 + seconds + offset), Is.EqualTo(timeout));
        }

        [TestCase(30, -0.001, true)]
        [TestCase(30, 0, false)]
        [TestCase(30, 0.001, false)]
        [TestCase(60, -0.001, true)]
        [TestCase(60, 0, false)]
        [TestCase(60, 0.001, false)]
        public void RequestAndTickShareTheExactDeadline(int seconds, double offset, bool accepted)
        {
            var match = Active(seconds);
            var attack = new MatchRequest(match.MatchId, 1, 0, MatchOperation.Attack, 0, 0);
            var response = match.Submit(PlayerId.PlayerA, attack, 100 + seconds + offset);
            Assert.That(response.Accepted, Is.EqualTo(accepted));
            if (!accepted)
            {
                Assert.That(match.State.EndReason, Is.EqualTo(MatchEndReason.Timeout));
                Assert.That(match.State.Loser, Is.EqualTo(PlayerId.PlayerA));
            }
        }

        [TestCase(30)]
        [TestCase(60)]
        public void CardInvalidRequestAndIdleUpdatesNeverRefillTime(int seconds)
        {
            var match = Active(seconds);
            double deadline = 100 + seconds;
            var card = new MatchRequest(match.MatchId, 1, 0, MatchOperation.UseCard, target: 0, cardId: 1);
            Assert.That(match.Submit(PlayerId.PlayerA, card, 110).Accepted, Is.True);
            Assert.That(match.Deadline, Is.EqualTo(deadline));
            Assert.That(match.Submit(PlayerId.PlayerA, card, 111).Replayed, Is.True);
            var invalid = new MatchRequest(match.MatchId, 2, match.State.StateVersion, MatchOperation.Redistribute, left: 4, right: 4);
            Assert.That(match.Submit(PlayerId.PlayerA, invalid, 112).Accepted, Is.False);
            for (int now = 113; now < deadline; now++)
            {
                Assert.That(match.Tick(now), Is.False);
                Assert.That(match.Deadline, Is.EqualTo(deadline));
            }
            Assert.That(match.Tick(deadline), Is.True);
            Assert.That(match.State.EndReason, Is.EqualTo(MatchEndReason.Timeout));
        }

        private static MatchAuthority Active(int seconds)
        {
            return new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1),
                PlayerId.PlayerA, playerACards: new[] { new CardDefinition(CardKind.Defense) }), seconds, 100);
        }
    }
}
