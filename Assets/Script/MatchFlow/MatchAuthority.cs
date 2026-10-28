using System;
using System.Collections.Generic;
//追加:本人の合法札の有無を正式状態で判断する。
using System.Linq;
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
        //追加:カード結果を公開する区間と、後続の自動スキップ表示を分ける。
        private bool skipAfterPresentation;

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
            //追加:判断する札も通常行動もない待機には時間切れを付けない。
            if (Gate == InputGate.Accepting && MustAutoSkip())
            {
                return ApplyAutomaticSkip(now);
            }
            if (Gate == InputGate.Accepting && Deadline.HasValue && now >= Deadline.Value)
            {
                return Commit(rules.TryEnd(State.CurrentPlayer, MatchEndReason.Timeout, State.StateVersion));
            }
            if (Gate == InputGate.Presenting && now >= PresentationUntil)
            {
                //追加:前のカード結果を表示し終えてから専用のスキップ通知を発行する。
                if (skipAfterPresentation)
                {
                    skipAfterPresentation = false;
                    return ApplyAutomaticSkip(now);
                }
                Gate = InputGate.Accepting;
                SetDeadline(now);
                LastResult = null;
                DeliverySequence++;
                //追加:次の手番の判断が不要なら、時計を消費せずスキップを確定する。
                if (MustAutoSkip())
                {
                    return ApplyAutomaticSkip(now);
                }
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
                //変更:スキップも通常行動の表示待ちへ通す。
                //削除:攻撃・振り分けだけを表示待ちにしていた条件。
                //if (result.Accepted && (request.Operation == MatchOperation.Attack || request.Operation == MatchOperation.Redistribute) &&
                if (result.Accepted && (action is AttackAction || action is RedistributeAction || action is SkipAction) &&
                    State.MatchPhase == MatchPhase.Active)
                {
                    BeginPresentation(now);
                }
                //追加:カード成立は先に公開し、その表示中に操作時計を進めない。
                else if (result.Accepted && action is UseCardAction && MustAutoSkip())
                {
                    skipAfterPresentation = true;
                    BeginPresentation(now);
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

        //追加:本人に使用可能な任意札があれば、辞退か使用の判断を待つ。
        private bool MustAutoSkip()
        {
            var actions = rules.GetLegalActions(State.CurrentPlayer);
            return actions.Any(action => action is SkipAction) && !actions.Any(action => action is UseCardAction);
        }

        //追加:ホストの自動進行も同じルール・確定通知を使う。
        private bool ApplyAutomaticSkip(double now)
        {
            var result = rules.TryApply(State.CurrentPlayer, new SkipAction(), State.StateVersion);
            if (!Commit(result))
            {
                return false;
            }
            BeginPresentation(now);
            return true;
        }

        //追加:操作判断の時計と、確定済み結果を見せる待機を分離する。
        private void BeginPresentation(double now)
        {
            Gate = InputGate.Presenting;
            Deadline = null;
            PresentationUntil = now + PresentationSeconds;
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
                //追加:降参・切断後に保留中のスキップを実行しない。
                skipAfterPresentation = false;
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
