using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using UnityEngine;
using UnityEngine.SceneManagement;
using Waribasi.MatchFlow;

namespace Waribasi.Networking
{
    public sealed partial class OnlineSessionController
    {
        private sealed class LobbyLease
        {
            internal Lobby Lobby;
            internal bool Host;
            internal string Player;
            internal Task Cleanup;
        }

        private int operation;
        private bool connecting;
        private bool searching;
        private bool heartbeatPending;
        private LobbyLease lobby;
        private float nextHeartbeat;
        private float nextSearch;
        private float connectionStarted;
        private float peerConnectedAt = -1;
        public string MatchmakingPartition { get; set; } = Protocol + "-random";

        public void StartRoomHost(MatchSetup setup)
        {
            if (setup != null && setup.Mode == MatchMode.Room)
            {
                _ = ConnectAsync(setup, null, true);
            }
        }

        public void StartRoomClient(string code)
        {
            code = code?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code) || code.Length > 16 || code.Any(c => !char.IsLetterOrDigit(c)))
            {
                SetStatus("合言葉を確認してください");
                return;
            }
            _ = ConnectAsync(new MatchSetup(MatchMode.Room), code, false);
        }

        public void StartRandom()
        {
            _ = ConnectAsync(new MatchSetup(MatchMode.Random), null, false);
        }

        private async Task ConnectAsync(MatchSetup setup, string code, bool host)
        {
            if (IsWorking || HasSession || (manager != null && manager.IsListening))
            {
                return;
            }
            int epoch = ++operation;
            connecting = true;
            SetStatus("オンライン対戦を準備しています");
            LobbyLease acquired = null;
            try
            {
                BindManager();
                ResetConnectionState();
                await OnlineServicesBootstrap.EnsureReadyAsync();
                if (!IsCurrent(epoch))
                {
                    return;
                }
                if (setup.Mode == MatchMode.Random)
                {
                    SetStatus("対戦相手を検索しています");
                    try
                    {
                        var joined = await LobbyService.Instance.QuickJoinLobbyAsync(new QuickJoinLobbyOptions { Filter = LobbyFilters() });
                        acquired = new LobbyLease { Lobby = joined, Host = false, Player = AuthenticationService.Instance.PlayerId };
                    }
                    catch (LobbyServiceException error) when (error.Reason == LobbyExceptionReason.NoOpenLobbies)
                    {
                        host = true;
                    }
                    if (!IsCurrent(epoch))
                    {
                        return;
                    }
                    if (acquired != null)
                    {
                        if (acquired.Lobby.Data == null || !acquired.Lobby.Data.TryGetValue("JoinCode", out var entry))
                        {
                            throw new InvalidOperationException("Lobby connection data is unavailable.");
                        }
                        code = entry.Value;
                    }
                }
                if (host)
                {
                    var allocation = await RelayService.Instance.CreateAllocationAsync(1);
                    if (!IsCurrent(epoch))
                    {
                        return;
                    }
                    string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                    if (!IsCurrent(epoch))
                    {
                        return;
                    }
                    RelayTransportSetup.Host(allocation);
                    StartPreparedHost(setup);
                    RoomCode = joinCode;
                    if (setup.Mode == MatchMode.Random)
                    {
                        var created = await LobbyService.Instance.CreateLobbyAsync("Waribasi", 2, new CreateLobbyOptions
                        {
                            IsPrivate = false,
                            Data = new Dictionary<string, DataObject>
                            {
                                { "Mode", new DataObject(DataObject.VisibilityOptions.Public, MatchmakingPartition, DataObject.IndexOptions.S1) },
                                { "JoinCode", new DataObject(DataObject.VisibilityOptions.Member, joinCode) }
                            }
                        });
                        acquired = new LobbyLease { Lobby = created, Host = true, Player = AuthenticationService.Instance.PlayerId };
                        if (!IsCurrent(epoch))
                        {
                            return;
                        }
                    }
                    SetStatus("相手の参加を待っています");
                }
                else
                {
                    var allocation = await RelayService.Instance.JoinAllocationAsync(code);
                    if (!IsCurrent(epoch))
                    {
                        return;
                    }
                    RelayTransportSetup.Client(allocation);
                    StartPreparedClient();
                    SetStatus("相手に接続しています");
                }
                if (IsCurrent(epoch))
                {
                    lobby = acquired;
                    acquired = null;
                    if (HasPeer)
                    {
                        _ = ReleaseLobbyAsync();
                    }
                    nextHeartbeat = Time.unscaledTime + 15;
                    nextSearch = Time.unscaledTime + 3;
                    Notify();
                }
            }
            catch (Exception error)
            {
                if (IsCurrent(epoch))
                {
                    Debug.LogWarning("オンライン接続: " + error.GetType().Name);
                    await StopSocketAsync();
                    if (IsCurrent(epoch))
                    {
                        ResetConnectionState();
                        connecting = false;
                        string message = error is LobbyServiceException lobbyError && lobbyError.Reason == LobbyExceptionReason.RateLimited
                            ? "接続操作が多いため、少し待って再試行してください" : "接続できませんでした。設定を確認して再試行してください";
                        SetStatus(message);
                    }
                }
            }
            finally
            {
                if (acquired != null)
                {
                    await CleanLobbyAsync(acquired);
                }
            }
        }

        private bool IsCurrent(int epoch)
        {
            return this != null && Instance == this && epoch == operation && !returning;
        }

        private void ResetConnectionState()
        {
            flow = null;
            remote = null;
            remoteSetup = null;
            peerLeft = false;
            resultReady = false;
            loading = false;
            pendingScene = null;
            settingsShownAt = -1;
            peerConnectedAt = -1;
            settingsAcknowledged = false;
            receivedSerial = serial = 0;
            RoomCode = "";
            controlTraffic.Clear();
        }

        private void StartPreparedHost(MatchSetup setup)
        {
            BindManager();
            flow = new SessionFlowState(Guid.NewGuid(), setup);
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(Protocol);
            manager.ConnectionApprovalCallback = ApproveConnection;
            MatchNetworkController.ConfigureNextMatch(setup.Mode == MatchMode.Random, (FirstPlayerChoice)setup.First, setup.TurnSeconds);
            connectionStarted = Time.unscaledTime;
            if (!manager.StartHost())
            {
                flow = null;
                throw new InvalidOperationException("Host start failed.");
            }
            connecting = false;
        }

        private void StartPreparedClient()
        {
            BindManager();
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(Protocol);
            connectionStarted = Time.unscaledTime;
            if (!manager.StartClient())
            {
                throw new InvalidOperationException("Client start failed.");
            }
        }

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool protocolMatches = request.Payload != null && Encoding.UTF8.GetString(request.Payload) == Protocol;
            response.Approved = !returning && !restarting && manager != null && manager.ConnectedClients.Count < 2 &&
                SceneManager.GetActiveScene().name == "StartGame" && flow != null && flow.Phase == SessionPhase.Waiting && protocolMatches;
            response.CreatePlayerObject = response.Approved;
            response.Pending = false;
            if (!response.Approved)
            {
                response.Reason = "この対戦には参加できません";
            }
        }

        public async Task CancelOnlineAsync(string message = "待機をキャンセルしました")
        {
            if (returning || restarting)
            {
                return;
            }
            if (SceneManager.GetActiveScene().name != "StartGame")
            {
                CancelPreparation();
                return;
            }
            int epoch = ++operation;
            restarting = true;
            connecting = false;
            Notify();
            var cleanup = ReleaseLobbyAsync();
            await StopSocketAsync();
            if (this != null && Instance == this && epoch == operation)
            {
                ResetConnectionState();
                restarting = false;
                SetStatus(message);
            }
            //Lobbyの後片付けは操作世代に属するTaskとして完了させ、画面の取消を待たせない。
            _ = cleanup;
        }

        private async Task StopSocketAsync()
        {
            UnregisterHandlers();
            if (manager == null)
            {
                return;
            }
            bool wasRestarting = restarting;
            restarting = true;
            manager.Shutdown();
            while (manager != null && (manager.IsListening || manager.ShutdownInProgress))
            {
                await Task.Yield();
            }
            if (this != null)
            {
                restarting = wasRestarting;
            }
        }

        private List<QueryFilter> LobbyFilters()
        {
            return new List<QueryFilter>
            {
                new QueryFilter(QueryFilter.FieldOptions.S1, MatchmakingPartition, QueryFilter.OpOptions.EQ),
                new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
            };
        }

        private void UpdateConnection()
        {
            if (HasPeer && peerConnectedAt < 0)
            {
                peerConnectedAt = Time.unscaledTime;
            }
            if (SceneManager.GetActiveScene().name == "StartGame" && manager != null)
            {
                if (connecting && manager.IsClient && !manager.IsHost && !manager.IsConnectedClient && Time.unscaledTime - connectionStarted > 25)
                {
                    _ = CancelOnlineAsync("相手への接続がタイムアウトしました。再試行できます");
                }
                else if (HasPeer && Phase == SessionPhase.Waiting && peerConnectedAt >= 0 && Time.unscaledTime - peerConnectedAt > 30)
                {
                    _ = CancelOnlineAsync("対戦の準備が完了しませんでした。再試行できます");
                }
            }
            if (lobby == null || !lobby.Host || !IsHost || HasPeer || Phase != SessionPhase.Waiting)
            {
                return;
            }
            if (!heartbeatPending && Time.unscaledTime >= nextHeartbeat)
            {
                nextHeartbeat = Time.unscaledTime + 15;
                _ = HeartbeatAsync(lobby);
            }
            if (!searching && Time.unscaledTime >= nextSearch)
            {
                nextSearch = Time.unscaledTime + 3.5f;
                _ = MergeWaitingRoomsAsync(operation, lobby);
            }
        }

        private async Task HeartbeatAsync(LobbyLease lease)
        {
            heartbeatPending = true;
            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(lease.Lobby.Id);
            }
            catch (Exception error)
            {
                Debug.LogWarning("待機の更新: " + error.GetType().Name);
            }
            finally
            {
                heartbeatPending = false;
            }
        }

        private async Task MergeWaitingRoomsAsync(int epoch, LobbyLease original)
        {
            searching = true;
            LobbyLease joined = null;
            try
            {
                var results = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
                {
                    Count = 25,
                    Filters = LobbyFilters(),
                    Order = new List<QueryOrder> { new QueryOrder(true, QueryOrder.FieldOptions.Created) }
                });
                if (!IsCurrent(epoch) || lobby != original || HasPeer)
                {
                    return;
                }
                var older = results.Results.Where(candidate => candidate.Id != original.Lobby.Id &&
                    (candidate.Created < original.Lobby.Created || (candidate.Created == original.Lobby.Created &&
                        string.CompareOrdinal(candidate.Id, original.Lobby.Id) < 0)))
                    .OrderBy(candidate => candidate.Created).ThenBy(candidate => candidate.Id, StringComparer.Ordinal).FirstOrDefault();
                if (older == null)
                {
                    return;
                }
                var target = await LobbyService.Instance.JoinLobbyByIdAsync(older.Id);
                joined = new LobbyLease { Lobby = target, Host = false, Player = AuthenticationService.Instance.PlayerId };
                if (!IsCurrent(epoch) || HasPeer || lobby != original)
                {
                    return;
                }
                if (target.Data == null || !target.Data.TryGetValue("JoinCode", out var code))
                {
                    return;
                }
                lobby = null;
                await CleanLobbyAsync(original);
                if (!IsCurrent(epoch) || HasPeer)
                {
                    return;
                }
                await StopSocketAsync();
                if (!IsCurrent(epoch))
                {
                    return;
                }
                ResetConnectionState();
                connecting = true;
                var allocation = await RelayService.Instance.JoinAllocationAsync(code.Value);
                if (!IsCurrent(epoch))
                {
                    return;
                }
                RelayTransportSetup.Client(allocation);
                StartPreparedClient();
                lobby = joined;
                joined = null;
                SetStatus("対戦相手が見つかりました。接続しています");
            }
            catch (Exception error)
            {
                Debug.LogWarning("待機部屋の検索: " + error.GetType().Name);
                nextSearch = Time.unscaledTime + 6;
                if (IsCurrent(epoch) && !HasPeer && (manager == null || !manager.IsListening))
                {
                    ResetConnectionState();
                    connecting = false;
                    SetStatus("対戦相手に接続できませんでした。再試行できます");
                }
            }
            finally
            {
                if (joined != null)
                {
                    await CleanLobbyAsync(joined);
                }
                searching = false;
            }
        }

        private Task ReleaseLobbyAsync()
        {
            var lease = lobby;
            lobby = null;
            return lease != null ? CleanLobbyAsync(lease) : Task.CompletedTask;
        }

        private static Task CleanLobbyAsync(LobbyLease lease)
        {
            return lease.Cleanup ?? (lease.Cleanup = CleanLobbyCoreAsync(lease));
        }

        private static async Task CleanLobbyCoreAsync(LobbyLease lease)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (lease.Host)
                    {
                        await LobbyService.Instance.DeleteLobbyAsync(lease.Lobby.Id);
                    }
                    else
                    {
                        await LobbyService.Instance.RemovePlayerAsync(lease.Lobby.Id, lease.Player);
                    }
                    return;
                }
                catch (LobbyServiceException error) when (error.Reason == LobbyExceptionReason.LobbyNotFound)
                {
                    return;
                }
                catch (Exception error)
                {
                    if (attempt == 2)
                    {
                        Debug.LogWarning("待機部屋の後片付け: " + error.GetType().Name);
                    }
                    else
                    {
                        await Task.Delay((attempt + 1) * 1500);
                    }
                }
            }
        }
    }
}
