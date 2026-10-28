using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Npc.Tests
{
    public sealed class SkipNpcTests
    {
        private static RuleEngine Sealed(PlayerId actor, HandSide side, bool withCard)
        {
            var hands = side == HandSide.Left ? new PlayerHands(4, 0) : new PlayerHands(0, 4);
            var effects = side == HandSide.Left ? new PlayerEffects(new HandEffects(0, 1), default) :
                new PlayerEffects(default, new HandEffects(0, 1));
            var cards = withCard ? new[] { new CardDefinition(CardKind.OpponentBoth, 1, 4) } : null;
            return RuleEngine.FromPosition(actor == PlayerId.PlayerA ? hands : new PlayerHands(2, 3),
                actor == PlayerId.PlayerA ? new PlayerHands(2, 3) : hands, actor,
                playerACards: actor == PlayerId.PlayerA ? cards : null,
                playerBCards: actor == PlayerId.PlayerB ? cards : null,
                playerAEffects: actor == PlayerId.PlayerA ? effects : default,
                playerBEffects: actor == PlayerId.PlayerB ? effects : default);
        }

        private static GameAction Choose(NpcDifficulty difficulty, PlayerObservation observation)
        {
            var random = new RuleRandom(7);
            if (difficulty == NpcDifficulty.Beginner)
            {
                return BeginnerNpc.Choose(observation, ref random, 100);
            }
            if (difficulty == NpcDifficulty.Intermediate)
            {
                return IntermediateNpc.Choose(observation, ref random).Action;
            }
            var options = new NpcSearchOptions
            {
                MaxMilliseconds = 0, MaxApplications = 5000, MaxWorkItems = 50000,
                HypothesisCount = 1, DrawSamples = 1, RootWidth = 4, ReplyWidth = 2
            };
            using (var search = difficulty == NpcDifficulty.Strongest
                ? StrongestNpc.CreateSearch(observation, Array.Empty<CardDefinition>(), random, options)
                : new AdvancedNpc(observation, random, options,
                    hypotheses: new[] { new NpcHandHypothesis(Array.Empty<CardDefinition>(), false) }))
            {
                search.CompleteWithinBudget();
                Assert.That(search.Statistics.Candidates, Is.GreaterThan(0));
                return search.Decision.Action;
            }
        }

        [TestCase(NpcDifficulty.Beginner)]
        [TestCase(NpcDifficulty.Intermediate)]
        [TestCase(NpcDifficulty.Advanced)]
        [TestCase(NpcDifficulty.Strongest)]
        public void AllDifficultiesReturnLegalActionsBeforeAndAfterCardAndWithNoCard(NpcDifficulty difficulty)
        {
            foreach (PlayerId actor in new[] { PlayerId.PlayerA, PlayerId.PlayerB })
            {
                foreach (HandSide side in new[] { HandSide.Left, HandSide.Right })
                {
                    var engine = Sealed(actor, side, true);
                    var before = engine.Snapshot;
                    var first = Choose(difficulty, engine.GetObservation(actor));
                    Assert.That(first, Is.Not.Null);
                    Assert.That(engine.Validate(actor, first, 0), Is.EqualTo(ActionRejection.None));
                    Assert.That(engine.Snapshot, Is.SameAs(before));
                    Assert.That(engine.TryApply(actor, new UseCardAction(1), 0).Accepted, Is.True);
                    var skip = Choose(difficulty, engine.GetObservation(actor));
                    Assert.That(skip, Is.TypeOf<SkipAction>());
                    Assert.That(engine.TryApply(actor, skip, 1).Accepted, Is.True);
                    Assert.That(engine.Snapshot.CurrentPlayer, Is.EqualTo(GameState.GetOpponent(actor)));
                    Assert.That(engine.Snapshot.GetEffects(actor).Get(side).IsSealed, Is.False);
                    Assert.That(engine.Snapshot.CardHistory.Count, Is.EqualTo(1));
                    var empty = Sealed(actor, side, false);
                    Assert.That(Choose(difficulty, empty.GetObservation(actor)), Is.TypeOf<SkipAction>());
                }
            }
        }

        [Test]
        public void CardPlusSkipCandidatesFinishVirtualTurnAndKeepFormalStateUntouched()
        {
            var engine = Sealed(PlayerId.PlayerB, HandSide.Right, true);
            var before = engine.Snapshot;
            var candidates = NpcCandidateGenerator.Generate(engine.GetObservation(PlayerId.PlayerB), 3);
            Assert.That(candidates.Count, Is.GreaterThan(1));
            Assert.That(candidates.Any(c => c.Card == null), Is.True);
            Assert.That(candidates.Any(c => c.Card != null), Is.True);
            Assert.That(candidates.All(c => c.Normal is SkipAction), Is.True);
            Assert.That(candidates.All(c => c.Position.Snapshot.CurrentPlayer == PlayerId.PlayerA), Is.True);
            Assert.That(candidates.All(c => c.Position.Snapshot.TurnNumber == 2), Is.True);
            Assert.That(candidates.All(c => !c.Position.Snapshot.PlayerBEffects.Right.IsSealed), Is.True);
            Assert.That(candidates.Select(c => c.Key).Distinct().Count(), Is.EqualTo(candidates.Count));
            Assert.That(NpcSimulation.ActionKey(new SkipAction()), Is.Not.EqualTo(NpcSimulation.ActionKey(null)));
            Assert.That(engine.Snapshot, Is.SameAs(before));
        }

        [Test]
        public void LocalSessionMapsSkipToTheSameFormalRequestForEitherActor()
        {
            var session = new LocalMatchSession(new NpcMatchSetup(NpcDifficulty.Beginner, NpcFirstPlayerChoice.Player), PlayerId.PlayerA);
            var create = typeof(LocalMatchSession).GetMethod("Request", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var actor in new[] { session.Human, session.Npc })
            {
                var request = (MatchRequest)create.Invoke(session, new object[] { actor, new SkipAction(), 0L, false });
                Assert.That(request.Operation, Is.EqualTo(MatchOperation.Skip));
                Assert.That(request.MatchId, Is.EqualTo(session.Authority.MatchId));
                Assert.That(request.RequestId, Is.EqualTo(1));
                Assert.That(request.TryGetAction(out var action), Is.True);
                Assert.That(action, Is.TypeOf<SkipAction>());
            }
        }
    }
}
