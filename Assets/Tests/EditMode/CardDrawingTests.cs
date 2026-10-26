using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Waribasi.Rules.Tests
{
    public class CardDrawingTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void EveryFamilyRollHasExactSpecifiedWeight(bool acquired)
        {
            var counts = new Dictionary<CardKind, int>();
            int limit = acquired ? 97 : 100;
            for (int roll = 0; roll < limit; roll++)
            {
                var random = RuleRandom.FromSequence(new[] { roll, 0, 0, 0 });
                var card = CardDrawing.Draw(ref random, acquired);
                counts[card.Kind] = counts.TryGetValue(card.Kind, out int count) ? count + 1 : 1;
            }

            Assert.That(counts[CardKind.SelfSingle], Is.EqualTo(50));
            Assert.That(counts[CardKind.Defense], Is.EqualTo(20));
            Assert.That(counts[CardKind.Seal], Is.EqualTo(15));
            Assert.That(counts[CardKind.DrawTwo], Is.EqualTo(12));
            Assert.That(counts.ContainsKey(CardKind.Revival), Is.EqualTo(!acquired));
            if (!acquired)
            {
                Assert.That(counts[CardKind.Revival], Is.EqualTo(3));
            }
        }

        [Test]
        public void EveryRefillSubtypeRollHasExactSpecifiedWeight()
        {
            var kinds = new List<CardKind>();
            for (int subtype = 0; subtype < 100; subtype++)
            {
                var random = RuleRandom.FromSequence(new[] { 85, subtype });
                kinds.Add(CardDrawing.Draw(ref random, false).Kind);
            }

            Assert.That(kinds.Count(k => k == CardKind.DrawTwo), Is.EqualTo(50));
            Assert.That(kinds.Count(k => k == CardKind.DrawThree), Is.EqualTo(10));
            Assert.That(kinds.Count(k => k == CardKind.DrawRandom), Is.EqualTo(40));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EachUnorderedPairIsChosenOnceAndEachSingleValueOnce(bool self)
        {
            var pairs = new HashSet<string>();
            for (int index = 0; index < 10; index++)
            {
                var random = RuleRandom.FromSequence(new[] { 1, index });
                var card = CardDrawing.DrawNumber(ref random, self);
                Assert.That(card.Kind, Is.EqualTo(self ? CardKind.SelfBoth : CardKind.OpponentBoth));
                Assert.That(card.First, Is.InRange(1, 4));
                Assert.That(card.Second, Is.InRange(card.First, 4));
                Assert.That(pairs.Add(card.First + ":" + card.Second), Is.True);
            }

            Assert.That(pairs.Count, Is.EqualTo(10));
            for (int count = 1; count <= 4; count++)
            {
                var random = RuleRandom.FromSequence(new[] { 0, count - 1 });
                var card = CardDrawing.DrawNumber(ref random, self);
                Assert.That(card.Kind, Is.EqualTo(self ? CardKind.SelfSingle : CardKind.OpponentSingle));
                Assert.That(card.First, Is.EqualTo(count));
            }
        }

        [Test]
        public void BothTargetRollsAndBothScopeRollsAreIndependent()
        {
            var kinds = new HashSet<CardKind>();
            for (int target = 0; target < 2; target++)
            {
                for (int scope = 0; scope < 2; scope++)
                {
                    var random = RuleRandom.FromSequence(new[] { 0, target, scope, 0 });
                    kinds.Add(CardDrawing.Draw(ref random, false).Kind);
                }
            }

            Assert.That(kinds, Is.EquivalentTo(new[]
            {
                CardKind.SelfSingle, CardKind.OpponentSingle, CardKind.SelfBoth, CardKind.OpponentBoth
            }));
        }

        [Test]
        public void RandomRefillHasThreeEqualOutcomeIntervals()
        {
            for (int roll = 0; roll < 3; roll++)
            {
                var random = RuleRandom.FromSequence(new[] { roll });
                Assert.That(CardDrawing.RollDrawCount(ref random), Is.EqualTo(roll + 1));
            }
        }

        [Test]
        public void RandomStateAndScriptedInputRemainImmutable()
        {
            var input = new[] { 50 };
            var random = RuleRandom.FromSequence(input);
            input[0] = 0;
            Assert.That(CardDrawing.Draw(ref random, false).Kind, Is.EqualTo(CardKind.Defense));
            var first = new RuleRandom(1);
            int value = first.Next(97, out var after);
            Assert.That(first.Next(97, out var again), Is.EqualTo(value));
            Assert.That(after.Next(100, out _), Is.EqualTo(again.Next(100, out _)));
            Assert.Throws<ArgumentOutOfRangeException>(() => first.Next(0, out _));
            var invalid = RuleRandom.FromSequence(new[] { 100 });
            Assert.Throws<InvalidOperationException>(() => invalid.Next(100, out _));
        }

        [Test]
        public void SeededRandomStaysInRequestedRangesAcrossRepeatedDraws()
        {
            var random = new RuleRandom(ulong.MaxValue);
            foreach (int bound in new[] { 1, 2, 3, 4, 10, 97, 100, int.MaxValue })
            {
                for (int index = 0; index < 500; index++)
                {
                    int value = random.Next(bound, out random);
                    Assert.That(value, Is.InRange(0, bound - 1));
                }
            }
        }
    }
}
