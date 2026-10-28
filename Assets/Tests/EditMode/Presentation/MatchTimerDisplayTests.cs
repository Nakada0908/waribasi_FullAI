using System;
using System.Linq;
using NUnit.Framework;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Presentation.Tests
{
    public sealed class MatchTimerDisplayTests
    {
        private static object Display(string method, params object[] arguments)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Waribasi.Presentation.MatchHudDisplay"))
                .First(value => value != null);
            return type.GetMethod(method).Invoke(null, arguments);
        }

        [TestCase(30)]
        [TestCase(60)]
        public void FormalTimeoutAndDisplayedZeroUseTheSameTimestamp(int seconds)
        {
            var authority = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1),
                PlayerId.PlayerA), seconds, 5000);
            var message = MatchMessages.Public(authority, 0, 7, 3, 5000);
            Assert.That(Display("Timer", message, 5000d), Is.EqualTo(seconds + "秒"));
            double deadline = 5000 + seconds;
            Assert.That(authority.Tick(deadline - 0.001), Is.False);
            Assert.That(Display("Timer", message, deadline - 0.001), Is.EqualTo("! 1秒"));
            Assert.That(authority.Tick(deadline), Is.True);
            Assert.That(authority.State.EndReason, Is.EqualTo(MatchEndReason.Timeout));
            Assert.That(Display("Timer", message, deadline), Is.EqualTo("! 0秒"));
            Assert.That(Display("Timer", message, deadline + 0.001), Is.EqualTo("! 0秒"));
        }

        [TestCase(30)]
        [TestCase(60)]
        public void ReceivedSnapshotAddsElapsedTimeOnceAcrossCardUpdates(int seconds)
        {
            var authority = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1),
                PlayerId.PlayerA, playerACards: new[] { new CardDefinition(CardKind.Defense) }), seconds, 5000);
            Assert.That(authority.Submit(PlayerId.PlayerA, new MatchRequest(authority.MatchId, 1, 0, MatchOperation.UseCard,
                target: 0, cardId: 1), 5010).Accepted, Is.True);
            var message = MatchMessages.Public(authority, 0, 7, 3, 5010);
            double estimated = (double)Display("EstimatedServerTime", message.serverTime, 100d, 105d);
            Assert.That(estimated, Is.EqualTo(5015));
            Assert.That(authority.Deadline, Is.EqualTo(5000 + seconds));
            Assert.That(Display("Timer", message, estimated), Is.EqualTo((seconds - 15) + "秒"));
        }
    }
}
