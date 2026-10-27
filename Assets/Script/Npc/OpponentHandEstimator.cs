using System;
using System.Collections.Generic;
using System.Linq;
using Waribasi.Rules;

namespace Waribasi.Npc
{
    public sealed class NpcHandHypothesis
    {
        public IReadOnlyList<CardDefinition> Cards { get; }
        public bool RevivalAcquired { get; }
        public double Weight { get; }

        public NpcHandHypothesis(IEnumerable<CardDefinition> cards, bool revivalAcquired, double weight = 1)
        {
            var copy = new List<CardDefinition>(cards ?? throw new ArgumentNullException(nameof(cards)));
            if (copy.Count > 5 || copy.Any(card => card == null) ||
                copy.Count(card => card.Kind == CardKind.Revival) > 1 || weight <= 0 ||
                double.IsNaN(weight) || double.IsInfinity(weight))
            {
                throw new ArgumentException("Invalid hand hypothesis.");
            }
            Cards = copy.AsReadOnly();
            RevivalAcquired = revivalAcquired || copy.Any(card => card.Kind == CardKind.Revival);
            Weight = weight;
        }
    }

    public sealed class OpponentHandEstimator
    {
        private sealed class Slot
        {
            internal int Domain;
            internal CardDefinition Used;
        }

        public IReadOnlyList<NpcHandHypothesis> Hypotheses => hypotheses.AsReadOnly();
        public string Diagnostic { get; private set; }
        private readonly List<NpcHandHypothesis> hypotheses = new List<NpcHandHypothesis>();

        //公開された札の入手枠を条件付きで割り当てる近似。相手の不使用は所持の証拠にしない。
        public void Generate(PlayerObservation observation, int count, RuleRandom random)
        {
            var options = new NpcSearchOptions { HypothesisCount = count, MaxMilliseconds = 0 };
            var budget = new NpcSearchBudget(options.Copy());
            foreach (var tick in GenerateSteps(observation, count, random, budget))
            {
            }
        }

        internal IEnumerable<bool> GenerateSteps(PlayerObservation observation, int count,
            RuleRandom random, NpcSearchBudget budget)
        {
            if (observation == null || random == null || count < 1 || count > 64)
            {
                throw new ArgumentException("An observation, independent random and bounded count are required.");
            }
            hypotheses.Clear();
            var logWeights = new List<double>();
            Diagnostic = null;
            PlayerId opponent = GameState.GetOpponent(observation.Player);
            int attempts = 0;
            while (hypotheses.Count < count && attempts++ < count * 12)
            {
                var all = new List<Slot>();
                var live = new List<Slot>();
                for (int index = 0; index < 5; index++)
                {
                    var slot = new Slot { Domain = index < 2 ? index : 2 };
                    live.Add(slot);
                    all.Add(slot);
                }
                bool valid = observation.PublicState.InitialCardsDealt;
                bool usedRevival = false;
                double logWeight = 0;
                foreach (var record in observation.PublicState.CardHistory)
                {
                    if (!budget.Spend())
                    {
                        yield break;
                    }
                    yield return true;
                    if (record.Player != opponent)
                    {
                        continue;
                    }
                    var weights = live.Select(slot => Probability(record.Card, slot.Domain, false)).ToArray();
                    double total = weights.Sum();
                    bool revival = record.Card.Kind == CardKind.Revival;
                    if (total <= 0 || (revival && (usedRevival || !record.Automatic)) ||
                        (!revival && record.Automatic) || record.DrawnCount < 0 || record.DrawnCount > 3)
                    {
                        valid = false;
                        break;
                    }
                    double roll = random.Next(1000000, out random) / 1000000d * total;
                    int selected = weights.Length - 1;
                    for (int index = 0; index < weights.Length; index++)
                    {
                        roll -= weights[index];
                        if (roll < 0)
                        {
                            selected = index;
                            break;
                        }
                    }
                    var consumed = live[selected];
                    consumed.Used = record.Card;
                    live.RemoveAt(selected);
                    logWeight += Math.Log(total);
                    usedRevival |= revival;
                    int requested = record.RequestedDrawCount;
                    int expected = record.Card.IsDraw ? Math.Min(requested, 5 - live.Count) : 0;
                    if ((record.Card.IsDraw && (requested < 1 || requested > 3 ||
                        (record.Card.Kind == CardKind.DrawTwo && requested != 2) ||
                        (record.Card.Kind == CardKind.DrawThree && requested != 3))) ||
                        (!record.Card.IsDraw && requested != 0) || expected != record.DrawnCount)
                    {
                        valid = false;
                        break;
                    }
                    for (int drawn = 0; drawn < record.DrawnCount; drawn++)
                    {
                        var slot = new Slot { Domain = 2 };
                        live.Add(slot);
                        all.Add(slot);
                    }
                }
                if (!valid || live.Count != observation.PublicState.GetCardCount(opponent))
                {
                    Diagnostic = "Public history has no compatible acquisition assignment in this attempt.";
                    continue;
                }
                var cards = new List<CardDefinition>();
                bool acquired = false;
                foreach (var slot in all)
                {
                    if (!budget.Spend())
                    {
                        yield break;
                    }
                    CardDefinition card = slot.Used;
                    if (card == null)
                    {
                        card = slot.Domain < 2 ? CardDrawing.DrawNumber(ref random, slot.Domain == 0) :
                            CardDrawing.Draw(ref random, acquired || usedRevival);
                        cards.Add(card);
                    }
                    else if (acquired && card.Kind == CardKind.Revival)
                    {
                        valid = false;
                        break;
                    }
                    acquired |= card.Kind == CardKind.Revival;
                    yield return true;
                }
                if (valid)
                {
                    //共通倍率は無関係なので、長い履歴でのアンダーフローを避ける。
                    logWeights.Add(logWeight);
                    hypotheses.Add(new NpcHandHypothesis(cards, acquired));
                }
            }
            if (hypotheses.Count > 0)
            {
                double maximum = logWeights.Max();
                for (int index = 0; index < hypotheses.Count; index++)
                {
                    var prior = hypotheses[index];
                    hypotheses[index] = new NpcHandHypothesis(prior.Cards, prior.RevivalAcquired,
                        Math.Exp(Math.Max(-600, logWeights[index] - maximum)));
                }
                Diagnostic = null;
            }
        }

        private static double Probability(CardDefinition card, int domain, bool acquired)
        {
            if (domain < 2)
            {
                return card.IsNumber && card.IsSelf == (domain == 0) ? (card.IsBoth ? 0.05 : 0.125) : 0;
            }
            double ratio = acquired ? 100d / 97 : 1;
            if (card.IsNumber)
            {
                return (card.IsBoth ? 0.0125 : 0.03125) * ratio;
            }
            switch (card.Kind)
            {
                case CardKind.Defense: return 0.20 * ratio;
                case CardKind.Seal: return 0.15 * ratio;
                case CardKind.DrawTwo: return 0.06 * ratio;
                case CardKind.DrawThree: return 0.012 * ratio;
                case CardKind.DrawRandom: return 0.048 * ratio;
                case CardKind.Revival: return acquired ? 0 : 0.03;
                default: return 0;
            }
        }
    }
}
