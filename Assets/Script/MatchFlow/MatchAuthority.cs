using System;
using System.Collections.Generic;
using Waribasi.Rules;

namespace Waribasi.MatchFlow
{
    //通信層が本人確認を済ませた席だけを渡す。呼出し元が直列化する。
    public sealed class MatchAuthority
    {
        public const int ResponseCacheSize = 64;
        public const double PresentationSeconds = 1.0;
        public const int RequestsPerSecond = 64;
        private readonly RuleEngine rules;
        private readonly Dictionary<ulong, CachedResponse>[] responses =
        {
            new Dictionary<ulong, CachedResponse>(), new Dictionary<ulong, CachedResponse>()
        };
        private readonly Queue<ulong>[] responseOrder = { new Queue<ulong>(), new Queue<ulong>() };
        private readonly ulong[] lastRequest = new ulong[2];
        private readonly double[] rateStart = new double[2];
        private readonly int[] rateCount = new int[2];
        private double lastTime;

        public Guid MatchId { get; }
        public int TurnSeconds { get; }
        public GameState State => rules.Snapshot;
        public InputGate Gate { get; private set; }
        public double? Deadline { get; private set; }
        public double PresentationUntil { get; private set; }
        public long DeliverySequence { get; private set; }
        public ActionResult LastResult { get; private set; }

        private sealed class CachedResponse
        {
            internal MatchRequest Request;
            internal MatchResponse Response;
        }

        public MatchAuthority(Guid matchId, PlayerId firstPlayer, int turnSeconds)
            : this(matchId, new RuleEngine(firstPlayer), turnSeconds, 0)
        {
        }

        //正式対局へ外部盤面を取り込むAPIとして通信に公開しない。
        public MatchAuthority(Guid matchId, RuleEngine engine, int turnSeconds, double now)
        {
            if (matchId == Guid.Empty || engine == null || !IsAllowedTime(turnSeconds))
            {
                throw new ArgumentException("Invalid match configuration.");
            }
            MatchId = matchId;
            TurnSeconds = turnSeconds;
            rules = engine.Fork();
            CheckTime(now);
            Gate = State.MatchPhase == MatchPhase.Preparing ? InputGate.Preparing :
                State.MatchPhase == MatchPhase.Active ? InputGate.Accepting : InputGate.Ended;
            if (Gate == InputGate.Accepting)
            {
                SetDeadline(now);
            }
        }

        public static bool IsAllowedTime(int seconds)
        {
            return seconds == 0 || seconds == 15 || seconds == 30 || seconds == 60 || seconds == 120 ||
                seconds == 180 || seconds == 300 || seconds == 600;
        }

        public PlayerObservation GetObservation(PlayerId authenticatedPlayer)
        {
            return rules.GetObservation(authenticatedPlayer);
        }

        public bool DealInitialCards()
        {
            return Commit(rules.TryDealInitialCards(State.StateVersion));
        }

        public bool Start(double now)
        {
            CheckTime(now);
            if (!Commit(rules.TryStart(State.StateVersion)))
            {
                return false;
            }
            Gate = InputGate.Accepting;
            SetDeadline(now);
            return true;
        }

        public bool Tick(double now)
        {
            CheckTime(now);
            if (Gate == InputGate.Accepting && Deadline.HasValue && now >= Deadline.Value)
            {
                return Commit(rules.TryEnd(State.CurrentPlayer, MatchEndReason.Timeout, State.StateVersion));
            }
            if (Gate == InputGate.Presenting && now >= PresentationUntil)
            {
                Gate = InputGate.Accepting;
                SetDeadline(now);
                LastResult = null;
                DeliverySequence++;
                return true;
            }
            return false;
        }

        public bool Disconnect(PlayerId player, double now)
        {
            CheckTime(now);
            return Commit(State.MatchPhase == MatchPhase.Active
                ? rules.TryEnd(player, MatchEndReason.Disconnected, State.StateVersion)
                : rules.TryInterrupt(State.StateVersion));
        }

