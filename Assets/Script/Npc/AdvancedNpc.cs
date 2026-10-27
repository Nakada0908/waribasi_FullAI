using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Waribasi.Rules;

namespace Waribasi.Npc
{
    public sealed class AdvancedNpcEvaluation
    {
        public double Mean { get; internal set; }
        public double Worst { get; internal set; }
        public double WinWeight { get; internal set; }
        public double LossWeight { get; internal set; }
        public double Score { get; internal set; }
        public bool Advantaged { get; internal set; }
        internal int Rank => WinWeight >= 0.999999 ? 2 : LossWeight <= 0.000001 ? 1 : 0;
    }

    public sealed class AdvancedNpcDecision
    {
        public GameAction Action { get; }
        public AdvancedNpcEvaluation Evaluation { get; }
        public NpcSearchStatistics Statistics { get; }

        internal AdvancedNpcDecision(GameAction action, AdvancedNpcEvaluation evaluation, NpcSearchStatistics statistics)
        {
            Action = action;
            Evaluation = evaluation;
            Statistics = statistics;
        }
    }

    //観測から作った局面だけを保持し、1回のAdvanceで少量ずつ進める。
    public sealed class AdvancedNpc : IDisposable
    {
        private sealed class Root
        {
            internal GameAction Action;
            internal string Key;
            internal int CardCost;
            internal readonly List<RuleEngine> Positions = new List<RuleEngine>();
            internal readonly List<double> Scores = new List<double>();
        }

        private sealed class Choice
        {
            internal Root Root;
            internal AdvancedNpcEvaluation Evaluation;
        }

        private readonly PlayerObservation observation;
        private readonly NpcSearchOptions options;
        private readonly NpcSearchBudget budget;
        private readonly OpponentHandEstimator estimator;
        private readonly IReadOnlyList<NpcHandHypothesis> supplied;
        private readonly IReadOnlyList<string> recentBoards;
        private readonly PlayerId me;
        private readonly PlayerId opponent;
        private IEnumerator<bool> work;
        private RuleRandom simulationRandom;
        private RuleRandom choiceRandom;
        private GameAction fallback;
        private List<Choice> completedChoices;
        private bool finished;

        public bool IsComplete => finished;
        public AdvancedNpcDecision Decision { get; private set; }
        public NpcSearchStatistics Statistics => budget.Statistics;
        public RuleRandom ChoiceRandom => choiceRandom;

        //明示仮説は固定局面検証用。通常の接続は本人観測から推定する。
        public AdvancedNpc(PlayerObservation observation, RuleRandom random, NpcSearchOptions options = null,
            IReadOnlyList<string> recentBoards = null, OpponentHandEstimator estimator = null,
            IReadOnlyList<NpcHandHypothesis> hypotheses = null)
        {
            this.observation = observation ?? throw new ArgumentNullException(nameof(observation));
            choiceRandom = random ?? throw new ArgumentNullException(nameof(random));
            this.options = (options ?? new NpcSearchOptions()).Copy();
            this.recentBoards = recentBoards == null ? Array.Empty<string>() : recentBoards.ToArray();
            this.estimator = estimator ?? new OpponentHandEstimator();
            if (hypotheses != null && (hypotheses.Count < 1 || hypotheses.Count > 64 || hypotheses.Any(value => value == null)))
            {
                throw new ArgumentException("A bounded set of explicit hypotheses is required.");
            }
            supplied = hypotheses?.ToArray();
            me = observation.Player;
            opponent = GameState.GetOpponent(me);
            budget = new NpcSearchBudget(this.options);
            int seed = choiceRandom.Next(int.MaxValue, out choiceRandom);
            simulationRandom = new RuleRandom(unchecked((ulong)seed + 0xC4CEB9FE1A85EC53UL));
            work = Search().GetEnumerator();
        }

        public void Advance(int maxWorkItems = 256, double? sliceMilliseconds = null)
        {
            double slice = sliceMilliseconds ?? options.SliceMilliseconds;
            if (maxWorkItems < 1 || slice <= 0 || double.IsNaN(slice) || double.IsInfinity(slice))
            {
                throw new ArgumentOutOfRangeException(nameof(maxWorkItems));
            }
            if (finished)
            {
                return;
            }
            var timer = Stopwatch.StartNew();
            int items = 0;
            while (items++ < maxWorkItems && timer.Elapsed.TotalMilliseconds < slice && budget.Check())
            {
                if (!work.MoveNext())
                {
                    budget.Stop(NpcSearchStop.Completed);
                    break;
                }
            }
            if (budget.Stopped)
            {
                Finish();
            }
            timer.Stop();
            Statistics.CpuMilliseconds += timer.Elapsed.TotalMilliseconds;
            Statistics.MaximumSliceMilliseconds = Math.Max(Statistics.MaximumSliceMilliseconds, timer.Elapsed.TotalMilliseconds);
        }

        public void CompleteWithinBudget()
        {
            while (!IsComplete)
            {
                Advance();
            }
        }

