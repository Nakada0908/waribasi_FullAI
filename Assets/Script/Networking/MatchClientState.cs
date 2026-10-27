using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Networking
{
    //NetworkManagerと同じ寿命で、受信済みの確定結果をGameシーン破棄後も保持する。
    public sealed class MatchClientState : MonoBehaviour
    {
        public static MatchClientState Instance { get; private set; }
        private OwnHandMessage own;
        private readonly List<CardUseMessage> history = new List<CardUseMessage>();
        private IReadOnlyList<HandCard> cards = Array.Empty<HandCard>();
        private ulong nextRequest = 1;

        public PublicMatchMessage Public { get; private set; }
        public int LocalPlayer { get; private set; } = -1;
        public IReadOnlyList<HandCard> OwnHand => cards;
        public IReadOnlyList<CardUseMessage> History => history.AsReadOnly();
        public bool RevivalAcquired => own != null && own.revivalAcquired;
        public bool HasPendingRequest { get; private set; }
        public bool CommunicationInterrupted { get; private set; }
        public bool HasConfirmedResult => Public != null && Public.matchPhase == (int)MatchPhase.Finished;
        public bool HandIsCurrent => Public != null && own != null && own.stateVersion == Public.stateVersion;
        public bool CanAct => Public != null && !CommunicationInterrupted && !HasPendingRequest && HandIsCurrent &&
            Public.matchPhase == (int)MatchPhase.Active && Public.gate == (int)InputGate.Accepting &&
            Public.currentPlayer == LocalPlayer;
        public event Action Changed;
        public event Action<RequestResponseMessage> Responded;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void ResetForNewMatch()
        {
            Public = null;
            own = null;
            cards = Array.Empty<HandCard>();
            history.Clear();
            nextRequest = 1;
            LocalPlayer = -1;
            HasPendingRequest = false;
            CommunicationInterrupted = false;
            Changed?.Invoke();
        }

        internal bool ReceivePublic(PublicMatchMessage message, ulong localClientId)
        {
            if (message == null || message.hands == null || message.hands.Length != 4 ||
                !Guid.TryParseExact(message.matchId, "N", out _) ||
                (Public != null && (message.matchId != Public.matchId || message.sequence <= Public.sequence ||
                    message.stateVersion < Public.stateVersion || HasConfirmedResult)))
            {
                return false;
            }
            int seat = localClientId == message.playerAClientId ? 0 : localClientId == message.playerBClientId ? 1 : -1;
            if (seat < 0)
            {
                return false;
            }
            Public = message;
            LocalPlayer = seat;
            Changed?.Invoke();
            return true;
        }

        internal bool ReceiveOwn(OwnHandMessage message)
        {
            if (Public == null || message == null || message.matchId != Public.matchId || message.player != LocalPlayer ||
                message.cards == null || message.cards.Length > 5 || message.stateVersion < Public.stateVersion ||
                (own != null && message.stateVersion < own.stateVersion))
            {
                return false;
            }
            cards = Array.AsReadOnly(message.cards.Select(c => c.ToCard()).ToArray());
            own = message;
            Changed?.Invoke();
            return true;
        }

        internal void ReceiveHistory(HistoryPageMessage message)
        {
            if (Public == null || message == null || message.matchId != Public.matchId || message.offset != history.Count ||
                message.records == null || message.records.Length > MatchMessages.HistoryPageSize)
            {
                return;
            }
            history.AddRange(message.records);
            Changed?.Invoke();
        }

        public MatchRequestMessage CreateRequest(MatchOperation operation, int source = -1, int target = -1,
            int left = 0, int right = 0, long cardId = 0, bool swapValues = false)
        {
            if (Public == null || HasPendingRequest || nextRequest == ulong.MaxValue)
            {
                throw new InvalidOperationException("A current match without a pending request is required.");
            }
            HasPendingRequest = true;
            return new MatchRequestMessage
            {
                MatchId = Public.matchId, RequestId = nextRequest++, ExpectedVersion = Public.stateVersion,
                Operation = (int)operation, Source = source, Target = target, Left = left, Right = right,
                CardId = cardId, SwapValues = swapValues
            };
        }

        internal void ReceiveResponse(RequestResponseMessage message)
        {
            if (Public == null || message == null || message.matchId != Public.matchId || message.requestId != nextRequest - 1)
            {
                return;
            }
            HasPendingRequest = false;
            Responded?.Invoke(message);
            Changed?.Invoke();
        }

        internal void InterruptLocally()
        {
            CommunicationInterrupted = !HasConfirmedResult;
            HasPendingRequest = false;
            Changed?.Invoke();
        }

        public static HandSide ToSide(HandType type)
        {
            switch (type)
            {
                case HandType.Left:
                    return HandSide.Left;
                case HandType.Right:
                    return HandSide.Right;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }
    }
}
