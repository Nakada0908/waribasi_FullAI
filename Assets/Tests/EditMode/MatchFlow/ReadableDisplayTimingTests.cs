using System;
using NUnit.Framework;
using Waribasi.Rules;

namespace Waribasi.MatchFlow.Tests
{
    public sealed class ReadableDisplayTimingTests
    {
        [TestCase(2.49, false)]
        [TestCase(2.5, true)]
        [TestCase(3, true)]
        public void MinimumStartsAtFirstReadableRender(double elapsed, bool complete)
        {
            var timing = new ReadableDisplayTiming();
            timing.Observe("match", 0, false, 0);
            timing.Observe("match", 0, false, 20);
            Assert.That(timing.IsComplete("match", 0, 100, 2.5), Is.False);
            timing.Observe("match", 0, true, 100);
            Assert.That(timing.IsComplete("match", 0, 100 + elapsed, 2.5), Is.EqualTo(complete));
        }

        [Test]
        public void RepeatedRendersOrSameResultAfterDisconnectDoNotRestartWait()
        {
            var timing = new ReadableDisplayTiming();
            timing.Observe("match", 4, true, 10);
            timing.Observe("match", 4, true, 12);
            Assert.That(timing.IsComplete("match", 4, 12.5, 2.5), Is.True);
            Assert.That(timing.IsComplete("other", 4, 100, 2.5), Is.False);
            Assert.That(timing.IsComplete("match", 5, 100, 2.5), Is.False);
        }

        [Test]
        public void DealingAndRematchRequireTheirOwnReadableStage()
        {
            var timing = new ReadableDisplayTiming();
            timing.Observe("first", 0, true, 0);
            Assert.That(timing.IsComplete("first", 0, 2.5, 2.5), Is.True);
            timing.Observe("first", 1, true, 2.5);
            Assert.That(timing.IsComplete("first", 1, 4.99, 2.5), Is.False);
            Assert.That(timing.IsComplete("first", 1, 5, 2.5), Is.True);
            timing.Observe("rematch", 0, true, 20);
            Assert.That(timing.IsComplete("rematch", 0, 20, 2.5), Is.False);
            Assert.That(timing.IsComplete("rematch", 0, 22.5, 2.5), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HiddenTextOrFadeRequiresReadableTimeAgain(bool explicitFade)
        {
            var timing = new ReadableDisplayTiming();
            timing.Observe("match", 0, true, 0);
            if (explicitFade)
            {
                timing.Invalidate();
            }
            else
            {
                timing.Observe("match", 0, false, 2);
            }
            Assert.That(timing.IsComplete("match", 0, 100, 2.5), Is.False);
            timing.Observe("match", 0, true, 100);
            Assert.That(timing.IsComplete("match", 0, 102.49, 2.5), Is.False);
            Assert.That(timing.IsComplete("match", 0, 102.5, 2.5), Is.True);
        }

        [Test]
        public void PreparationAndResultDurationsAreIndependentAndMovementRunsInParallel()
        {
            var preparation = new ReadableDisplayTiming();
            var result = new ReadableDisplayTiming();
            preparation.Observe("match", 0, true, 10);
            result.Observe("match", 8, true, 10);
            Assert.That(preparation.IsComplete("match", 0, 11, 1), Is.True);
            Assert.That(result.IsComplete("match", 8, 11, 2.5), Is.False);
            Assert.That(result.IsComplete("match", 8, 12.5, 2.5), Is.True);
            Assert.That(MatchAuthority.PresentationSeconds, Is.EqualTo(1));
        }

        [Test]
        public void ZeroDurationStillRequiresReadableRender()
        {
            var timing = new ReadableDisplayTiming();
            Assert.That(timing.IsComplete("match", 0, 10, 0), Is.False);
            timing.Observe("match", 0, true, 10);
            Assert.That(timing.IsComplete("match", 0, 10, 0), Is.True);
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(0)]
        public void LongPreparationHasNoDeadlineAndStartReceivesFullTurn(int turnSeconds)
        {
            var authority = new MatchAuthority(Guid.NewGuid(), PlayerId.PlayerA, turnSeconds);
            var timing = new ReadableDisplayTiming();
            string match = authority.MatchId.ToString("N");
            timing.Observe(match, authority.State.StateVersion, false, 0);
            Assert.That(authority.Deadline, Is.Null);
            timing.Observe(match, authority.State.StateVersion, true, 100);
            Assert.That(timing.IsComplete(match, authority.State.StateVersion, 102.5, 2.5), Is.True);
            Assert.That(authority.DealInitialCards(), Is.True);
            timing.Observe(match, authority.State.StateVersion, true, 102.5);
            Assert.That(authority.Gate, Is.EqualTo(InputGate.Preparing));
            Assert.That(authority.Deadline, Is.Null);
            Assert.That(timing.IsComplete(match, authority.State.StateVersion, 105, 2.5), Is.True);
            Assert.That(authority.Start(105), Is.True);
            Assert.That(authority.Gate, Is.EqualTo(InputGate.Accepting));
            Assert.That(authority.Deadline, Is.EqualTo(turnSeconds == 0 ? (double?)null : 105 + turnSeconds));
        }
    }
}
