using System;
using System.Collections.Generic;
using System.Linq;
using Waribasi.Rules;

namespace Waribasi.Npc
{
    public static class StrongestNpc
    {
        public static NpcSearchOptions CreateOptions()
        {
            return new NpcSearchOptions
            {
                HypothesisCount = 1, DrawSamples = 2, RootWidth = 12, ReplyWidth = 6,
                MaxApplications = 40000, MaxWorkItems = 200000,
                MaxMilliseconds = 600, SliceMilliseconds = 3, NearScore = 0
            };
        }

        //現在札の定義だけを受け取る。正式進行、相手の非公開状態、未来乱数は保持しない。
        public static AdvancedNpc CreateSearch(PlayerObservation observation, IEnumerable<CardDefinition> currentOpponentCards,
            RuleRandom thinkingRandom, NpcSearchOptions options = null, IReadOnlyList<string> recentBoards = null)
        {
            if (observation == null || currentOpponentCards == null)
            {
                throw new ArgumentNullException(observation == null ? nameof(observation) : nameof(currentOpponentCards));
            }
            var cards = currentOpponentCards.ToArray();
            PlayerId opponent = GameState.GetOpponent(observation.Player);
            bool revivalUsed = observation.PublicState.CardHistory.Any(record =>
                record.Player == opponent && record.Card.Kind == CardKind.Revival);
            if (cards.Length != observation.PublicState.GetCardCount(opponent) ||
                (revivalUsed && cards.Any(card => card != null && card.Kind == CardKind.Revival)))
            {
                throw new ArgumentException("Current cards must match the public count and revival history.");
            }
            var exact = new NpcHandHypothesis(cards, revivalUsed);
            return new AdvancedNpc(observation, thinkingRandom, options ?? CreateOptions(), recentBoards,
                hypotheses: new[] { exact });
        }
    }
}
