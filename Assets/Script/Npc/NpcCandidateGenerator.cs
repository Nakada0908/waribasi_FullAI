using System.Collections.Generic;
using System.Linq;
using Waribasi.Rules;

namespace Waribasi.Npc
{
    public sealed class NpcCandidate
    {
        public UseCardAction Card { get; }
        public GameAction Normal { get; }
        public RuleEngine Position { get; }
        public GameAction FirstAction => Card ?? Normal;
        public string Key => NpcSimulation.ActionKey(Card) + "/" + NpcSimulation.ActionKey(Normal);

        internal NpcCandidate(UseCardAction card, GameAction normal, RuleEngine position)
        {
            Card = card;
            Normal = normal;
            Position = position;
        }
    }

    public static class NpcCandidateGenerator
    {
        //追加:観測から作った仮想局面だけを内部で受け取り、カード適用も予算に数えて逐次列挙する。
        internal static IEnumerable<NpcCandidate> EnumerateSimulation(RuleEngine engine, PlayerId player,
            NpcSearchBudget budget)
        {
            foreach (var candidate in EnumerateNormal(engine, player, null, budget))
            {
                yield return candidate;
            }
            foreach (var card in engine.GetLegalActions(player).OfType<UseCardAction>())
            {
                //変更:同期列挙も同じ経路を使い、分割探索のときだけ予算を消費する。
                //削除:同期列挙では予算を渡さない。
                //if (!budget.Spend(true))
                if (budget != null && !budget.Spend(true))
                {
                    yield break;
                }
                var position = engine.Fork();
                bool accepted = position.TryApply(player, card, position.Snapshot.StateVersion).Accepted;
                yield return null;
                if (accepted)
                {
                    foreach (var candidate in EnumerateNormal(position, player, card, budget))
                    {
                        yield return candidate;
                    }
                }
            }
        }

        //追加:1回の列挙で通常行動を1件だけ仮想適用する。
        private static IEnumerable<NpcCandidate> EnumerateNormal(RuleEngine engine, PlayerId player,
            UseCardAction card, NpcSearchBudget budget)
        {
            foreach (var normal in engine.GetLegalActions(player).Where(action => !(action is UseCardAction)))
            {
                //変更:同期・分割探索の適用条件を共通化する。
                //削除:同期列挙では予算を渡さない。
                //if (!budget.Spend(true))
                if (budget != null && !budget.Spend(true))
                {
                    yield break;
                }
                var position = engine.Fork();
                if (position.TryApply(player, normal, position.Snapshot.StateVersion).Accepted)
                {
                    //変更:分割探索の統計だけを更新する。
                    //削除:同期列挙では統計を保持しない。
                    //budget.Statistics.Candidates++;
                    if (budget != null)
                    {
                        budget.Statistics.Candidates++;
                    }
                    yield return new NpcCandidate(card, normal, position);
                }
            }
        }

        public static IReadOnlyList<NpcCandidate> Generate(PlayerObservation observation, ulong simulationSeed)
        {
            var engine = NpcSimulation.Create(observation, simulationSeed);
            //変更:中級と上級・最強で、カード＋通常行動の生成処理を共有する。
            return EnumerateSimulation(engine, observation.Player, null)
                .Where(candidate => candidate != null).ToList().AsReadOnly();
        }

        //削除:同期列挙専用だった処理は参照用に保存する。
        //var candidates = new List<NpcCandidate>();
        //AddNormal(engine, observation.Player, null, candidates);
        //foreach (var card in engine.GetLegalActions(observation.Player).OfType<UseCardAction>())
        //{
        //    var position = engine.Fork();
        //    if (position.TryApply(observation.Player, card, position.Snapshot.StateVersion).Accepted)
        //    {
        //        AddNormal(position, observation.Player, card, candidates);
        //    }
        //}
        //return candidates.AsReadOnly();
        //private static void AddNormal(RuleEngine engine, PlayerId player, UseCardAction card, List<NpcCandidate> candidates)
        //{
        //    foreach (var normal in engine.GetLegalActions(player).Where(action => !(action is UseCardAction)))
        //    {
        //        var position = engine.Fork();
        //        if (position.TryApply(player, normal, position.Snapshot.StateVersion).Accepted)
        //        {
        //            candidates.Add(new NpcCandidate(card, normal, position));
        //        }
        //    }
        //}
    }
}
