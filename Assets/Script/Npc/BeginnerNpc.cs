using System;
using System.Linq;
using Waribasi.Rules;

namespace Waribasi.Npc
{
    public static class BeginnerNpc
    {
        public static GameAction Choose(PlayerObservation observation, ref RuleRandom choiceRandom, int cardPercent = 25)
        {
            if (cardPercent < 0 || cardPercent > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(cardPercent));
            }
            var engine = NpcSimulation.Create(observation, 1);
            var actions = engine.GetLegalActions(observation.Player);
            var attacks = actions.OfType<AttackAction>().Where(attack =>
            {
                var result = engine.Fork().TryApply(observation.Player, attack, engine.Snapshot.StateVersion);
                return result.Events.Any(value => value.Type == RuleEventType.HandEliminated &&
                    value.Player == GameState.GetOpponent(observation.Player));
            }).Cast<GameAction>().ToArray();
            if (attacks.Length > 0)
            {
                return attacks[choiceRandom.Next(attacks.Length, out choiceRandom)];
            }
            var cards = actions.OfType<UseCardAction>().Cast<GameAction>().ToArray();
            var normal = actions.Where(action => !(action is UseCardAction)).ToArray();
            var pool = cards.Length > 0 && choiceRandom.Next(100, out choiceRandom) < cardPercent ? cards : normal;
            return pool.Length == 0 ? null : pool[choiceRandom.Next(pool.Length, out choiceRandom)];
        }
    }
}
