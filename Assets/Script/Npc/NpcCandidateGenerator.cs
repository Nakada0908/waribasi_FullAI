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
        public static IReadOnlyList<NpcCandidate> Generate(PlayerObservation observation, ulong simulationSeed)
        {
            var engine = NpcSimulation.Create(observation, simulationSeed);
            var candidates = new List<NpcCandidate>();
            AddNormal(engine, observation.Player, null, candidates);
            foreach (var card in engine.GetLegalActions(observation.Player).OfType<UseCardAction>())
            {
                var position = engine.Fork();
                if (position.TryApply(observation.Player, card, position.Snapshot.StateVersion).Accepted)
                {
                    AddNormal(position, observation.Player, card, candidates);
                }
            }
            return candidates.AsReadOnly();
        }

        private static void AddNormal(RuleEngine engine, PlayerId player, UseCardAction card, List<NpcCandidate> candidates)
        {
            foreach (var normal in engine.GetLegalActions(player).Where(action => !(action is UseCardAction)))
            {
                var position = engine.Fork();
                if (position.TryApply(player, normal, position.Snapshot.StateVersion).Accepted)
                {
                    candidates.Add(new NpcCandidate(card, normal, position));
                }
            }
        }
    }
}
