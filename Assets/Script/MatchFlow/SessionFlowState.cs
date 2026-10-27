using System;
using Waribasi.Rules;

namespace Waribasi.MatchFlow
{
    public enum SessionPhase
    {
        Waiting,
        Loading,
        Preparing,
        Playing,
        Result,
        Closed
    }

    public sealed class SessionFlowState
    {
        private readonly bool[] ready = new bool[2];
        private readonly bool[] rematch = new bool[2];
        public Guid SessionId { get; }
        public MatchSetup Setup { get; }
        public int Round { get; private set; } = 1;
        public Guid MatchId { get; private set; }
        public SessionPhase Phase { get; private set; }
        public bool PeerPresent { get; private set; }
        public bool PlayerARematch => rematch[0];
        public bool PlayerBRematch => rematch[1];

        public SessionFlowState(Guid sessionId, MatchSetup setup)
        {
            if (sessionId == Guid.Empty || setup == null || setup.Mode == MatchMode.Npc)
            {
                throw new ArgumentException("An online session is required.");
            }
            SessionId = sessionId;
            Setup = setup;
        }

        public void ConnectPeer()
        {
            if (Phase == SessionPhase.Waiting)
            {
                PeerPresent = true;
            }
        }

        public bool AcknowledgeSettings(PlayerId player, Guid sessionId, int round)
        {
            if (!IsPlayer(player) || !PeerPresent || sessionId != SessionId || round != Round || Phase != SessionPhase.Waiting)
            {
                return false;
            }
            ready[(int)player] = true;
            return true;
        }

        public bool TryBegin()
        {
            if (Phase != SessionPhase.Waiting || !PeerPresent || !ready[0] || !ready[1])
            {
                return false;
            }
            Phase = SessionPhase.Loading;
            return true;
        }

        public bool BindMatch(Guid matchId)
        {
            if (Phase != SessionPhase.Loading || matchId == Guid.Empty)
            {
                return false;
            }
            MatchId = matchId;
            Phase = SessionPhase.Preparing;
            return true;
        }

        public bool MarkPlaying(Guid matchId)
        {
            if (matchId != MatchId || matchId == Guid.Empty || Phase != SessionPhase.Preparing)
            {
                return false;
            }
            Phase = SessionPhase.Playing;
            return true;
        }

        public bool Finish(Guid matchId)
        {
            if (matchId != MatchId || matchId == Guid.Empty || Phase != SessionPhase.Playing)
            {
                return false;
            }
            Phase = SessionPhase.Result;
            return true;
        }

        public bool VoteRematch(PlayerId player, Guid matchId, int round, bool agreed)
        {
            if (!IsPlayer(player) || !PeerPresent || Phase != SessionPhase.Result || matchId != MatchId || round != Round)
            {
                return false;
            }
            rematch[(int)player] = agreed;
            return true;
        }

        public bool TryRematch()
        {
            if (Phase != SessionPhase.Result || !PeerPresent || !rematch[0] || !rematch[1] || Round == int.MaxValue)
            {
                return false;
            }
            Round++;
            MatchId = Guid.Empty;
            ready[0] = ready[1] = rematch[0] = rematch[1] = false;
            Phase = SessionPhase.Loading;
            return true;
        }

        public bool CancelPreparation()
        {
            if (Phase != SessionPhase.Waiting && Phase != SessionPhase.Loading && Phase != SessionPhase.Preparing)
            {
                return false;
            }
            Phase = SessionPhase.Closed;
            rematch[0] = rematch[1] = false;
            return true;
        }

        public void DisconnectPeer()
        {
            PeerPresent = false;
            ready[0] = ready[1] = rematch[0] = rematch[1] = false;
            if (Phase != SessionPhase.Result && Phase != SessionPhase.Playing)
            {
                Phase = SessionPhase.Closed;
            }
        }

        private static bool IsPlayer(PlayerId player)
        {
            return player == PlayerId.PlayerA || player == PlayerId.PlayerB;
        }
    }
}