        public void Dispose()
        {
            if (!finished)
            {
                budget.Stop(NpcSearchStop.Cancelled);
                Finish();
            }
        }

        private void Finish()
        {
            work.Dispose();
            work = null;
            finished = true;
            GameAction action = fallback;
            AdvancedNpcEvaluation evaluation = null;
            if (completedChoices != null && completedChoices.Count > 0)
            {
                var best = completedChoices.OrderByDescending(value => value.Evaluation.Rank)
                    .ThenByDescending(value => value.Evaluation.Score).ThenBy(value => value.Root.Key, StringComparer.Ordinal).First();
                var near = completedChoices.Where(value => value.Evaluation.Rank == best.Evaluation.Rank &&
                    Math.Abs(value.Evaluation.LossWeight - best.Evaluation.LossWeight) <= 0.000001 &&
                    Math.Abs(value.Evaluation.WinWeight - best.Evaluation.WinWeight) <= 0.000001 &&
                    best.Evaluation.Score - value.Evaluation.Score <= options.NearScore).ToArray();
                var selected = near[choiceRandom.Next(near.Length, out choiceRandom)];
                action = selected.Root.Action;
                evaluation = selected.Evaluation;
                Statistics.Fallback = false;
            }
            if (Statistics.Stop == NpcSearchStop.Cancelled)
            {
                action = null;
            }
            Decision = new AdvancedNpcDecision(action, evaluation, Statistics);
        }

        private IEnumerable<bool> Search()
        {
            if (observation.PublicState.MatchPhase != MatchPhase.Active || observation.PublicState.CurrentPlayer != me)
            {
                Statistics.Diagnostic = "The observation is not an active own turn.";
                yield break;
            }
            fallback = NpcSimulation.Create(observation, 1).GetLegalActions(me).FirstOrDefault(action => !(action is UseCardAction));
            yield return true;
            if (supplied == null)
            {
                foreach (var tick in estimator.GenerateSteps(observation, options.HypothesisCount, simulationRandom, budget))
                {
                    yield return tick;
                }
            }
            if (!budget.Check())
            {
                yield break;
            }
            var hypotheses = supplied ?? estimator.Hypotheses;
            Statistics.Hypotheses = hypotheses.Count;
            if (hypotheses.Count == 0)
            {
                Statistics.Diagnostic = estimator.Diagnostic;
                budget.Stop(NpcSearchStop.InvalidHypothesis);
                yield break;
            }
            var roots = new Dictionary<string, Root>(StringComparer.Ordinal);
            var weights = new List<double>();
            double initialPosition = 0;
            for (int sample = 0; sample < options.DrawSamples; sample++)
            {
                //仮説間にも共通の抽選条件を用い、実際の未来乱数とは共有しない。
                ulong seed = (ulong)simulationRandom.Next(int.MaxValue, out simulationRandom) + 104729UL;
                foreach (var hypothesis in hypotheses)
                {
                    RuleEngine position = null;
                    try
                    {
                        position = NpcSimulation.Create(observation, hypothesis.Cards, hypothesis.RevivalAcquired, new RuleRandom(seed));
                    }
                    catch (ArgumentException error)
                    {
                        Statistics.Diagnostic = error.Message;
                    }
                    if (position == null)
                    {
                        budget.Stop(NpcSearchStop.InvalidHypothesis);
                        yield break;
                    }
                    initialPosition += PositionValue(position) * hypothesis.Weight;
                    weights.Add(hypothesis.Weight);
                    foreach (var candidate in NpcCandidateGenerator.EnumerateSimulation(position, me, budget))
                    {
                        if (candidate != null)
                        {
                            if (!roots.TryGetValue(candidate.Key, out var root))
                            {
                                root = new Root { Action = candidate.FirstAction, Key = candidate.Key, CardCost = candidate.Card == null ? 0 : 1 };
                                roots.Add(root.Key, root);
                            }
                            root.Positions.Add(candidate.Position);
                            root.Scores.Add(PositionValue(candidate.Position));
                        }
                        yield return true;
                    }
                    if (!budget.Check())
                    {
                        yield break;
                    }
                    //途中でも、全初手を同じ仮説集合で比較し終えた結果を保持する。
                    Statistics.Scenarios = weights.Count;
                    completedChoices = EvaluateRoots(roots.Values.Where(root => root.Scores.Count == weights.Count),
                        weights, initialPosition >= 0);
                    Statistics.CompletedDepth = 1;
                    yield return true;
                }
            }
            Statistics.Scenarios = weights.Count;
            bool advantaged = initialPosition >= 0;
            var completeRoots = roots.Values.Where(root => root.Scores.Count == weights.Count).ToList();
            completedChoices = EvaluateRoots(completeRoots, weights, advantaged);
            Statistics.CompletedDepth = 1;
            var selectedRoots = completedChoices.OrderByDescending(value => value.Evaluation.Rank)
                .ThenByDescending(value => value.Evaluation.Score).ThenBy(value => value.Root.Key, StringComparer.Ordinal)
                .Take(options.RootWidth).Select(value => value.Root).ToArray();
            if (selectedRoots.Length == 0 || completedChoices.Any(value => value.Evaluation.WinWeight >= 0.999999))
            {
                yield break;
            }
            yield return true;
            for (int scenario = 0; scenario < weights.Count; scenario++)
            {
                var pending = new double[selectedRoots.Length];
                for (int rootIndex = 0; rootIndex < selectedRoots.Length; rootIndex++)
                {
                    var root = selectedRoots[rootIndex];
                    var position = root.Positions[scenario];
                    if (position.Snapshot.MatchPhase != MatchPhase.Active)
                    {
                        pending[rootIndex] = PositionValue(position);
                        continue;
                    }
                    var replies = new List<NpcCandidate>();
                    foreach (var reply in NpcCandidateGenerator.EnumerateSimulation(position, opponent, budget))
                    {
                        if (reply != null)
                        {
                            replies.Add(reply);
                        }
                        yield return true;
                    }
                    if (!budget.Check())
                    {
                        yield break;
                    }
                    var retained = replies.OrderBy(reply => PositionValue(reply.Position)).Take(options.ReplyWidth)
                        .Concat(replies.Where(reply => reply.Position.Snapshot.Winner == opponent)).Distinct().ToArray();
                    double worst = double.PositiveInfinity;
                    foreach (var reply in retained)
                    {
                        double best = double.NegativeInfinity;
                        if (reply.Position.Snapshot.MatchPhase != MatchPhase.Active)
                        {
                            best = PositionValue(reply.Position);
                        }
                        else
                        {
                            foreach (var next in NpcCandidateGenerator.EnumerateSimulation(reply.Position, me, budget))
                            {
                                if (next != null)
                                {
                                    best = Math.Max(best, PositionValue(next.Position));
                                }
                                yield return true;
                            }
                        }
                        if (!budget.Check())
                        {
                            yield break;
                        }
                        worst = Math.Min(worst, best);
                    }
                    pending[rootIndex] = double.IsInfinity(worst) ? PositionValue(position) : worst;
                }
                //全ての初手を同じ仮説・深さまで比較し終えてから結果へ反映する。
                for (int rootIndex = 0; rootIndex < selectedRoots.Length; rootIndex++)
                {
                    selectedRoots[rootIndex].Scores[scenario] = pending[rootIndex];
                }
                completedChoices = EvaluateRoots(selectedRoots, weights, advantaged);
                Statistics.CompletedDeepScenarios = scenario + 1;
                if (scenario + 1 == weights.Count)
                {
                    Statistics.CompletedDepth = 3;
                }
                yield return true;
            }
        }

