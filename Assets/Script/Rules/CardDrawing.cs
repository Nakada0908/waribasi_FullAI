using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Waribasi.Rules
{
    //乱数の状態自体も不変にし、正式な確定前に共有状態を進めない。
    public sealed class RuleRandom
    {
        private readonly ulong seed;
        private readonly IReadOnlyList<int> sequence;
        private readonly int index;

        public RuleRandom(ulong seed)
        {
            this.seed = seed;
        }

        private RuleRandom(IReadOnlyList<int> sequence, int index)
        {
            this.sequence = sequence;
            this.index = index;
        }

        public static RuleRandom Create()
        {
            var bytes = new byte[8];
            using (var generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }

            return new RuleRandom(BitConverter.ToUInt64(bytes, 0));
        }

        //各要素は、その呼出しで要求される上限未満の値を指定する。
        public static RuleRandom FromSequence(IEnumerable<int> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            return new RuleRandom(new List<int>(values).AsReadOnly(), 0);
        }

        public int Next(int exclusiveMax, out RuleRandom next)
        {
            if (exclusiveMax <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            }

            if (sequence != null)
            {
                if (index >= sequence.Count || sequence[index] < 0 || sequence[index] >= exclusiveMax)
                {
                    throw new InvalidOperationException("Scripted random sequence is exhausted or out of range.");
                }

                next = new RuleRandom(sequence, index + 1);
                return sequence[index];
            }

            ulong value;
            ulong cursor = seed;
            ulong bound = (ulong)exclusiveMax;
            ulong threshold = unchecked(0UL - bound) % bound;
            do
            {
                cursor = unchecked(cursor + 0x9E3779B97F4A7C15UL);
                value = cursor;
                value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
                value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
                value ^= value >> 31;
            }
            while (value < threshold);

            next = new RuleRandom(cursor);
            return (int)(value % bound);
        }
    }

    public static class CardDrawing
    {
        public static CardDefinition Draw(ref RuleRandom random, bool revivalAcquired)
        {
            int roll = Take(ref random, revivalAcquired ? 97 : 100);
            if (roll < 50)
            {
                bool self = Take(ref random, 2) == 0;
                return DrawNumber(ref random, self);
            }

            if (roll < 70)
            {
                return new CardDefinition(CardKind.Defense);
            }

            if (roll < 85)
            {
                return new CardDefinition(CardKind.Seal);
            }

            if (roll < 97)
            {
                int subtype = Take(ref random, 100);
                return new CardDefinition(subtype < 50 ? CardKind.DrawTwo :
                    subtype < 60 ? CardKind.DrawThree : CardKind.DrawRandom);
            }

            return new CardDefinition(CardKind.Revival);
        }

        public static CardDefinition DrawNumber(ref RuleRandom random, bool self)
        {
            if (Take(ref random, 2) == 0)
            {
                return new CardDefinition(self ? CardKind.SelfSingle : CardKind.OpponentSingle,
                    Take(ref random, 4) + 1);
            }

            int pair = Take(ref random, 10);
            for (int first = 1; first <= 4; first++)
            {
                for (int second = first; second <= 4; second++)
                {
                    if (pair-- == 0)
                    {
                        return new CardDefinition(self ? CardKind.SelfBoth : CardKind.OpponentBoth, first, second);
                    }
                }
            }

            throw new InvalidOperationException("Unreachable pair index.");
        }

        public static int RollDrawCount(ref RuleRandom random)
        {
            return Take(ref random, 3) + 1;
        }

        private static int Take(ref RuleRandom random, int exclusiveMax)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            int value = random.Next(exclusiveMax, out var next);
            random = next;
            return value;
        }
    }
}
