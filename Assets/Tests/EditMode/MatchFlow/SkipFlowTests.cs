using System;
using System.Linq;
using NUnit.Framework;
using Waribasi.Rules;

namespace Waribasi.MatchFlow.Tests
{
    public sealed class SkipFlowTests
    {
        private const PlayerId A = PlayerId.PlayerA;

        private static MatchAuthority Sealed(CardDefinition[] cards = null, TurnPhase phase = TurnPhase.CardOrNormalAction)
        {
            return new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(0, 4),
                new PlayerHands(2, 3), A, phase, playerACards: cards,
                playerAEffects: new PlayerEffects(default, new HandEffects(sealUntilTurn: 1)),
                random: new RuleRandom(123)), 30, 100);
        }

        private static MatchRequest Request(MatchAuthority match, ulong id, MatchOperation operation, long? version = null)
        {
            return new MatchRequest(match.MatchId, id, version ?? match.State.StateVersion, operation,
                cardId: operation == MatchOperation.UseCard ? 1 : 0);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NoOptionalCardAutomaticallySkipsEvenWhenLastCardIsAutomatic(bool revival)
        {
            var match = Sealed(revival ? new[] { new CardDefinition(CardKind.Revival) } :
                new[] { new CardDefinition(CardKind.SelfSingle, 2) });
            Assert.That(match.Tick(500), Is.True);
            Assert.That(match.State.CurrentPlayer, Is.EqualTo(PlayerId.PlayerB));
            Assert.That(match.State.MatchPhase, Is.EqualTo(MatchPhase.Active));
            Assert.That(match.State.StateVersion, Is.EqualTo(1));
            Assert.That(match.Gate, Is.EqualTo(InputGate.Presenting));
            Assert.That(match.Deadline, Is.Null);
            Assert.That(match.LastResult.Events.Any(e => e.Type == RuleEventType.TurnSkipped), Is.True);
            Assert.That(match.GetObservation(A).OwnHand.Count, Is.EqualTo(1));
            Assert.That(match.Tick(500.5), Is.False);
            Assert.That(match.Tick(501), Is.True);
            Assert.That(match.State.StateVersion, Is.EqualTo(1));
            Assert.That(match.Deadline, Is.EqualTo(531));
        }

        [Test]
        public void PlayableCardKeepsDecisionAndDeadlineUntilDeclinedAndDuplicateSkipIsReplayed()
        {
            var match = Sealed(new[] { new CardDefinition(CardKind.DrawTwo) });
            Assert.That(match.Tick(129), Is.False);
            Assert.That(match.Deadline, Is.EqualTo(130));
            var request = Request(match, 1, MatchOperation.Skip);
            var response = match.Submit(A, request, 129.5);
            Assert.That(response.Accepted, Is.True);
            var committed = match.State;
            Assert.That(match.GetObservation(A).OwnHand.Count, Is.EqualTo(1));
            Assert.That(match.Submit(A, request, 129.6).Replayed, Is.True);
            Assert.That(match.State, Is.SameAs(committed));
            Assert.That(match.Tick(130), Is.False);
            Assert.That(match.State.EndReason, Is.EqualTo(MatchEndReason.None));
            Assert.That(match.Tick(130.5), Is.True);
            Assert.That(match.Deadline, Is.EqualTo(160.5));
        }

        [Test]
        public void CardDecisionCanTimeOutButInvalidSkipDoesNotResetDeadline()
        {
            var match = Sealed(new[] { new CardDefinition(CardKind.DrawTwo) });
            Assert.That(match.Submit(A, Request(match, 1, MatchOperation.Skip, 99), 129).RuleRejection,
                Is.EqualTo(ActionRejection.StaleState));
            Assert.That(match.Deadline, Is.EqualTo(130));
            Assert.That(match.Submit(A, Request(match, 2, MatchOperation.Skip), 130).Accepted, Is.False);
            Assert.That(match.State.EndReason, Is.EqualTo(MatchEndReason.Timeout));
            Assert.That(match.LastResult.Events.Any(e => e.Type == RuleEventType.TurnSkipped), Is.False);
        }

        [Test]
        public void CardAndSkipArePublishedSeparatelyAndNeitherPresentationConsumesDecisionTime()
        {
            var match = Sealed(new[] { new CardDefinition(CardKind.DrawTwo) });
            Assert.That(match.Submit(A, Request(match, 1, MatchOperation.UseCard), 129.9).Accepted, Is.True);
            var cardMessage = MatchMessages.Public(match, 1, 2, 3, 129.9);
            Assert.That(cardMessage.events.Any(e => e.type == (int)RuleEventType.CardUsed), Is.True);
            Assert.That(cardMessage.events.Any(e => e.type == (int)RuleEventType.TurnSkipped), Is.False);
            Assert.That(match.State.CurrentPlayer, Is.EqualTo(A));
            Assert.That(match.Gate, Is.EqualTo(InputGate.Presenting));
            Assert.That(match.Deadline, Is.Null);
            Assert.That(match.Submit(A, Request(match, 2, MatchOperation.Skip), 130).Rejection,
                Is.EqualTo(RequestRejection.InputClosed));
            Assert.That(match.State.StateVersion, Is.EqualTo(1));
            var cards = match.GetObservation(A).OwnHand;
            Assert.That(match.Tick(500), Is.True);
            var skipMessage = MatchMessages.Public(match, 1, 2, 3, 500);
            Assert.That(skipMessage.events.Count(e => e.type == (int)RuleEventType.TurnSkipped), Is.EqualTo(1));
            Assert.That(skipMessage.events.Any(e => e.type == (int)RuleEventType.CardUsed), Is.False);
            Assert.That(match.State.StateVersion, Is.EqualTo(2));
            Assert.That(match.State.CardHistory.Count, Is.EqualTo(1));
            Assert.That(match.GetObservation(A).OwnHand, Is.SameAs(cards));
            Assert.That(match.State.PlayerAEffects.Right.IsSealed, Is.False);
            Assert.That(match.Deadline, Is.Null);
            Assert.That(match.Tick(500.5), Is.False);
            Assert.That(match.Tick(501), Is.True);
            Assert.That(match.Deadline, Is.EqualTo(531));
            Assert.That(match.LastResult, Is.Null);
        }

        [TestCase(MatchEndReason.Surrender)]
        [TestCase(MatchEndReason.Disconnected)]
        [TestCase(MatchEndReason.CommunicationInterrupted)]
        public void EndingDuringCardPresentationCancelsPendingSkipAndPreservesResult(MatchEndReason reason)
        {
            var match = Sealed(new[] { new CardDefinition(CardKind.DrawTwo) });
            match.Submit(A, Request(match, 1, MatchOperation.UseCard), 101);
            if (reason == MatchEndReason.Surrender)
            {
                Assert.That(match.Submit(A, Request(match, 2, MatchOperation.Surrender), 101.5).Accepted, Is.True);
            }
            else if (reason == MatchEndReason.Disconnected)
            {
                Assert.That(match.Disconnect(A, 101.5), Is.True);
            }
            else
            {
                Assert.That(match.Interrupt(101.5), Is.True);
            }
            var ended = match.State;
            Assert.That(match.Gate, Is.EqualTo(InputGate.Ended));
            Assert.That(match.Tick(500), Is.False);
            Assert.That(match.State, Is.SameAs(ended));
            Assert.That(match.State.StateVersion, Is.EqualTo(2));
        }

        [Test]
        public void NextSealedTurnAutomaticallySkipsAfterPriorActionPresentation()
        {
            var match = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(1, 1),
                new PlayerHands(0, 2), A, playerBEffects: new PlayerEffects(default, new HandEffects(2, 2))), 30, 100);
            var attack = new MatchRequest(match.MatchId, 1, 0, MatchOperation.Attack, 0, 1);
            Assert.That(match.Submit(A, attack, 100).Accepted, Is.True);
            Assert.That(match.State.CurrentPlayer, Is.EqualTo(PlayerId.PlayerB));
            Assert.That(match.State.StateVersion, Is.EqualTo(1));
            Assert.That(match.Tick(101), Is.True);
            Assert.That(match.State.CurrentPlayer, Is.EqualTo(A));
            Assert.That(match.State.StateVersion, Is.EqualTo(2));
            Assert.That(match.State.TurnNumber, Is.EqualTo(3));
            Assert.That(match.State.PlayerBEffects.Right.IsSealed, Is.False);
            Assert.That(match.Gate, Is.EqualTo(InputGate.Presenting));
            Assert.That(match.Tick(102), Is.True);
            Assert.That(match.Deadline, Is.EqualTo(132));
        }

        [Test]
        public void SkipPayloadMustBeEmptyAndSenderMatchVersionAreVerified()
        {
            var match = Sealed(new[] { new CardDefinition(CardKind.DrawTwo) });
            var request = Request(match, 1, MatchOperation.Skip);
            Assert.That(request.TryGetAction(out var action), Is.True);
            Assert.That(action, Is.TypeOf<SkipAction>());
            Assert.That(match.Submit((PlayerId)99, request, 100).Rejection, Is.EqualTo(RequestRejection.UnknownPlayer));
            Assert.That(match.Submit(A, new MatchRequest(Guid.NewGuid(), 1, 0, MatchOperation.Skip), 100).Rejection,
                Is.EqualTo(RequestRejection.WrongMatch));
            Assert.That(match.Submit(PlayerId.PlayerB, request, 100).RuleRejection, Is.EqualTo(ActionRejection.NotYourTurn));
            Assert.That(match.Submit(A, new MatchRequest(match.MatchId, 1, 0, MatchOperation.Skip, target: 0), 100).Rejection,
                Is.EqualTo(RequestRejection.InvalidPayload));
            Assert.That(match.State.StateVersion, Is.Zero);
            Assert.That(match.Submit(A, Request(match, 2, MatchOperation.Skip), 100).Accepted, Is.True);
        }
    }
}