        private List<Choice> EvaluateRoots(IEnumerable<Root> roots, IReadOnlyList<double> weights, bool advantaged)
        {
            double total = weights.Sum();
            var choices = new List<Choice>();
            foreach (var root in roots)
            {
                double mean = 0;
                double wins = 0;
                double losses = 0;
                double worst = root.Scores.Min();
                for (int index = 0; index < weights.Count; index++)
                {
                    mean += root.Scores[index] * weights[index] / total;
                    wins += root.Scores[index] >= 999999 ? weights[index] / total : 0;
                    losses += root.Scores[index] <= -999999 ? weights[index] / total : 0;
                }
                double score = mean * (advantaged ? 0.65 : 0.9) + worst * (advantaged ? 0.35 : 0.1);
                score -= root.CardCost * 8;
                choices.Add(new Choice { Root = root, Evaluation = new AdvancedNpcEvaluation
                {
                    Mean = mean, Worst = worst, WinWeight = wins, LossWeight = losses,
                    Score = score, Advantaged = advantaged
                } });
            }
            return choices;
        }

        private double PositionValue(RuleEngine position)
        {
            var state = position.Snapshot;
            if (state.Winner.HasValue)
            {
                return state.Winner == me ? 1000000 : -1000000;
            }
            double result = (NpcSimulation.Alive(state.GetHands(me)) - NpcSimulation.Alive(state.GetHands(opponent))) * 180;
            var mine = position.GetObservation(me).OwnHand;
            var theirs = position.GetObservation(opponent).OwnHand;
            result += (mine.Count - theirs.Count) * 12;
            result += (mine.Count(card => card.Definition.Kind == CardKind.Revival) -
                theirs.Count(card => card.Definition.Kind == CardKind.Revival)) * 90;
            foreach (var side in new[] { HandSide.Left, HandSide.Right })
            {
                var ownEffects = state.GetEffects(me).Get(side);
                var otherEffects = state.GetEffects(opponent).Get(side);
                result += (ownEffects.IsDefended ? 22 : 0) - (otherEffects.IsDefended ? 22 : 0);
                result += (otherEffects.IsSealed ? 28 : 0) - (ownEffects.IsSealed ? 28 : 0);
            }
            string key = NpcSimulation.BoardKey(state);
            result -= recentBoards.Count(value => value == key) * 4;
            return result;
        }
    }
}