        public bool Interrupt(double now)
        {
            CheckTime(now);
            return Commit(rules.TryInterrupt(State.StateVersion));
        }

        public MatchResponse Submit(PlayerId authenticatedPlayer, MatchRequest request, double now)
        {
            CheckTime(now);
            if (authenticatedPlayer != PlayerId.PlayerA && authenticatedPlayer != PlayerId.PlayerB)
            {
                return Reject(request, RequestRejection.UnknownPlayer);
            }
            if (request.MatchId != MatchId)
            {
                return Reject(request, RequestRejection.WrongMatch);
            }
            int seat = (int)authenticatedPlayer;
            if (now - rateStart[seat] >= 1)
            {
                rateStart[seat] = now;
                rateCount[seat] = 0;
            }
            if (++rateCount[seat] > RequestsPerSecond)
            {
                return Reject(request, RequestRejection.RateLimited);
            }
            if (responses[seat].TryGetValue(request.RequestId, out var cached))
            {
                return cached.Request.Equals(request) ? cached.Response.Replay() : Reject(request, RequestRejection.ChangedRequest);
            }
            if (request.RequestId == 0 || request.RequestId == ulong.MaxValue ||
                request.RequestId > lastRequest[seat] + 1)
            {
                return Reject(request, RequestRejection.InvalidSequence);
            }
            if (request.RequestId <= lastRequest[seat])
            {
                return Reject(request, RequestRejection.ExpiredRequest);
            }

            lastRequest[seat] = request.RequestId;
            //期限判定と通常要求を分離して待機させない。
            Tick(now);
            MatchResponse response;
            if (!request.TryGetAction(out var action))
            {
                response = Reject(request, RequestRejection.InvalidPayload);
            }
            else if (State.MatchPhase != MatchPhase.Active ||
                (Gate != InputGate.Accepting && request.Operation != MatchOperation.Surrender))
            {
                response = Reject(request, RequestRejection.InputClosed);
            }
            else
            {
                var result = request.Operation == MatchOperation.Surrender
                    ? rules.TryEnd(authenticatedPlayer, MatchEndReason.Surrender, request.ExpectedVersion)
                    : rules.TryApply(authenticatedPlayer, action, request.ExpectedVersion);
                Commit(result);
                if (result.Accepted && (request.Operation == MatchOperation.Attack || request.Operation == MatchOperation.Redistribute) &&
                    State.MatchPhase == MatchPhase.Active)
                {
                    Gate = InputGate.Presenting;
                    Deadline = null;
                    PresentationUntil = now + PresentationSeconds;
                }
                response = new MatchResponse(request.RequestId, result.Accepted,
                    result.Accepted ? RequestRejection.None : RequestRejection.RuleRejected,
                    result.Rejection, State.StateVersion);
            }

            responses[seat].Add(request.RequestId, new CachedResponse { Request = request, Response = response });
            responseOrder[seat].Enqueue(request.RequestId);
            if (responseOrder[seat].Count > ResponseCacheSize)
            {
                responses[seat].Remove(responseOrder[seat].Dequeue());
            }
            return response;
        }

        private bool Commit(ActionResult result)
        {
            if (!result.Accepted)
            {
                return false;
            }
            LastResult = result;
            DeliverySequence++;
            if (State.MatchPhase == MatchPhase.Finished || State.MatchPhase == MatchPhase.Interrupted)
            {
                Gate = InputGate.Ended;
                Deadline = null;
            }
            return true;
        }

        private MatchResponse Reject(MatchRequest request, RequestRejection reason)
        {
            return new MatchResponse(request.RequestId, false, reason, ActionRejection.None, State.StateVersion);
        }

        private void SetDeadline(double now)
        {
            Deadline = TurnSeconds == 0 ? (double?)null : now + TurnSeconds;
        }

        private void CheckTime(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < lastTime)
            {
                throw new ArgumentOutOfRangeException(nameof(now));
            }
            lastTime = now;
        }
    }
}
