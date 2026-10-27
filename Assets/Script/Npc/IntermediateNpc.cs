using System;
using System.Collections.Generic;
using System.Linq;
using Waribasi.Rules;

namespace Waribasi.Npc
{
    public sealed class NpcEvaluation : IComparable<NpcEvaluation>
    {
        public int Finish { get; internal set; }
        public int FatalReplies { get; internal set; }
        public double RevivalCost { get; internal set; }
        public double Position { get; internal set; }
        public int CardCost { get; internal set; }
        public int Repetitions { get; internal set; }

        public int CompareTo(NpcEvaluation other)
        {
            int order = Finish.CompareTo(other.Finish);
            if (order == 0)
            {
                order = other.FatalReplies.CompareTo(FatalReplies);
            }
            if (order == 0)
            {
                order = other.RevivalCost.CompareTo(RevivalCost);
            }
            if (order == 0)
            {
                order = Position.CompareTo(other.Position);
            }
            if (order == 0)
            {
                order = other.CardCost.CompareTo(CardCost);
            }
            if (order == 0)
            {
                order = other.Repetitions.CompareTo(Repetitions);
            }
            return order;
        }
    }

    public sealed class NpcDecision
    {
        public GameAction Action { get; }
        public NpcEvaluation Evaluation { get; }
        public int CandidateCount { get; }

        internal NpcDecision(GameAction action, NpcEvaluation evaluation, int candidateCount)
        {
            Action = action;
            Evaluation = evaluation;
            CandidateCount = candidateCount;
        }
    }

    public static class IntermediateNpc
    {
        //実抽選から独立した共通サンプルで、補充候補間の比較条件を揃える。
        public static NpcDecision Choose(PlayerObservation observation, ref RuleRandom choiceRandom,
            IReadOnlyList<string> recentBoards = null)
        {
            var candidates = new List<NpcCandidate>();
            for (ulong sample = 1; sample <= 3; sample++)
            {
                candidates.AddRange(NpcCandidateGenerator.Generate(observation, sample * 7919));
            }
            NpcEvaluation best = null;
            var choices = new List<NpcCandidate>();
            foreach (var group in candidates.GroupBy(candidate => candidate.Key))
            {
                var scores = group.Select(candidate => Evaluate(observation, candidate, recentBoards)).ToArray();
                var score = new NpcEvaluation
                {
                    Finish = scores.Min(value => value.Finish),
                    FatalReplies = scores.Max(value => value.FatalReplies),
                    RevivalCost = scores.Average(value => value.RevivalCost),
                    Position = scores.Average(value => value.Position),
                    CardCost = scores[0].CardCost,
                    Repetitions = scores[0].Repetitions
                };
                int comparison = best == null ? 1 : score.CompareTo(best);
                if (comparison > 0)
                {
                    best = score;
                    choices.Clear();
                }
                if (comparison >= 0)
                {
                    choices.Add(group.First());
                }
            }
            return choices.Count == 0 ? new NpcDecision(null, null, 0) :
                new NpcDecision(choices[choiceRandom.Next(choices.Count, out choiceRandom)].FirstAction, best, candidates.Count);
        }

        public static NpcEvaluation Evaluate(PlayerObservation observation, NpcCandidate candidate,
            IReadOnlyList<string> recentBoards = null)
        {
            PlayerId me = observation.Player;
            PlayerId opponent = GameState.GetOpponent(me);
            var state = candidate.Position.Snapshot;
            int alive = NpcSimulation.Alive(state.GetHands(me));
            int worstAlive = alive;
            bool revival = candidate.Position.GetObservation(me).OwnHand.Any(card => card.Definition.Kind == CardKind.Revival);
            bool loses = false;
            bool consumesRevival = false;
            if (state.MatchPhase == MatchPhase.Active)
            {
                foreach (var attack in candidate.Position.GetLegalActions(opponent).OfType<AttackAction>())
                {
                    var position = candidate.Position.Fork();
                    var response = position.TryApply(opponent, attack, state.StateVersion);
                    loses |= response.State.Winner == opponent;
                    consumesRevival |= response.Events.Any(value => value.Type == RuleEventType.CardUsed &&
                        value.Player == me && value.CardUse != null && value.CardUse.Automatic);
                    worstAlive = Math.Min(worstAlive, NpcSimulation.Alive(response.State.GetHands(me)));
                }
            }
            var used = candidate.Card == null ? null : observation.OwnHand.Single(card => card.Id == candidate.Card.CardId).Definition;
            int handGain = candidate.Position.GetObservation(me).OwnHand.Count - observation.OwnHand.Count;
            double supply = state.MatchPhase == MatchPhase.Active && used != null && used.IsDraw &&
                observation.OwnHand.Count <= 2 ? handGain * 20 : 0;
            return new NpcEvaluation
            {
                Finish = state.Winner == me ? 1 : 0,
                FatalReplies = loses ? 1 : 0,
                RevivalCost = revival && consumesRevival ? 1 : 0,
                Position = alive * 100 - NpcSimulation.Alive(state.GetHands(opponent)) * 130 + worstAlive * 70 + supply,
                CardCost = candidate.Card == null ? 0 : 1,
                Repetitions = recentBoards == null ? 0 : recentBoards.Count(key => key == NpcSimulation.BoardKey(state))
            };
        }
    }
}
