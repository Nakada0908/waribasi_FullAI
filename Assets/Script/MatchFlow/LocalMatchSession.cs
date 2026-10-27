using System;
using Waribasi.Rules;

namespace Waribasi.MatchFlow
{
    public sealed class LocalMatchSession
    {
        private readonly ulong[] requests = new ulong[2];
        public NpcMatchSetup Setup { get; }
        public MatchAuthority Authority { get; private set; }
        public PlayerId Human => PlayerId.PlayerA;
        public PlayerId Npc => PlayerId.PlayerB;

        public LocalMatchSession(NpcMatchSetup setup, PlayerId randomFirst)
        {
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            if (setup.Difficulty == NpcDifficulty.Advanced)
            {
                throw new ArgumentException("Advanced NPC is not implemented.", nameof(setup));
            }
            Restart(randomFirst);
        }

        public void Restart(PlayerId randomFirst)
        {
            if (Authority != null && Authority.State.MatchPhase == MatchPhase.Active)
            {
                throw new InvalidOperationException("Finish the current match before restarting.");
            }
            PlayerId first = Setup.First == NpcFirstPlayerChoice.Player ? Human :
                Setup.First == NpcFirstPlayerChoice.Npc ? Npc : randomFirst;
            Authority = new MatchAuthority(Guid.NewGuid(), first, 0);
            requests[0] = requests[1] = 0;
        }

        public MatchResponse Submit(PlayerId actor, GameAction action, long version, double now)
        {
            return Authority.Submit(actor, Request(actor, action, version, false), now);
        }

        public MatchResponse Surrender(long version, double now)
        {
            return Authority.Submit(Human, Request(Human, null, version, true), now);
        }

        private MatchRequest Request(PlayerId actor, GameAction action, long version, bool surrender)
        {
            if (actor != Human && actor != Npc)
            {
                throw new ArgumentOutOfRangeException(nameof(actor));
            }
            ulong id = ++requests[(int)actor];
            if (surrender)
            {
                return new MatchRequest(Authority.MatchId, id, version, MatchOperation.Surrender);
            }
            if (action is AttackAction attack)
            {
                return new MatchRequest(Authority.MatchId, id, version, MatchOperation.Attack,
                    (int)attack.Source, (int)attack.Target);
            }
            if (action is RedistributeAction distribution)
            {
                return new MatchRequest(Authority.MatchId, id, version, MatchOperation.Redistribute,
                    left: distribution.Left, right: distribution.Right);
            }
            if (action is UseCardAction card)
            {
                return new MatchRequest(Authority.MatchId, id, version, MatchOperation.UseCard,
                    target: card.Target.HasValue ? (int)card.Target.Value : -1,
                    cardId: card.CardId, swapValues: card.SwapValues);
            }
            throw new ArgumentException("A supported action is required.", nameof(action));
        }
    }
}
