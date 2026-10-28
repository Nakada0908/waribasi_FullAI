using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Presentation.Tests
{
    public sealed class SkipPresentationTests
    {
        private static string Text(PublicMatchMessage state, int localPlayer)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Waribasi.Presentation.MatchText"))
                .First(value => value != null);
            return (string)type.GetMethod("Presentation", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { state, localPlayer });
        }

        [TestCase(0)]
        [TestCase(1)]
        public void CardResultIsShownBeforeSeparateSkipAndDisappearsWhenInputReopens(int localPlayer)
        {
            var match = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(0, 4),
                new PlayerHands(2, 3), PlayerId.PlayerA, playerACards: new[] { new CardDefinition(CardKind.DrawTwo) },
                playerAEffects: new PlayerEffects(default, new HandEffects(0, 1)), random: new RuleRandom(1)), 30, 100);
            Assert.That(match.Submit(PlayerId.PlayerA,
                new MatchRequest(match.MatchId, 1, 0, MatchOperation.UseCard, cardId: 1), 129.9).Accepted, Is.True);
            var card = MatchMessages.Public(match, 0, 7, 3, 129.9);
            Assert.That(Text(card, localPlayer), Does.Contain("2枚補充"));
            Assert.That(Text(card, localPlayer), Does.Contain("実際に2枚補充"));
            Assert.That(Text(card, localPlayer), Does.Not.Contain("スキップ"));
            //変更:カード後の独立した1.5秒を満たしてからスキップへ進む。
            //削除:match.Tick(131);
            match.Tick(131.4);
            var skip = MatchMessages.Public(match, 0, 7, 3, 131.4);
            Assert.That(Text(skip, localPlayer), Is.EqualTo((localPlayer == 0 ? "自分：" : "相手：") + "スキップ！"));
            Assert.That(Text(card, localPlayer), Does.Contain("2枚補充"));
            //変更:スキップ自体の1秒はカード表示と別に維持する。
            //削除:match.Tick(132);
            match.Tick(132.4);
            Assert.That(Text(MatchMessages.Public(match, 0, 7, 3, 132.4), localPlayer), Is.Null);
        }

        [Test]
        public void SkipPresentationUsesEventActorInsteadOfAlreadyChangedCurrentPlayer()
        {
            var state = new PublicMatchMessage
            {
                gate = (int)InputGate.Presenting, currentPlayer = 0,
                events = new[] { new RuleEventMessage { type = (int)RuleEventType.TurnSkipped, player = 1 } }
            };
            Assert.That(Text(state, 1), Is.EqualTo("自分：スキップ！"));
            Assert.That(Text(state, 0), Is.EqualTo("相手：スキップ！"));
            state.gate = (int)InputGate.Accepting;
            Assert.That(Text(state, 1), Is.Null);
        }
    }
}
