using System;
using Waribasi.Rules;

namespace Waribasi.MatchFlow
{
    public enum MatchOperation
    {
        Attack,
        Redistribute,
        UseCard,
        Surrender
    }

    public enum InputGate
    {
        Preparing,
        Accepting,
        Presenting,
        Ended
    }

    public enum RequestRejection
    {
        None,
        UnknownPlayer,
        WrongMatch,
        InvalidSequence,
        ExpiredRequest,
        ChangedRequest,
        InvalidPayload,
        InputClosed,
        RuleRejected,
        RateLimited
    }

    public readonly struct MatchRequest : IEquatable<MatchRequest>
    {
        public Guid MatchId { get; }
        public ulong RequestId { get; }
        public long ExpectedVersion { get; }
        public MatchOperation Operation { get; }
        public int Source { get; }
        public int Target { get; }
        public int Left { get; }
        public int Right { get; }
        public long CardId { get; }
        public bool SwapValues { get; }

        public MatchRequest(Guid matchId, ulong requestId, long expectedVersion, MatchOperation operation,
            int source = -1, int target = -1, int left = 0, int right = 0,
            long cardId = 0, bool swapValues = false)
        {
            MatchId = matchId;
            RequestId = requestId;
            ExpectedVersion = expectedVersion;
            Operation = operation;
            Source = source;
            Target = target;
            Left = left;
            Right = right;
            CardId = cardId;
            SwapValues = swapValues;
        }

        public bool TryGetAction(out GameAction action)
        {
            action = null;
            if (ExpectedVersion < 0)
            {
                return false;
            }

            switch (Operation)
            {
                case MatchOperation.Attack:
                    if (!IsSide(Source) || !IsSide(Target) || Left != 0 || Right != 0 || CardId != 0 || SwapValues)
                    {
                        return false;
                    }
                    action = new AttackAction((HandSide)Source, (HandSide)Target);
                    return true;
                case MatchOperation.Redistribute:
                    if (Source != -1 || Target != -1 || Left < 0 || Left > 4 || Right < 0 || Right > 4 ||
                        CardId != 0 || SwapValues)
                    {
                        return false;
                    }
                    action = new RedistributeAction(Left, Right);
                    return true;
                case MatchOperation.UseCard:
                    if (Source != -1 || (Target != -1 && !IsSide(Target)) || Left != 0 || Right != 0 || CardId <= 0)
                    {
                        return false;
                    }
                    action = new UseCardAction(CardId, Target == -1 ? (HandSide?)null : (HandSide)Target, SwapValues);
                    return true;
                case MatchOperation.Surrender:
                    return Source == -1 && Target == -1 && Left == 0 && Right == 0 && CardId == 0 && !SwapValues;
                default:
                    return false;
            }
        }

        private static bool IsSide(int value)
        {
            return value == (int)HandSide.Left || value == (int)HandSide.Right;
        }

        public bool Equals(MatchRequest other)
        {
            return MatchId == other.MatchId && RequestId == other.RequestId && ExpectedVersion == other.ExpectedVersion &&
                Operation == other.Operation && Source == other.Source && Target == other.Target && Left == other.Left &&
                Right == other.Right && CardId == other.CardId && SwapValues == other.SwapValues;
        }

        public override bool Equals(object obj)
        {
            return obj is MatchRequest other && Equals(other);
        }

        public override int GetHashCode()
        {
            return MatchId.GetHashCode() ^ RequestId.GetHashCode();
        }
    }

    public sealed class MatchResponse
    {
        public ulong RequestId { get; }
        public bool Accepted { get; }
        public RequestRejection Rejection { get; }
        public ActionRejection RuleRejection { get; }
        public long StateVersion { get; }
        public bool Replayed { get; }

        internal MatchResponse(ulong requestId, bool accepted, RequestRejection rejection,
            ActionRejection ruleRejection, long stateVersion, bool replayed = false)
        {
            RequestId = requestId;
            Accepted = accepted;
            Rejection = rejection;
            RuleRejection = ruleRejection;
            StateVersion = stateVersion;
            Replayed = replayed;
        }

        internal MatchResponse Replay()
        {
            return new MatchResponse(RequestId, Accepted, Rejection, RuleRejection, StateVersion, true);
        }
    }
}
