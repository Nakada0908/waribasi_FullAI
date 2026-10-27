using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Networking
{
    public enum FirstPlayerChoice
    {
        Random,
        Host,
        Guest
    }

    public sealed class MatchNetworkController : NetworkBehaviour
    {
        public static MatchNetworkController Instance { get; private set; }
        public const double PreparationTimeoutSeconds = 30;
        public const double ResultReceiptTimeoutSeconds = 3;
        private readonly Dictionary<ulong, PlayerId> seats = new Dictionary<ulong, PlayerId>();
        private readonly Dictionary<PlayerId, PlayerStatus> players = new Dictionary<PlayerId, PlayerStatus>();
        private readonly Dictionary<(PlayerId, HandSide), NetworkObject> hands =
            new Dictionary<(PlayerId, HandSide), NetworkObject>();
        private readonly Dictionary<ulong, (double start, int count)> traffic = new Dictionary<ulong, (double, int)>();
        private readonly Queue<Action> work = new Queue<Action>();
        private readonly HashSet<ulong> loadedClients = new HashSet<ulong>();
        private readonly HashSet<ulong> readyClients = new HashSet<ulong>();
        private readonly HashSet<ulong> resultReceipts = new HashSet<ulong>();
        private MatchAuthority authority;
        private MatchClientState view;
        private bool processing;
        private bool loadingEnd;
        private int preparationStage;
        private long readySentVersion = -1;
        private long resultSentVersion = -1;
        private double preparationStarted;
        private double receivedAt;
        private double endedAt = -1;
        private double lastResync;
        private NetworkObject movingHand;
        private Vector3 moveOrigin;
        private Vector3 moveContact;
        private double movementStarted;
        private static FirstPlayerChoice nextFirst = FirstPlayerChoice.Random;
        private static int nextTurnSeconds = 30;

        private double Now => NetworkManager.ServerTime.Time;
        public MatchClientState ClientState => view;

        //T05は接続開始前に設定する。試合中の変更には使わない。
        public static void ConfigureNextMatch(bool randomMatch, FirstPlayerChoice first = FirstPlayerChoice.Random,
            int turnSeconds = 30)
        {
            if (!MatchAuthority.IsAllowedTime(turnSeconds) || first < FirstPlayerChoice.Random || first > FirstPlayerChoice.Guest)
            {
                throw new ArgumentException("Invalid match settings.");
            }
            nextFirst = randomMatch ? FirstPlayerChoice.Random : first;
            nextTurnSeconds = randomMatch ? 30 : turnSeconds;
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            view = NetworkManager.GetComponent<MatchClientState>();
            if (view == null)
            {
                view = NetworkManager.gameObject.AddComponent<MatchClientState>();
            }
            view.ResetForNewMatch();
            NetworkManager.OnClientDisconnectCallback += OnDisconnected;
            if (IsServer)
            {
                preparationStarted = Now;
                NetworkManager.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= OnDisconnected;
                if (NetworkManager.SceneManager != null)
                {
                    NetworkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoaded;
                }
            }
            work.Clear();
            if (Instance == this)
            {
                Instance = null;
            }
            base.OnNetworkDespawn();
        }

        private void OnSceneLoaded(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            if (sceneName != "Game")
            {
                return;
            }
            foreach (ulong client in completed)
            {
                loadedClients.Add(client);
            }
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }
            if (IsServer)
            {
                Enqueue(ServerUpdate);
            }
            ClientUpdate();
        }

        private void ServerUpdate()
        {
            if (authority == null)
            {
                TryPrepare();
                if (authority == null && Now - preparationStarted >= PreparationTimeoutSeconds)
                {
                    var remote = NetworkManager.ConnectedClientsIds.FirstOrDefault(id => id != Unity.Netcode.NetworkManager.ServerClientId);
                    if (remote != Unity.Netcode.NetworkManager.ServerClientId)
                    {
                        AbortPreparation(remote);
                    }
                }
                return;
            }
            double now = Now;
            UpdateMovement(now);
            if (authority.Gate == InputGate.Preparing && now - preparationStarted >= PreparationTimeoutSeconds)
            {
                if (authority.Interrupt(now))
                {
                    Publish();
                }
            }
            if (authority.Tick(now))
            {
                ApplyHandPoses();
                Publish();
            }
            if (authority.State.MatchPhase == MatchPhase.Finished && !loadingEnd && endedAt >= 0 &&
                now - endedAt >= MatchAuthority.PresentationSeconds &&
                (resultReceipts.Count == 2 || now - endedAt >= ResultReceiptTimeoutSeconds))
            {
                loadingEnd = true;
                NetworkManager.SceneManager.LoadScene("End", LoadSceneMode.Single);
            }
        }

        private void TryPrepare()
        {
            if (NetworkManager.ConnectedClients.Count != 2)
            {
                return;
            }
            players.Clear();
            seats.Clear();
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null || !client.PlayerObject.IsSpawned ||
                    client.PlayerObject.OwnerClientId != client.ClientId || !loadedClients.Contains(client.ClientId))
                {
                    return;
                }
                var status = client.PlayerObject.GetComponent<PlayerStatus>();
                if (status == null)
                {
                    return;
                }
                var player = client.ClientId == Unity.Netcode.NetworkManager.ServerClientId ? PlayerId.PlayerA : PlayerId.PlayerB;
                seats.Add(client.ClientId, player);
                players.Add(player, status);
            }

            hands.Clear();
            foreach (var hand in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
            {
                var side = hand.GetComponent<HandRL>();
                if (side == null || !hand.IsSpawned || !hand.CompareTag("Hand") || !seats.TryGetValue(hand.OwnerClientId, out var player))
                {
                    continue;
                }
                if (side.handType != HandType.Left && side.handType != HandType.Right)
                {
                    return;
                }
                var key = (player, MatchClientState.ToSide(side.handType));
                if (hands.ContainsKey(key))
                {
                    return;
                }
                hands.Add(key, hand);
            }
            if (hands.Count != 4)
            {
                return;
            }
            var first = nextFirst == FirstPlayerChoice.Host ? PlayerId.PlayerA :
                nextFirst == FirstPlayerChoice.Guest ? PlayerId.PlayerB :
                ((Guid.NewGuid().ToByteArray()[0] & 1) == 0 ? PlayerId.PlayerA : PlayerId.PlayerB);
            authority = new MatchAuthority(Guid.NewGuid(), first, nextTurnSeconds);
            preparationStage = 1;
            preparationStarted = Now;
            Publish();
        }

        private void ClientUpdate()
        {
            var state = view.Public;
            if (state == null || view.CommunicationInterrupted)
            {
                return;
            }
            if (state.matchPhase == (int)MatchPhase.Preparing && readySentVersion != state.stateVersion &&
                Time.realtimeSinceStartupAsDouble - receivedAt >= 0.25 && IsLocalDisplayReady() &&
                (state.stateVersion == 0 || view.HandIsCurrent))
            {
                readySentVersion = state.stateVersion;
                ReadyRpc(state.matchId, state.stateVersion, NetworkManager.LocalClient.PlayerObject.NetworkObjectId);
            }
            if (state.matchPhase == (int)MatchPhase.Finished && resultSentVersion != state.stateVersion)
            {
                resultSentVersion = state.stateVersion;
                ResultReceivedRpc(state.matchId, state.stateVersion);
            }
            if (view.History.Count < state.historyCount && Time.realtimeSinceStartupAsDouble - lastResync >= 0.5)
            {
                lastResync = Time.realtimeSinceStartupAsDouble;
                ResyncRpc(state.matchId, view.History.Count);
            }
        }

        private bool IsLocalDisplayReady()
        {
            return NetworkManager.LocalClient?.PlayerObject != null && Camera.main != null &&
                FindObjectsByType<CanvasReadyChecker>(FindObjectsSortMode.None).Length > 0 &&
                FindObjectsByType<HandRL>(FindObjectsSortMode.None).Length == 4;
        }

        private void Enqueue(Action action)
        {
            work.Enqueue(action);
            if (processing)
            {
                return;
            }
            processing = true;
            try
            {
                while (work.Count > 0)
                {
                    work.Dequeue()();
                }
            }
            finally
            {
                processing = false;
            }
        }

        private bool TryPeer(ulong clientId, out PlayerId player)
        {
            player = default;
            if (!IsServer || authority == null || !seats.TryGetValue(clientId, out player) ||
                !NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null ||
                !players.TryGetValue(player, out var status) || status == null ||
                client.PlayerObject != status.NetworkObject || client.PlayerObject.OwnerClientId != clientId)
            {
                return false;
            }
            double now = Now;
            traffic.TryGetValue(clientId, out var budget);
            if (now - budget.start >= 1)
            {
                budget = (now, 0);
            }
            traffic[clientId] = (budget.start, budget.count + 1);
            return budget.count < MatchAuthority.RequestsPerSecond;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReadyRpc(FixedString64Bytes matchId, long version, ulong playerObjectId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            Enqueue(() =>
            {
                if (!TryPeer(sender, out var player) || matchId.ToString() != authority.MatchId.ToString("N") ||
                    version != authority.State.StateVersion || authority.Gate != InputGate.Preparing ||
                    players[player].NetworkObjectId != playerObjectId || !loadedClients.Contains(sender))
                {
                    return;
                }
                readyClients.Add(sender);
                if (readyClients.Count != 2)
                {
                    return;
                }
                readyClients.Clear();
                if (!authority.State.InitialCardsDealt)
                {
                    authority.DealInitialCards();
                    preparationStage = 2;
                }
                else
                {
                    authority.Start(Now);
                    preparationStage = 3;
                }
                Publish();
            });
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestActionRpc(MatchRequestMessage message, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            Enqueue(() =>
            {
                if (!TryPeer(sender, out var player) || !message.TryDecode(out var request))
                {
                    return;
                }
                double now = Now;
                long sequence = authority.DeliverySequence;
                var response = authority.Submit(player, request, now);
                if (authority.DeliverySequence != sequence)
                {
                    if (response.Accepted && !response.Replayed && request.Operation == MatchOperation.Attack)
                    {
                        BeginMovement(player, request, now);
                    }
                    Publish();
                    if (authority.Gate != InputGate.Presenting)
                    {
                        ApplyHandPoses();
                    }
                }
                var reply = new RequestResponseMessage
                {
                    matchId = authority.MatchId.ToString("N"), requestId = response.RequestId,
                    accepted = response.Accepted, rejection = (int)response.Rejection,
                    ruleRejection = (int)response.RuleRejection, stateVersion = response.StateVersion, replayed = response.Replayed
                };
                ResponseRpc(JsonUtility.ToJson(reply), RpcTarget.Single(sender, RpcTargetUse.Temp));
            });
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ResyncRpc(FixedString64Bytes matchId, int historyOffset, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            Enqueue(() =>
            {
                if (!TryPeer(sender, out var player) || matchId.ToString() != authority.MatchId.ToString("N") ||
                    historyOffset < 0 || historyOffset > authority.State.CardHistory.Count)
                {
                    return;
                }
                SendState(sender, player);
                HistoryRpc(JsonUtility.ToJson(MatchMessages.History(authority, historyOffset)), RpcTarget.Single(sender, RpcTargetUse.Temp));
            });
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ResultReceivedRpc(FixedString64Bytes matchId, long version, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            Enqueue(() =>
            {
                if (TryPeer(sender, out _) && matchId.ToString() == authority.MatchId.ToString("N") &&
                    version == authority.State.StateVersion && authority.State.MatchPhase == MatchPhase.Finished)
                {
                    resultReceipts.Add(sender);
                }
            });
        }

        private void Publish()
        {
            MirrorState();
            foreach (var peer in seats)
            {
                if (NetworkManager.ConnectedClients.ContainsKey(peer.Key))
                {
                    SendState(peer.Key, peer.Value);
                }
            }
            if (authority.State.MatchPhase == MatchPhase.Finished && endedAt < 0)
            {
                endedAt = Now;
            }
        }

        private void SendState(ulong clientId, PlayerId player)
        {
            //切断時にはPlayerObjectが破棄済みでも、確定した席の接続IDを使う。
            ulong clientA = seats.First(pair => pair.Value == PlayerId.PlayerA).Key;
            ulong clientB = seats.First(pair => pair.Value == PlayerId.PlayerB).Key;
            PublicStateRpc(JsonUtility.ToJson(MatchMessages.Public(authority, clientA, clientB, preparationStage, Now)),
                RpcTarget.Single(clientId, RpcTargetUse.Temp));
            OwnHandRpc(JsonUtility.ToJson(MatchMessages.OwnHand(authority, player)), RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        private void MirrorState()
        {
            foreach (var pair in players)
            {
                if (pair.Value == null)
                {
                    continue;
                }
                var state = authority.State;
                pair.Value.ApplyConfirmed(state, pair.Key, authority.Gate == InputGate.Accepting);
                var card = pair.Value.GetComponent<CardAction>();
                if (card != null)
                {
                    card.ApplyConfirmed(state, pair.Key);
                }
            }
        }

        private bool IsServerMessage(RpcParams rpcParams, string json)
        {
            return rpcParams.Receive.SenderClientId == Unity.Netcode.NetworkManager.ServerClientId &&
                json != null && json.Length <= MatchMessages.MaximumServerMessageCharacters;
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void PublicStateRpc(string json, RpcParams rpcParams = default)
        {
            if (IsServerMessage(rpcParams, json) && view.ReceivePublic(JsonUtility.FromJson<PublicMatchMessage>(json), NetworkManager.LocalClientId))
            {
                receivedAt = Time.realtimeSinceStartupAsDouble;
            }
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void OwnHandRpc(string json, RpcParams rpcParams = default)
        {
            if (IsServerMessage(rpcParams, json))
            {
                view.ReceiveOwn(JsonUtility.FromJson<OwnHandMessage>(json));
            }
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void HistoryRpc(string json, RpcParams rpcParams = default)
        {
            if (IsServerMessage(rpcParams, json))
            {
                view.ReceiveHistory(JsonUtility.FromJson<HistoryPageMessage>(json));
            }
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ResponseRpc(string json, RpcParams rpcParams = default)
        {
            if (IsServerMessage(rpcParams, json))
            {
                view.ReceiveResponse(JsonUtility.FromJson<RequestResponseMessage>(json));
            }
        }

        public bool SendAttack(NetworkObject source, NetworkObject target)
        {
            if (!view.CanAct || source == null || target == null || !source.IsSpawned || !target.IsSpawned ||
                source.OwnerClientId != NetworkManager.LocalClientId || target.OwnerClientId == NetworkManager.LocalClientId ||
                !source.CompareTag("Hand") || !target.CompareTag("Hand") ||
                source.GetComponent<HandRL>() == null || target.GetComponent<HandRL>() == null)
            {
                return false;
            }
            RequestActionRpc(view.CreateRequest(MatchOperation.Attack,
                (int)MatchClientState.ToSide(source.GetComponent<HandRL>().handType),
                (int)MatchClientState.ToSide(target.GetComponent<HandRL>().handType)));
            return true;
        }

        public bool SendRedistribution(int left, int right)
        {
            if (!view.CanAct)
            {
                return false;
            }
            RequestActionRpc(view.CreateRequest(MatchOperation.Redistribute, left: left, right: right));
            return true;
        }

        public bool SendCard(long cardId, HandSide? target, bool swapValues, long selectedVersion)
        {
            if (!view.CanAct || view.Public.stateVersion != selectedVersion)
            {
                return false;
            }
            RequestActionRpc(view.CreateRequest(MatchOperation.UseCard, target: target.HasValue ? (int)target.Value : -1,
                cardId: cardId, swapValues: swapValues));
            return true;
        }

        public bool SendSurrender()
        {
            if (!IsSpawned || NetworkManager == null || !NetworkManager.IsConnectedClient || view == null ||
                view.CommunicationInterrupted || view.Public == null || view.Public.matchPhase != (int)MatchPhase.Active || view.HasPendingRequest)
            {
                return false;
            }
            RequestActionRpc(view.CreateRequest(MatchOperation.Surrender));
            return true;
        }

        public void RefreshHandPoses()
        {
            if (IsServer && authority != null && authority.Gate != InputGate.Presenting)
            {
                ApplyHandPoses();
            }
        }

        private void BeginMovement(PlayerId player, MatchRequest request, double now)
        {
            movingHand = hands[(player, (HandSide)request.Source)];
            var target = hands[(GameState.GetOpponent(player), (HandSide)request.Target)];
            moveOrigin = movingHand.transform.position;
            Transform from = movingHand.transform.Find("targetpos");
            Transform to = target.transform.Find("targetpos");
            moveContact = from != null && to != null ? moveOrigin + to.position - from.position : target.transform.position;
            movementStarted = now;
        }

        private void UpdateMovement(double now)
        {
            if (movingHand == null)
            {
                return;
            }
            float progress = (float)((now - movementStarted) / MatchAuthority.PresentationSeconds);
            if (progress >= 1 || authority.Gate == InputGate.Ended)
            {
                movingHand.transform.position = moveOrigin;
                movingHand = null;
                ApplyHandPoses();
                return;
            }
            movingHand.transform.position = progress < 0.5f
                ? Vector3.Lerp(moveOrigin, moveContact, progress * 2)
                : Vector3.Lerp(moveContact, moveOrigin, (progress - 0.5f) * 2);
        }

        private void ApplyHandPoses()
        {
            foreach (var pair in hands)
            {
                if (pair.Value == null)
                {
                    continue;
                }
                int count = authority.State.GetHands(pair.Key.Item1).GetCount(pair.Key.Item2);
                var animator = pair.Value.GetComponentInChildren<HandFingerController>();
                if (animator != null)
                {
                    animator.SetFinger(count == 0 ? 5 : count);
                }
                var home = pair.Value.GetComponent<HandPos>();
                var stay = pair.Value.transform.Find("staypos");
                if (home != null)
                {
                    //stayposは手の子なので毎回のworld値を累積しない。
                    pair.Value.transform.position = count == 0 && stay != null
                        ? home.resetPos + pair.Value.transform.TransformVector(stay.localPosition)
                        : home.resetPos;
                }
            }
        }

        private void OnDisconnected(ulong clientId)
        {
            if (!IsServer)
            {
                view.InterruptLocally();
                return;
            }
            Enqueue(() =>
            {
                if (authority == null && clientId != Unity.Netcode.NetworkManager.ServerClientId)
                {
                    AbortPreparation(clientId);
                    return;
                }
                if (authority != null && seats.TryGetValue(clientId, out var player) && authority.Disconnect(player, Now))
                {
                    Publish();
                }
            });
        }

        private void AbortPreparation(ulong remoteClientId)
        {
            seats.Clear();
            seats.Add(Unity.Netcode.NetworkManager.ServerClientId, PlayerId.PlayerA);
            seats.Add(remoteClientId, PlayerId.PlayerB);
            players.Clear();
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (seats.TryGetValue(client.ClientId, out var player) && client.PlayerObject != null &&
                    client.PlayerObject.GetComponent<PlayerStatus>() != null)
                {
                    players.Add(player, client.PlayerObject.GetComponent<PlayerStatus>());
                }
            }
            authority = new MatchAuthority(Guid.NewGuid(), PlayerId.PlayerA, nextTurnSeconds);
            preparationStage = 0;
            authority.Interrupt(Now);
            Publish();
        }
    }
}
