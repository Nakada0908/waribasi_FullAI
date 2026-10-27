using System;
using NUnit.Framework;
using Waribasi.Rules;

namespace Waribasi.MatchFlow.Tests
{
    public sealed class MatchAuthorityTests
    {
        private static MatchAuthority Active(int seconds = 30, CardDefinition[] cards = null)
        {
            return new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(1, 3),
                new PlayerHands(1, 1), PlayerId.PlayerA, playerACards: cards, random: new RuleRandom(123)), seconds, 100);
        }

        private static MatchRequest Attack(MatchAuthority match, ulong id = 1, long? version = null)
        {
            return new MatchRequest(match.MatchId, id, version ?? match.State.StateVersion, MatchOperation.Attack, 0, 0);
        }

        [Test]
        public void PreparationRequiresDealAndExplicitStart()
        {
            var match = new MatchAuthority(Guid.NewGuid(), PlayerId.PlayerB, 30);
            Assert.That(match.Gate, Is.EqualTo(InputGate.Preparing));
            Assert.That(match.Deadline, Is.Null);
            Assert.That(match.Start(1), Is.False);
            Assert.That(match.DealInitialCards(), Is.True);
            Assert.That(match.DealInitialCards(), Is.False);
            Assert.That(match.State.StateVersion, Is.EqualTo(1));
            Assert.That(match.GetObservation(PlayerId.PlayerA).OwnHand.Count, Is.EqualTo(5));
            Assert.That(match.Start(2), Is.True);
            Assert.That(match.Start(2), Is.False);
            Assert.That(match.State.StateVersion, Is.EqualTo(2));
            Assert.That(match.State.CurrentPlayer, Is.EqualTo(PlayerId.PlayerB));
            Assert.That(match.Deadline, Is.EqualTo(32));
        }

        [TestCase(129.999, true)]
        [TestCase(130.0, false)]
        [TestCase(130.001, false)]
        public void BoundaryUsesHostProcessingTime(double now, bool accepted)
        {
            var match = Active();
            var response = match.Submit(PlayerId.PlayerA, Attack(match), now);
            Assert.That(response.Accepted, Is.EqualTo(accepted));
            Assert.That(match.State.MatchPhase, Is.EqualTo(accepted ? MatchPhase.Active : MatchPhase.Finished));
            if (!accepted)
            {
                Assert.That(match.State.EndReason, Is.EqualTo(MatchEndReason.Timeout));
                Assert.That(match.State.Loser, Is.EqualTo(PlayerId.PlayerA));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TimeoutAndActionAreSingleCommitInEitherOrder(bool timeoutFirst)
        {
            var match = Active();
            var request = Attack(match);
            if (timeoutFirst)
            {
                match.Tick(130);
            }
            Assert.That(match.Submit(PlayerId.PlayerA, request, 130).Accepted, Is.False);
            match.Tick(130);
            Assert.That(match.State.StateVersion, Is.EqualTo(1));
            Assert.That(match.State.EndReason, Is.EqualTo(MatchEndReason.Timeout));
        }

        [Test]
        public void AcceptedActionClosesInputAndNextClockStartsAfterPresentation()
        {
            var match = Active();
            Assert.That(match.Submit(PlayerId.PlayerA, Attack(match), 129.9).Accepted, Is.True);
            Assert.That(match.State.CurrentPlayer, Is.EqualTo(PlayerId.PlayerB));
            Assert.That(match.Gate, Is.EqualTo(InputGate.Presenting));
            Assert.That(match.Deadline, Is.Null);
            var early = new MatchRequest(match.MatchId, 1, match.State.StateVersion, MatchOperation.Attack, 0, 0);
            Assert.That(match.Submit(PlayerId.PlayerB, early, 130).Rejection, Is.EqualTo(RequestRejection.InputClosed));
            Assert.That(match.Tick(130.899), Is.False);
            Assert.That(match.Tick(130.9), Is.True);
            Assert.That(match.Deadline, Is.EqualTo(160.9).Within(0.00001));
            Assert.That(match.LastResult, Is.Null);
            Assert.That(match.Submit(PlayerId.PlayerB, new MatchRequest(match.MatchId, 2, match.State.StateVersion,
                MatchOperation.Attack, 0, 0), 131).Accepted, Is.True);
        }

        [Test]
        public void CardAndInvalidRequestDoNotExtendDeadline()
        {
            var match = Active(cards: new[] { new CardDefinition(CardKind.Defense) });
            long id = match.GetObservation(PlayerId.PlayerA).OwnHand[0].Id;
            Assert.That(match.Submit(PlayerId.PlayerA, new MatchRequest(match.MatchId, 1, 0,
                MatchOperation.UseCard, target: 0, cardId: id), 120).Accepted, Is.True);
            Assert.That(match.Gate, Is.EqualTo(InputGate.Accepting));
            Assert.That(match.Deadline, Is.EqualTo(130));
            Assert.That(match.Submit(PlayerId.PlayerA, new MatchRequest(match.MatchId, 2, 1,
                MatchOperation.Redistribute, left: 4, right: 4), 129).Accepted, Is.False);
            Assert.That(match.Deadline, Is.EqualTo(130));
            Assert.That(match.Tick(130), Is.True);
            Assert.That(match.State.EndReason, Is.EqualTo(MatchEndReason.Timeout));
        }

        [Test]
        public void UnchangedCardDoesNotConsumeCardVersionOrClock()
        {
            var match = Active(cards: new[] { new CardDefinition(CardKind.SelfSingle, 1) });
            var response = match.Submit(PlayerId.PlayerA, new MatchRequest(match.MatchId, 1, 0,
                MatchOperation.UseCard, target: 0, cardId: 1), 120);
            Assert.That(response.RuleRejection, Is.EqualTo(ActionRejection.CardUnchanged));
            Assert.That(match.State.StateVersion, Is.Zero);
            Assert.That(match.GetObservation(PlayerId.PlayerA).OwnHand.Count, Is.EqualTo(1));
            Assert.That(match.Deadline, Is.EqualTo(130));
        }

        [Test]
        public void RetransmissionDoesNotApplyOrNotifyAgainEvenAfterDeadline()
        {
            var match = Active();
            var request = Attack(match);
            var first = match.Submit(PlayerId.PlayerA, request, 129.9);
            long sequence = match.DeliverySequence;
            var replay = match.Submit(PlayerId.PlayerA, request, 140);
            Assert.That(first.Accepted, Is.True);
            Assert.That(replay.Accepted, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(match.State.StateVersion, Is.EqualTo(1));
            Assert.That(match.DeliverySequence, Is.EqualTo(sequence));
        }

        [Test]
        public void ModifiedReplayCannotChangeTheOriginalResponse()
        {
            var match = Active();
            var original = Attack(match);
            Assert.That(match.Submit(PlayerId.PlayerA, original, 100).Accepted, Is.True);
            var modified = new MatchRequest(match.MatchId, 1, 0, MatchOperation.Redistribute, left: 2, right: 2);
            Assert.That(match.Submit(PlayerId.PlayerA, modified, 100).Rejection, Is.EqualTo(RequestRejection.ChangedRequest));
            Assert.That(match.Submit(PlayerId.PlayerA, original, 100).Replayed, Is.True);
            Assert.That(match.State.StateVersion, Is.EqualTo(1));
        }

        [Test]
        public void RejectedRequestIdCannotBecomeAValidActionLater()
        {
            var match = Active();
            var stale = Attack(match, version: 99);
            Assert.That(match.Submit(PlayerId.PlayerA, stale, 100).RuleRejection, Is.EqualTo(ActionRejection.StaleState));
            Assert.That(match.Submit(PlayerId.PlayerA, Attack(match), 100).Rejection, Is.EqualTo(RequestRejection.ChangedRequest));
            Assert.That(match.State.StateVersion, Is.Zero);
        }

        [Test]
        public void EvictedResponseIsNeverAppliedAgain()
        {
            var match = Active(600);
            for (ulong i = 1; i <= MatchAuthority.ResponseCacheSize + 1; i++)
            {
                match.Submit(PlayerId.PlayerA, Attack(match, i, 99), 100 + i * 0.02);
            }
            Assert.That(match.Submit(PlayerId.PlayerA, Attack(match, 1, 99), 102).Rejection,
                Is.EqualTo(RequestRejection.ExpiredRequest));
            Assert.That(match.State.StateVersion, Is.Zero);
        }

        [TestCase(0UL)]
        [TestCase(2UL)]
        [TestCase(ulong.MaxValue)]
        public void InvalidRequestNumbersDoNotPoisonTheExpectedSequence(ulong id)
        {
            var match = Active();
            Assert.That(match.Submit(PlayerId.PlayerA, Attack(match, id), 100).Rejection,
                Is.EqualTo(RequestRejection.InvalidSequence));
            Assert.That(match.Submit(PlayerId.PlayerA, Attack(match), 100).Accepted, Is.True);
        }

        [TestCase(-1)]
        [TestCase(2)]
        [TestCase(int.MaxValue)]
        public void UnknownPlayerCannotMutateStateOrSequence(int player)
        {
            var match = Active();
            Assert.That(match.Submit((PlayerId)player, Attack(match), 100).Rejection,
                Is.EqualTo(RequestRejection.UnknownPlayer));
            Assert.That(match.Submit(PlayerId.PlayerA, Attack(match), 100).Accepted, Is.True);
        }

        [Test]
        public void PreviousMatchAndOtherPlayerSequencesAreIndependent()
        {
            var match = Active();
            var old = new MatchRequest(Guid.NewGuid(), 1, 0, MatchOperation.Attack, 0, 0);
            Assert.That(match.Submit(PlayerId.PlayerA, old, 100).Rejection, Is.EqualTo(RequestRejection.WrongMatch));
            Assert.That(match.Submit(PlayerId.PlayerB, Attack(match), 100).RuleRejection, Is.EqualTo(ActionRejection.NotYourTurn));
            Assert.That(match.Submit(PlayerId.PlayerA, Attack(match), 100).Accepted, Is.True);
        }

        [TestCase(0)]
        [TestCase(15)]
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        [TestCase(180)]
        [TestCase(300)]
        [TestCase(600)]
        public void AllSpecifiedTimeChoicesAreSupported(int seconds)
        {
            var match = Active(seconds);
            Assert.That(match.Deadline, Is.EqualTo(seconds == 0 ? (double?)null : 100 + seconds));
            if (seconds == 0)
            {
                Assert.That(match.Tick(999999), Is.False);
            }
        }

        [TestCase(-1)]
        [TestCase(1)]
        [TestCase(14)]
        [TestCase(16)]
        [TestCase(601)]
        public void ArbitraryTimeLimitsAreRejected(int seconds)
        {
            Assert.Throws<ArgumentException>(() => Active(seconds));
        }

        [TestCase(MatchEndReason.Timeout)]
        [TestCase(MatchEndReason.Disconnected)]
        [TestCase(MatchEndReason.Surrender)]
        public void ExternalEndDoesNotUseRevivalAndLaterEventsCannotReplaceResult(MatchEndReason reason)
        {
            var match = Active(cards: new[] { new CardDefinition(CardKind.Revival) });
            if (reason == MatchEndReason.Timeout)
            {
                match.Tick(130);
            }
            else if (reason == MatchEndReason.Disconnected)
            {
                match.Disconnect(PlayerId.PlayerA, 100);
            }
            else
            {
                match.Submit(PlayerId.PlayerA, new MatchRequest(match.MatchId, 1, 0, MatchOperation.Surrender), 100);
            }
            Assert.That(match.State.EndReason, Is.EqualTo(reason));
            long version = match.State.StateVersion;
            Assert.That(match.Interrupt(131), Is.False);
            Assert.That(match.Disconnect(PlayerId.PlayerB, 131), Is.False);
            Assert.That(match.State.StateVersion, Is.EqualTo(version));
            Assert.That(match.State.Loser, Is.EqualTo(PlayerId.PlayerA));
            Assert.That(match.GetObservation(PlayerId.PlayerA).OwnHand.Count, Is.EqualTo(1));
        }

        [Test]
        public void PreparationDisconnectHasNoWinner()
        {
            var match = new MatchAuthority(Guid.NewGuid(), PlayerId.PlayerA, 30);
            Assert.That(match.Disconnect(PlayerId.PlayerB, 1), Is.True);
            Assert.That(match.State.MatchPhase, Is.EqualTo(MatchPhase.Interrupted));
            Assert.That(match.State.Winner, Is.Null);
            Assert.That(match.Start(2), Is.False);
        }

        [Test]
        public void SurrenderIsAllowedDuringPresentationAndForTheNonCurrentPlayer()
        {
            var match = Active();
            match.Submit(PlayerId.PlayerA, Attack(match), 100);
            var response = match.Submit(PlayerId.PlayerA, new MatchRequest(match.MatchId, 2, 1, MatchOperation.Surrender), 100.1);
            Assert.That(response.Accepted, Is.True);
            Assert.That(match.State.Loser, Is.EqualTo(PlayerId.PlayerA));
            Assert.That(match.Tick(200), Is.False);
        }

        [Test]
        public void RateLimitHasBoundedWorkAndDoesNotExtendDeadline()
        {
            var match = Active();
            for (ulong i = 1; i <= MatchAuthority.RequestsPerSecond; i++)
            {
                match.Submit(PlayerId.PlayerA, Attack(match, i, 99), 100);
            }
            Assert.That(match.Submit(PlayerId.PlayerA, Attack(match, 65, 99), 100).Rejection,
                Is.EqualTo(RequestRejection.RateLimited));
            Assert.That(match.Deadline, Is.EqualTo(130));
            Assert.That(match.Tick(130), Is.True);
        }

        [Test]
        public void InvalidPayloadCannotSmuggleExtraValuesOrArbitraryEndReasons()
        {
            var match = Active();
            var invalid = new[]
            {
                new MatchRequest(match.MatchId, 1, 0, MatchOperation.Attack, 0, 0, cardId: 1),
                new MatchRequest(match.MatchId, 2, 0, MatchOperation.Attack, -1, 0),
                new MatchRequest(match.MatchId, 3, 0, MatchOperation.Redistribute, left: -1, right: 5),
                new MatchRequest(match.MatchId, 4, 0, MatchOperation.UseCard, cardId: 0),
                new MatchRequest(match.MatchId, 5, 0, MatchOperation.Surrender, target: 0),
                new MatchRequest(match.MatchId, 6, 0, (MatchOperation)99)
            };
            foreach (var request in invalid)
            {
                Assert.That(match.Submit(PlayerId.PlayerA, request, 100).Rejection,
                    Is.EqualTo(RequestRejection.InvalidPayload));
            }
            Assert.That(match.State.StateVersion, Is.Zero);
            Assert.That(match.Deadline, Is.EqualTo(130));
        }

        [Test]
        public void ClockMustBeFiniteAndMonotonic()
        {
            var match = Active();
            Assert.Throws<ArgumentOutOfRangeException>(() => match.Tick(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => match.Tick(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => match.Tick(99));
        }

        [Test]
        public void SuppliedEngineCannotMutateTheAuthorityAfterConstruction()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerA);
            var match = new MatchAuthority(Guid.NewGuid(), engine, 30, 100);
            engine.TryEnd(PlayerId.PlayerA, MatchEndReason.Surrender, 0);
            Assert.That(match.State.MatchPhase, Is.EqualTo(MatchPhase.Active));
        }
    }
}
