using System;
using System.Collections.Generic;
using System.Linq;
using Waribasi.Rules;

namespace Waribasi.Npc
{
    public static class NpcSimulation
    {
        //中級では相手の任意カードを使わない。仮置き札は公開枚数を保つためだけに用いる。
        public static RuleEngine Create(PlayerObservation observation, ulong seed)
        {
            if (observation == null)
            {
                throw new ArgumentNullException(nameof(observation));
            }
            PlayerId opponent = GameState.GetOpponent(observation.Player);
            bool usedRevival = observation.PublicState.CardHistory.Any(record =>
                record.Player == opponent && record.Card.Kind == CardKind.Revival);
            var placeholders = Enumerable.Repeat(new CardDefinition(CardKind.SelfSingle, 1),
                observation.PublicState.GetCardCount(opponent));
            return RuleEngine.FromObservation(observation, placeholders, usedRevival, new RuleRandom(seed));
        }

        //T07は相手札の仮説を明示し、ここから独立した仮想局面を作れる。
        public static RuleEngine Create(PlayerObservation observation, IEnumerable<CardDefinition> opponentCards,
            bool opponentRevivalAcquired, RuleRandom simulationRandom)
        {
            return RuleEngine.FromObservation(observation, opponentCards, opponentRevivalAcquired, simulationRandom);
        }

        public static int Alive(PlayerHands hands)
        {
            return (hands.Left > 0 ? 1 : 0) + (hands.Right > 0 ? 1 : 0);
        }

        public static string BoardKey(GameState state)
        {
            return $"{state.PlayerA.Left},{state.PlayerA.Right},{state.PlayerB.Left},{state.PlayerB.Right}," +
                $"{state.CurrentPlayer},{state.PlayerAEffects.Left.IsDefended},{state.PlayerAEffects.Right.IsDefended}," +
                $"{state.PlayerBEffects.Left.IsDefended},{state.PlayerBEffects.Right.IsDefended}," +
                $"{state.PlayerAEffects.Left.IsSealed},{state.PlayerAEffects.Right.IsSealed}," +
                $"{state.PlayerBEffects.Left.IsSealed},{state.PlayerBEffects.Right.IsSealed}";
        }

        public static string ActionKey(GameAction action)
        {
            if (action is AttackAction attack)
            {
                return $"A{attack.Source}:{attack.Target}";
            }
            if (action is RedistributeAction distribution)
            {
                return $"R{distribution.Left}:{distribution.Right}";
            }
            if (action is UseCardAction card)
            {
                return $"C{card.CardId}:{card.Target}:{card.SwapValues}";
            }
            return "-";
        }
    }
}
