using System;
using System.Linq;
using NUnit.Framework;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Npc.Tests
{
    public sealed class AdvancedNpcTests
    {
        private static NpcSearchOptions Exact => new NpcSearchOptions
        {
            MaxMilliseconds = 0, RootWidth = 88, ReplyWidth = 88, DrawSamples = 1,
            MaxApplications = 200000, MaxWorkItems = 1000000, NearScore = 0
        };

        private static AdvancedNpc Solve(PlayerHands mine, PlayerHands theirs, CardDefinition[] cards = null,
            CardDefinition[] assumed = null, NpcSearchOptions options = null)
        {
            assumed = assumed ?? Array.Empty<CardDefinition>();
            var observation = RuleEngine.FromPosition(mine, theirs, PlayerId.PlayerA,
                playerACards: cards, playerBCards: assumed).GetObservation(PlayerId.PlayerA);
            var search = new AdvancedNpc(observation, new RuleRandom(7), options ?? Exact,
                hypotheses: new[] { new NpcHandHypothesis(assumed, false) });
            search.CompleteWithinBudget();
            return search;
        }

        [Test]
        public void ImmediateWinDoesNotSpendAnUnnecessaryCard()
        {
            using (var search = Solve(new PlayerHands(2, 1), new PlayerHands(3, 0),
                new[] { new CardDefinition(CardKind.DrawTwo) }))
            {
                Assert.That(search.Decision.Action, Is.TypeOf<AttackAction>());
                Assert.That(search.Decision.Evaluation.WinWeight, Is.EqualTo(1));
                Assert.That(search.Statistics.Stop, Is.EqualTo(NpcSearchStop.Completed));
            }
        }

        [Test]
        public void CardAndNormalActionAreOneTurnAndCardUseReturnsOnlyTheFirstAction()
        {
            var card = new CardDefinition(CardKind.SelfSingle, 2);
            using (var search = Solve(new PlayerHands(1, 1), new PlayerHands(3, 0), new[] { card }))
            {
                Assert.That(search.Decision.Action, Is.TypeOf<UseCardAction>());
                Assert.That(search.Decision.Evaluation.WinWeight, Is.EqualTo(1));
                var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(3, 0), PlayerId.PlayerA,
                    playerACards: new[] { card });
                engine.TryApply(PlayerId.PlayerA, search.Decision.Action, 0);
                using (var next = new AdvancedNpc(engine.GetObservation(PlayerId.PlayerA), new RuleRandom(7), Exact,
                    hypotheses: new[] { new NpcHandHypothesis(Array.Empty<CardDefinition>(), false) }))
                {
                    next.CompleteWithinBudget();
                    Assert.That(next.Decision.Action, Is.TypeOf<AttackAction>());
                    Assert.That(engine.TryApply(PlayerId.PlayerA, next.Decision.Action, 1).State.Winner, Is.EqualTo(PlayerId.PlayerA));
                }
            }
        }

        [Test]
        public void OpponentCardRepliesAreIncludedWhenAvoidingDefeat()
        {
            using (var search = Solve(new PlayerHands(1, 0), new PlayerHands(2, 2),
                new[] { new CardDefinition(CardKind.Defense) }, new[] { new CardDefinition(CardKind.SelfBoth, 4, 4) }))
            {
                Assert.That(search.Decision.Action, Is.TypeOf<UseCardAction>());
                Assert.That(search.Decision.Evaluation.LossWeight, Is.Zero);
                Assert.That(search.Statistics.CompletedDepth, Is.EqualTo(3));
            }
        }

        [Test]
        public void ThreeTurnSearchMatchesAnIndependentExhaustiveSmallTree()
        {
            int verified = 0;
            foreach (var mine in new[] { new PlayerHands(1, 0), new PlayerHands(2, 1), new PlayerHands(3, 2) })
            {
                foreach (var theirs in new[] { new PlayerHands(2, 0), new PlayerHands(3, 1), new PlayerHands(4, 2) })
                {
                    var engine = RuleEngine.FromPosition(mine, theirs, PlayerId.PlayerA);
                    using (var search = Solve(mine, theirs))
                    {
                        var after = engine.Fork();
                        after.TryApply(PlayerId.PlayerA, search.Decision.Action, 0);
                        int selected = TerminalOutcome(after, PlayerId.PlayerA, 2);
                        int best = engine.GetLegalActions(PlayerId.PlayerA).Max(action =>
                        {
                            var branch = engine.Fork();
                            branch.TryApply(PlayerId.PlayerA, action, 0);
                            return TerminalOutcome(branch, PlayerId.PlayerA, 2);
                        });
                        Assert.That(selected, Is.EqualTo(best), $"{mine.Left},{mine.Right}/{theirs.Left},{theirs.Right}");
                        Assert.That(search.Statistics.CompletedDepth, Is.EqualTo(search.Decision.Evaluation.WinWeight == 1 && search.Statistics.CompletedDeepScenarios == 0 ? 1 : 3));
                        verified++;
                    }
                }
            }
            Assert.That(verified, Is.EqualTo(9));
        }

        private static int TerminalOutcome(RuleEngine engine, PlayerId me, int remaining)
        {
            if (engine.Snapshot.Winner.HasValue)
            {
                return engine.Snapshot.Winner == me ? 1 : -1;
            }
            if (remaining == 0)
            {
                return 0;
            }
            var player = engine.Snapshot.CurrentPlayer;
            var values = engine.GetLegalActions(player).Select(action =>
            {
                var branch = engine.Fork();
                branch.TryApply(player, action, engine.Snapshot.StateVersion);
                return TerminalOutcome(branch, me, remaining - 1);
            });
            return player == me ? values.Max() : values.Min();
        }

        [Test]
        public void ThirdTurnWinIsFoundBeforeItIsAnImmediateWin()
        {
            for (int a = 0; a <= 4; a++)
            {
                for (int b = 0; b <= 4; b++)
                {
                    for (int c = 0; c <= 4; c++)
                    {
                        for (int d = 0; d <= 4; d++)
                        {
                            if (a + b == 0 || c + d == 0)
                            {
                                continue;
                            }
                            var engine = RuleEngine.FromPosition(new PlayerHands(a, b), new PlayerHands(c, d), PlayerId.PlayerA);
                            if (TerminalOutcome(engine, PlayerId.PlayerA, 1) != 0 || TerminalOutcome(engine, PlayerId.PlayerA, 3) != 1)
                            {
                                continue;
                            }
                            using (var search = Solve(new PlayerHands(a, b), new PlayerHands(c, d)))
                            {
                                Assert.That(search.Decision.Evaluation.WinWeight, Is.EqualTo(1));
                                Assert.That(search.Statistics.CompletedDepth, Is.EqualTo(3));
                                TestContext.WriteLine($"Three-turn winning board: {a},{b}/{c},{d}; action={NpcSimulation.ActionKey(search.Decision.Action)}");
                                return;
                            }
                        }
                    }
                }
            }
            Assert.Fail("No representative non-immediate three-turn win was found.");
        }

        [Test]
        public void LongPublicRefillHistoryIsChargedToTheWorkBudget()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerB,
                playerBCards: Enumerable.Repeat(new CardDefinition(CardKind.DrawTwo), 5),
                random: RuleRandom.FromSequence(Enumerable.Range(0, 1000).SelectMany(index => new[] { 85, 0 })));
            for (int turn = 0; turn < 1000; turn++)
            {
                var draw = engine.GetLegalActions(PlayerId.PlayerB).OfType<UseCardAction>().First();
                engine.TryApply(PlayerId.PlayerB, draw, engine.Snapshot.StateVersion);
                var normal = engine.GetLegalActions(PlayerId.PlayerB).OfType<RedistributeAction>().First();
                engine.TryApply(PlayerId.PlayerB, normal, engine.Snapshot.StateVersion);
                if (turn == 999)
                {
                    break;
                }
                var reply = engine.GetLegalActions(PlayerId.PlayerA).OfType<RedistributeAction>().First();
                engine.TryApply(PlayerId.PlayerA, reply, engine.Snapshot.StateVersion);
            }
            var observation = engine.GetObservation(PlayerId.PlayerA);
            using (var search = new AdvancedNpc(observation, new RuleRandom(3),
                new NpcSearchOptions { MaxMilliseconds = 0, MaxWorkItems = 16 }))
            {
                search.CompleteWithinBudget();
                Assert.That(search.Statistics.Stop, Is.EqualTo(NpcSearchStop.WorkLimit));
                Assert.That(search.Statistics.WorkItems, Is.EqualTo(16));
                Assert.That(engine.Validate(PlayerId.PlayerA, search.Decision.Action, engine.Snapshot.StateVersion), Is.EqualTo(ActionRejection.None));
            }
        }

        [Test]
        public void TinyApplicationBudgetReturnsALegalNormalFallback()
        {
            using (var search = Solve(new PlayerHands(1, 1), new PlayerHands(1, 1),
                options: new NpcSearchOptions { MaxMilliseconds = 0, MaxApplications = 1, DrawSamples = 1 }))
            {
                Assert.That(search.Statistics.Applications, Is.EqualTo(1));
                Assert.That(search.Statistics.Stop, Is.EqualTo(NpcSearchStop.ApplicationLimit));
                Assert.That(search.Statistics.Fallback, Is.True);
                Assert.That(search.Decision.Action, Is.Not.Null);
                Assert.That(search.Decision.Action, Is.Not.TypeOf<UseCardAction>());
            }
        }

        [Test]
        public void IncrementalWorkAndCancellationNeverReturnAStaleAction()
        {
            var engine = new RuleEngine(random: new RuleRandom(1));
            engine.TryDealInitialCards(0);
            engine.TryStart(1);
            using (var search = new AdvancedNpc(engine.GetObservation(PlayerId.PlayerA), new RuleRandom(3), Exact))
            {
                search.Advance(1, 100);
                Assert.That(search.IsComplete, Is.False);
                while (search.Statistics.Applications < 10 && !search.IsComplete)
                {
                    search.Advance(1, 100);
                }
                Assert.That(search.Statistics.Applications, Is.GreaterThanOrEqualTo(10));
                search.Dispose();
                Assert.That(search.Decision.Action, Is.Null);
                Assert.That(search.Statistics.Stop, Is.EqualTo(NpcSearchStop.Cancelled));
            }
        }

        [Test]
        public void IdenticalObservationAndBudgetIgnoreActualOpponentCardsAndOfficialFutureRandom()
        {
            var own = new[] { new CardDefinition(CardKind.DrawTwo), new CardDefinition(CardKind.Defense) };
            var first = RuleEngine.FromPosition(new PlayerHands(2, 1), new PlayerHands(3, 4), PlayerId.PlayerA,
                playerACards: own, playerBCards: Enumerable.Repeat(new CardDefinition(CardKind.Seal), 5), random: new RuleRandom(1));
            var second = RuleEngine.FromPosition(new PlayerHands(2, 1), new PlayerHands(3, 4), PlayerId.PlayerA,
                playerACards: own, playerBCards: Enumerable.Repeat(new CardDefinition(CardKind.Defense), 5), random: new RuleRandom(999));
            var options = new NpcSearchOptions { MaxMilliseconds = 0, MaxApplications = 15000 };
            using (var a = new AdvancedNpc(first.GetObservation(PlayerId.PlayerA), new RuleRandom(7), options))
            using (var b = new AdvancedNpc(second.GetObservation(PlayerId.PlayerA), new RuleRandom(7), options))
            {
                a.CompleteWithinBudget();
                b.CompleteWithinBudget();
                Assert.That(NpcSimulation.ActionKey(a.Decision.Action), Is.EqualTo(NpcSimulation.ActionKey(b.Decision.Action)));
                Assert.That(a.Statistics.Applications, Is.EqualTo(b.Statistics.Applications));
                Assert.That(a.Statistics.CompletedDeepScenarios, Is.EqualTo(b.Statistics.CompletedDeepScenarios));
                Assert.That(a.Decision.Evaluation.Score, Is.EqualTo(b.Decision.Evaluation.Score));
                Assert.That(first.Snapshot.StateVersion, Is.Zero);
                Assert.That(second.Snapshot.StateVersion, Is.Zero);
            }
        }

        [Test]
        public void InvalidHistoryFallsBackWithADiagnostic()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerA);
            using (var search = new AdvancedNpc(engine.GetObservation(PlayerId.PlayerA), new RuleRandom(7), Exact))
            {
                search.CompleteWithinBudget();
                Assert.That(search.Statistics.Stop, Is.EqualTo(NpcSearchStop.InvalidHypothesis));
                Assert.That(search.Statistics.Diagnostic, Is.Not.Null);
                Assert.That(engine.Validate(PlayerId.PlayerA, search.Decision.Action, 0), Is.EqualTo(ActionRejection.None));
            }
        }

        [Test]
        public void NearEqualChoicesVaryWithChoiceRandomAndRepeatWithAFixedSeed()
        {
            var engine = RuleEngine.FromPosition(new PlayerHands(2, 2), new PlayerHands(3, 0), PlayerId.PlayerA);
            var actions = Enumerable.Range(1, 30).Select(seed =>
            {
                using (var search = new AdvancedNpc(engine.GetObservation(PlayerId.PlayerA), new RuleRandom((ulong)seed), Exact,
                    hypotheses: new[] { new NpcHandHypothesis(Array.Empty<CardDefinition>(), false) }))
                {
                    search.CompleteWithinBudget();
                    return NpcSimulation.ActionKey(search.Decision.Action);
                }
            }).Distinct().ToArray();
            Assert.That(actions.Length, Is.GreaterThan(1));
        }

        [TestCase(NpcFirstPlayerChoice.Player)]
        [TestCase(NpcFirstPlayerChoice.Npc)]
        [TestCase(NpcFirstPlayerChoice.Random)]
        public void AdvancedSessionUsesTheSameUntimedRulesAndResetsOnRematch(NpcFirstPlayerChoice first)
        {
            var session = new LocalMatchSession(new NpcMatchSetup(NpcDifficulty.Advanced, first), PlayerId.PlayerB);
            session.Authority.DealInitialCards();
            session.Authority.Start(0);
            Assert.That(session.Authority.Deadline, Is.Null);
            var previous = session.Authority.MatchId;
            session.Surrender(session.Authority.State.StateVersion, 1);
            session.Restart(PlayerId.PlayerA);
            Assert.That(session.Authority.MatchId, Is.Not.EqualTo(previous));
            Assert.That(session.Authority.State.CardHistory, Is.Empty);
        }

        [Test]
        public void EveryLiveNormalBoardReturnsALegalAction()
        {
            int boards = 0;
            for (int a = 0; a <= 4; a++)
            {
                for (int b = 0; b <= 4; b++)
                {
                    for (int c = 0; c <= 4; c++)
                    {
                        for (int d = 0; d <= 4; d++)
                        {
                            if (a + b == 0 || c + d == 0)
                            {
                                continue;
                            }
                            using (var search = Solve(new PlayerHands(a, b), new PlayerHands(c, d)))
                            {
                                var engine = RuleEngine.FromPosition(new PlayerHands(a, b), new PlayerHands(c, d), PlayerId.PlayerA);
                                Assert.That(engine.Validate(PlayerId.PlayerA, search.Decision.Action, 0), Is.EqualTo(ActionRejection.None));
                                Assert.That(search.Statistics.Fallback, Is.False);
                                boards++;
                            }
                        }
                    }
                }
            }
            Assert.That(boards, Is.EqualTo(576));
        }

        [Test]
        public void WallBudgetIncludesTimeBetweenSlices()
        {
            var engine = new RuleEngine(random: new RuleRandom(1));
            engine.TryDealInitialCards(0);
            engine.TryStart(1);
            using (var search = new AdvancedNpc(engine.GetObservation(PlayerId.PlayerA), new RuleRandom(7),
                new NpcSearchOptions { MaxMilliseconds = 5 }))
            {
                search.Advance(1, 100);
                System.Threading.Thread.Sleep(10);
                search.Advance();
                Assert.That(search.IsComplete, Is.True);
                Assert.That(search.Statistics.Stop, Is.EqualTo(NpcSearchStop.TimeLimit));
                Assert.That(search.Decision.Action, Is.Not.Null);
            }
        }

        [Test]
        public void SearchDoesNotAdvanceTheOfficialFutureDraw()
        {
            var own = new[] { new CardDefinition(CardKind.DrawTwo) };
            var hidden = Enumerable.Repeat(new CardDefinition(CardKind.Seal), 5);
            var official = RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerA,
                playerACards: own, playerBCards: hidden, random: new RuleRandom(77));
            var control = official.Fork();
            using (var search = new AdvancedNpc(official.GetObservation(PlayerId.PlayerA), new RuleRandom(3),
                new NpcSearchOptions { MaxMilliseconds = 0, MaxApplications = 2000 }))
            {
                search.CompleteWithinBudget();
            }
            official.TryApply(PlayerId.PlayerA, new UseCardAction(1), 0);
            control.TryApply(PlayerId.PlayerA, new UseCardAction(1), 0);
            string Key(HandCard card) => $"{card.Id}/{card.Definition.Kind}/{card.Definition.First}/{card.Definition.Second}";
            Assert.That(official.GetObservation(PlayerId.PlayerA).OwnHand.Select(Key),
                Is.EqualTo(control.GetObservation(PlayerId.PlayerA).OwnHand.Select(Key)));
        }

        [Test]
        public void FixedWorkBudgetIsReproducibleAcrossDifferentSliceSizes()
        {
            var engine = new RuleEngine(random: new RuleRandom(2));
            engine.TryDealInitialCards(0);
            engine.TryStart(1);
            var options = new NpcSearchOptions { MaxMilliseconds = 0, MaxApplications = 3000 };
            using (var a = new AdvancedNpc(engine.GetObservation(PlayerId.PlayerA), new RuleRandom(7), options))
            using (var b = new AdvancedNpc(engine.GetObservation(PlayerId.PlayerA), new RuleRandom(7), options))
            {
                a.CompleteWithinBudget();
                while (!b.IsComplete)
                {
                    b.Advance(1, 100);
                }
                Assert.That(NpcSimulation.ActionKey(a.Decision.Action), Is.EqualTo(NpcSimulation.ActionKey(b.Decision.Action)));
                Assert.That(a.Statistics.CompletedDeepScenarios, Is.EqualTo(b.Statistics.CompletedDeepScenarios));
                Assert.That(a.Statistics.Applications, Is.EqualTo(b.Statistics.Applications));
            }
        }
    }
}
