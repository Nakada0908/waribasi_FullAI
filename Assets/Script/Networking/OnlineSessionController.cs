using System;
using System.Collections;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Networking
{
    public sealed partial class OnlineSessionController : MonoBehaviour
    {
        public const string Protocol = "waribasi-session-v1";
        private const string Channel = "waribasi/session/v1";
        private const float SettingsDisplaySeconds = 0.75f;
        private const int MaximumMessageBytes = 4096;
        public static OnlineSessionController Instance { get; private set; }

        [Serializable]
        private sealed class ControlMessage
        {
            public int kind;
            public string session;
            public string match;
            public int round;
            public long serial;
            public int mode;
            public int first;
            public int seconds;
            public int phase;
            public bool voteA;
            public bool voteB;
            public bool peer;
            public bool resultReady;
            public bool agree;
        }

        private enum ControlKind
        {
            Offer,
            SettingsReceived,
            State,
            Rematch,
            Leave,
            Cancel,
            CancelRejected,
            Hello
        }

        private NetworkManager manager;
        private MatchClientState view;
        private SessionFlowState flow;
        private MatchSetup remoteSetup;
        private ControlMessage remote;
        private bool handlersRegistered;
        private bool settingsAcknowledged;
        private bool returning;
        private bool restarting;
        private bool peerLeft;
        private bool resultReady;
        private bool loading;
        private string pendingScene;
        private string departure;
        private bool surrenderSent;
        private double resultObservedAt = -1;
        private float settingsShownAt = -1;
        private float lastOffer;
        private long serial;
        private long receivedSerial;
        private readonly System.Collections.Generic.Dictionary<ulong, (float start, int count)> controlTraffic =
            new System.Collections.Generic.Dictionary<ulong, (float, int)>();

        public event Action Changed;
        public string Status { get; private set; } = "対戦モードを選んでください";
        public string RoomCode { get; private set; } = "";
        public MatchSetup Settings => flow?.Setup ?? remoteSetup;
        public bool IsWorking => connecting || returning || restarting;
        public bool HasSession => flow != null || remote != null;
        public bool HasPeer => !peerLeft && manager != null && manager.IsConnectedClient &&
            (!manager.IsHost || manager.ConnectedClients.Count == 2);
        public bool Loading => loading || pendingScene != null;
        public bool IsHost => manager != null && manager.IsHost;
        public bool LocalRematch => IsHost ? flow != null && flow.PlayerARematch : remote != null && remote.voteB;
        public bool OpponentRematch => IsHost ? flow != null && flow.PlayerBRematch : remote != null && remote.voteA;
        public bool OpponentLeft => peerLeft;
        public bool CanRematch => !IsWorking && !Loading && HasPeer && resultReady && view != null && view.HasConfirmedResult &&
            SceneManager.GetActiveScene().name == "End";
        public SessionPhase Phase => flow?.Phase ?? (remote != null ? (SessionPhase)remote.phase : SessionPhase.Closed);

        public static OnlineSessionController Ensure()
        {
            if (Instance == null)
            {
                new GameObject("MatchSession").AddComponent<OnlineSessionController>();
            }
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void BindManager()
        {
            var next = NetworkManager.Singleton;
            if (manager == next)
            {
                return;
            }
            UnbindManager();
            manager = next;
            if (manager == null)
            {
                throw new InvalidOperationException("NetworkManager is unavailable.");
            }
            manager.OnServerStarted += RegisterHandlers;
            manager.OnClientStarted += RegisterHandlers;
            manager.OnClientConnectedCallback += OnConnected;
            manager.OnClientDisconnectCallback += OnDisconnected;
        }

        private void RegisterHandlers()
        {
            if (manager == null || handlersRegistered || manager.CustomMessagingManager == null)
            {
                return;
            }
            manager.CustomMessagingManager.RegisterNamedMessageHandler(Channel, ReceiveControl);
            manager.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
            handlersRegistered = true;
        }

        private void UnregisterHandlers()
        {
            if (manager != null && handlersRegistered)
            {
                manager.CustomMessagingManager?.UnregisterNamedMessageHandler(Channel);
                if (manager.SceneManager != null)
                {
                    manager.SceneManager.OnLoadEventCompleted -= OnSceneLoaded;
                }
            }
            handlersRegistered = false;
        }

        private void UnbindManager()
        {
            UnregisterHandlers();
            if (manager != null)
            {
                manager.OnServerStarted -= RegisterHandlers;
                manager.OnClientStarted -= RegisterHandlers;
                manager.OnClientConnectedCallback -= OnConnected;
                manager.OnClientDisconnectCallback -= OnDisconnected;
            }
            manager = null;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }
            operation++;
            UnbindManager();
            if (view != null)
            {
                view.Changed -= ObserveMatch;
            }
            Instance = null;
        }

        private void Update()
        {
            if (Instance != this || returning || restarting)
            {
                return;
            }
            if (view != MatchClientState.Instance)
            {
                if (view != null)
                {
                    view.Changed -= ObserveMatch;
                }
                view = MatchClientState.Instance;
                if (view != null)
                {
                    view.Changed += ObserveMatch;
                    ObserveMatch();
                }
            }
            if (HasPeer && settingsShownAt >= 0 && !settingsAcknowledged &&
                Time.unscaledTime - settingsShownAt >= SettingsDisplaySeconds &&
                (IsHost ? flow != null && flow.PeerPresent : remote != null && remote.peer) &&
                Waribasi.Presentation.MatchSetupPresenter.Instance != null &&
                Waribasi.Presentation.MatchSetupPresenter.Instance.SettingsVisible)
            {
                if (IsHost)
                {
                    settingsAcknowledged = flow.AcknowledgeSettings(PlayerId.PlayerA, flow.SessionId, flow.Round);
                    TryBegin();
                }
                else
                {
                    settingsAcknowledged = true;
                    SendCommand(ControlKind.SettingsReceived);
                }
            }
            if (IsHost && flow != null && flow.Phase == SessionPhase.Waiting && HasPeer && Time.unscaledTime - lastOffer >= 1)
            {
                SendSnapshot(ControlKind.Offer);
            }
            if (pendingScene != null && !loading && manager != null && manager.IsHost)
            {
                string next = pendingScene;
                var progress = manager.SceneManager.LoadScene(next, LoadSceneMode.Single);
                if (progress == SceneEventProgressStatus.Started)
                {
                    pendingScene = null;
                    loading = true;
                    Notify();
                }
                else if (progress != SceneEventProgressStatus.SceneEventInProgress)
                {
                    pendingScene = null;
                    SetStatus("画面を読み込めませんでした。退出して再試行してください");
                }
            }
            if (departure != null)
            {
                ProcessDeparture();
            }
            UpdateConnection();
        }

        private void OnConnected(ulong clientId)
        {
            if (returning || restarting)
            {
                return;
            }
            RegisterHandlers();
            if (IsHost && clientId != NetworkManager.ServerClientId && flow != null)
            {
                flow.ConnectPeer();
                connecting = false;
                settingsShownAt = Time.unscaledTime;
                SetStatus("相手が接続しました。対戦設定を確認しています");
                SendSnapshot(ControlKind.Offer);
                _ = ReleaseLobbyAsync();
            }
            else if (!IsHost && clientId == manager.LocalClientId)
            {
                connecting = false;
                Send(new ControlMessage { kind = (int)ControlKind.Hello }, NetworkManager.ServerClientId);
                SetStatus("接続完了。ホストの対戦設定を待っています");
            }
        }

        private void OnDisconnected(ulong clientId)
        {
            if (returning || restarting || manager == null)
            {
                return;
            }
            if (manager.IsServer && clientId == NetworkManager.ServerClientId)
            {
                return;
            }
            peerLeft = true;
            resultReady = false;
            if (remote != null)
            {
                remote.voteA = remote.voteB = false;
            }
            flow?.DisconnectPeer();
            string scene = SceneManager.GetActiveScene().name;
            if (scene == "StartGame")
            {
                _ = CancelOnlineAsync("接続が終了しました。もう一度開始できます");
            }
            else if (!manager.IsServer)
            {
                view?.InterruptLocally();
                SetStatus("相手との接続が終了しました");
                StartCoroutine(OpenLocalResult());
            }
            else
            {
                SetStatus("相手が退出しました");
            }
        }

        private IEnumerator OpenLocalResult()
        {
            yield return null;
            if (SceneManager.GetActiveScene().name != "End" && !returning)
            {
                SceneManager.LoadScene("End");
            }
        }

        private void ObserveMatch()
        {
            var state = view?.Public;
            if (state == null || !Guid.TryParseExact(state.matchId, "N", out var matchId))
            {
                return;
            }
            if (IsHost && flow != null)
            {
                bool changed = false;
                if (flow.Phase == SessionPhase.Loading)
                {
                    changed = flow.BindMatch(matchId);
                    resultObservedAt = -1;
                    resultReady = false;
                }
                if (state.matchPhase == (int)MatchPhase.Active || state.matchPhase == (int)MatchPhase.Finished)
                {
                    changed |= flow.MarkPlaying(matchId);
                }
                if (view.HasConfirmedResult)
                {
                    changed |= flow.Finish(matchId);
                }
                if (changed)
                {
                    SendSnapshot(ControlKind.State);
                }
            }
            if (view.HasConfirmedResult && resultObservedAt < 0)
            {
                resultObservedAt = Time.realtimeSinceStartupAsDouble;
            }
            if (state.matchPhase == (int)MatchPhase.Interrupted)
            {
                if (IsHost)
                {
                    QueueResultScene();
                }
            }
            Notify();
        }

        private void TryBegin()
        {
            if (flow != null && flow.TryBegin())
            {
                BeginRound();
            }
        }

        private void BeginRound()
        {
            resultReady = false;
            settingsAcknowledged = false;
            settingsShownAt = -1;
            MatchNetworkController.ConfigureNextMatch(flow.Setup.Mode == MatchMode.Random,
                (FirstPlayerChoice)flow.Setup.First, flow.Setup.TurnSeconds);
            pendingScene = "Game";
            SetStatus("対戦画面を読み込んでいます");
            SendSnapshot(ControlKind.State);
        }

        private void OnSceneLoaded(string scene, LoadSceneMode mode, System.Collections.Generic.List<ulong> completed,
            System.Collections.Generic.List<ulong> timedOut)
        {
            loading = false;
            if (scene == "End")
            {
                resultReady = HasPeer && timedOut.Count == 0 && completed.Count == 2 && view != null && view.HasConfirmedResult;
                SendSnapshot(ControlKind.State);
            }
            Notify();
        }

        public void QueueResultScene()
        {
            if (IsHost && SceneManager.GetActiveScene().name != "End" && pendingScene != "End")
            {
                pendingScene = "End";
            }
        }

        public bool RequestRematch(bool agreed = true)
        {
            if (!CanRematch)
            {
                return false;
            }
            if (IsHost)
            {
                ApplyVote(PlayerId.PlayerA, flow.MatchId, flow.Round, agreed);
            }
            else
            {
                SendCommand(ControlKind.Rematch, agreed);
            }
            return true;
        }

        private void ApplyVote(PlayerId player, Guid matchId, int round, bool agreed)
        {
            if (!resultReady || !flow.VoteRematch(player, matchId, round, agreed))
            {
                return;
            }
            if (flow.TryRematch())
            {
                BeginRound();
            }
            else
            {
                SendSnapshot(ControlKind.State);
                Notify();
            }
        }

        public void CancelPreparation()
        {
            if (SceneManager.GetActiveScene().name == "StartGame")
            {
                _ = CancelOnlineAsync();
            }
            else if (IsHost)
            {
                ApplyPreparationCancel();
            }
            else if (HasPeer)
            {
                SendCommand(ControlKind.Cancel);
            }
        }

        private void ApplyPreparationCancel()
        {
            if ((view?.Public != null && view.Public.matchPhase == (int)MatchPhase.Active) ||
                (MatchNetworkController.Instance != null && !MatchNetworkController.Instance.CancelPreparationFromSession()))
            {
                SetStatus("対戦は開始済みです。退出する場合は降参を選んでください");
                SendSnapshot(ControlKind.CancelRejected);
                return;
            }
            if (flow == null || !flow.CancelPreparation())
            {
                return;
            }
            SendSnapshot(ControlKind.Cancel);
            RequestLeave("StartGame");
        }

        public void RequestLeave(string destination)
        {
            if (returning || departure != null || (destination != "StartGame" && destination != "Title" && destination != "Quit"))
            {
                return;
            }
            if (manager == null && NetworkManager.Singleton != null)
            {
                BindManager();
            }
            departure = destination;
            surrenderSent = false;
            ProcessDeparture();
        }

        private void ProcessDeparture()
        {
            var state = view?.Public;
            if (state != null && !view.CommunicationInterrupted && state.matchPhase == (int)MatchPhase.Active && HasPeer)
            {
                if (!surrenderSent && !view.HasPendingRequest)
                {
                    surrenderSent = MatchNetworkController.Instance != null && MatchNetworkController.Instance.SendSurrender();
                }
                SetStatus("降参の結果を確認しています");
                return;
            }
            if (view != null && view.HasConfirmedResult && HasPeer && SceneManager.GetActiveScene().name != "End")
            {
                return;
            }
            if (view != null && view.HasConfirmedResult && HasPeer && resultObservedAt >= 0 &&
                Time.realtimeSinceStartupAsDouble - resultObservedAt < MatchNetworkController.ResultReceiptTimeoutSeconds && !resultReady)
            {
                return;
            }
            string target = departure;
            departure = null;
            StartCoroutine(LeaveAndLoad(target));
        }

        private IEnumerator LeaveAndLoad(string destination)
        {
            returning = true;
            operation++;
            pendingScene = null;
            if (HasPeer)
            {
                if (IsHost)
                {
                    SendSnapshot(ControlKind.Leave);
                }
                else
                {
                    SendCommand(ControlKind.Leave);
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            var cleanup = ReleaseLobbyAsync();
            UnregisterHandlers();
            if (manager != null)
            {
                manager.Shutdown();
                while (manager != null && (manager.IsListening || manager.ShutdownInProgress))
                {
                    yield return null;
                }
                Destroy(manager.gameObject);
                yield return null;
            }
            if (!cleanup.IsCompleted)
            {
                SetStatus("接続の後片付けをしています");
            }
            Instance = null;
            if (destination == "Quit")
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                SceneManager.LoadScene("Title");
#else
                Application.Quit();
#endif
            }
            else
            {
                SceneManager.LoadScene(destination);
            }
            Destroy(gameObject);
        }

        private void SendSnapshot(ControlKind kind)
        {
            if (!IsHost || flow == null)
            {
                return;
            }
            var message = new ControlMessage
            {
                kind = (int)kind, session = flow.SessionId.ToString("N"), match = flow.MatchId.ToString("N"), round = flow.Round,
                serial = ++serial, mode = (int)flow.Setup.Mode, first = (int)flow.Setup.First, seconds = flow.Setup.TurnSeconds,
                phase = (int)flow.Phase, voteA = flow.PlayerARematch, voteB = flow.PlayerBRematch,
                peer = flow.PeerPresent && !peerLeft, resultReady = resultReady
            };
            foreach (ulong peer in manager.ConnectedClientsIds.Where(id => id != NetworkManager.ServerClientId).ToArray())
            {
                Send(message, peer);
            }
            lastOffer = Time.unscaledTime;
        }

        private void SendCommand(ControlKind kind, bool agreed = false)
        {
            if (remote == null)
            {
                return;
            }
            Send(new ControlMessage
            {
                kind = (int)kind, session = remote.session, match = remote.match, round = remote.round, agree = agreed
            }, NetworkManager.ServerClientId);
        }

        private void Send(ControlMessage message, ulong recipient)
        {
            if (manager == null || !manager.IsListening || manager.CustomMessagingManager == null)
            {
                return;
            }
            string json = JsonUtility.ToJson(message);
            using (var writer = new FastBufferWriter(MaximumMessageBytes, Allocator.Temp))
            {
                writer.WriteValueSafe(json);
                manager.CustomMessagingManager.SendNamedMessage(Channel, recipient, writer, NetworkDelivery.ReliableSequenced);
            }
        }

        private void ReceiveControl(ulong sender, FastBufferReader reader)
        {
            if (returning || restarting || reader.Length > MaximumMessageBytes || reader.Length < 4 || manager == null)
            {
                return;
            }
            controlTraffic.TryGetValue(sender, out var budget);
            if (Time.unscaledTime - budget.start >= 1)
            {
                budget = (Time.unscaledTime, 0);
            }
            controlTraffic[sender] = (budget.start, budget.count + 1);
            if (budget.count >= 32)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out string json);
                var message = JsonUtility.FromJson<ControlMessage>(json);
                if (message == null || message.kind < 0 || message.kind > (int)ControlKind.Hello)
                {
                    return;
                }
                if (IsHost)
                {
                    ReceiveCommand(sender, message);
                }
                else if (sender == NetworkManager.ServerClientId)
                {
                    ReceiveSnapshot(message);
                }
            }
            catch (Exception error) when (error is ArgumentException || error is OverflowException || error is InvalidOperationException)
            {
                Debug.LogWarning("対戦設定の通知を受信できませんでした: " + error.GetType().Name);
            }
        }

        private void ReceiveCommand(ulong sender, ControlMessage message)
        {
            if (flow == null || sender == NetworkManager.ServerClientId ||
                !manager.ConnectedClients.TryGetValue(sender, out var peer) || peer.PlayerObject == null ||
                peer.PlayerObject.OwnerClientId != sender || !flow.PeerPresent)
            {
                return;
            }
            var kind = (ControlKind)message.kind;
            if (kind == ControlKind.Hello && flow.Phase == SessionPhase.Waiting)
            {
                SendSnapshot(ControlKind.Offer);
                return;
            }
            if (message.session != flow.SessionId.ToString("N") || message.round != flow.Round)
            {
                return;
            }
            if (kind == ControlKind.SettingsReceived && flow.AcknowledgeSettings(PlayerId.PlayerB, flow.SessionId, message.round))
            {
                TryBegin();
            }
            else if (kind == ControlKind.Rematch && message.match == flow.MatchId.ToString("N") && CanRematch)
            {
                ApplyVote(PlayerId.PlayerB, flow.MatchId, message.round, message.agree);
            }
            else if (kind == ControlKind.Cancel && (flow.MatchId == Guid.Empty || message.match == flow.MatchId.ToString("N") ||
                (message.match == Guid.Empty.ToString("N") && (flow.Phase == SessionPhase.Loading || flow.Phase == SessionPhase.Preparing))))
            {
                ApplyPreparationCancel();
            }
            else if (kind == ControlKind.Leave && message.match == flow.MatchId.ToString("N") && flow.Phase == SessionPhase.Result)
            {
                peerLeft = true;
                resultReady = false;
                flow.DisconnectPeer();
                SendSnapshot(ControlKind.State);
                SetStatus("相手が退出しました");
            }
        }

        private void ReceiveSnapshot(ControlMessage message)
        {
            if (!Guid.TryParseExact(message.session, "N", out var sessionId) || sessionId == Guid.Empty || message.serial <= receivedSerial ||
                message.round < 1 || message.phase < 0 || message.phase > (int)SessionPhase.Closed ||
                message.mode < (int)MatchMode.Room || message.mode > (int)MatchMode.Random ||
                message.first < 0 || message.first > (int)FirstTurnSetting.PlayerB || !MatchAuthority.IsAllowedTime(message.seconds) ||
                (message.mode == (int)MatchMode.Random && (message.first != 0 || message.seconds != 30)))
            {
                return;
            }
            var kind = (ControlKind)message.kind;
            if (kind != ControlKind.Offer && kind != ControlKind.State && kind != ControlKind.Leave &&
                kind != ControlKind.Cancel && kind != ControlKind.CancelRejected)
            {
                return;
            }
            if (remote == null && kind != ControlKind.Offer)
            {
                return;
            }
            if (remote != null && (remote.session != message.session || message.round < remote.round || message.round > remote.round + 1 ||
                message.mode != remote.mode || message.first != remote.first || message.seconds != remote.seconds))
            {
                return;
            }
            bool firstOffer = remote == null;
            receivedSerial = message.serial;
            remote = message;
            remoteSetup = new MatchSetup((MatchMode)message.mode, (FirstTurnSetting)message.first, message.seconds);
            resultReady = message.resultReady;
            if (firstOffer)
            {
                settingsShownAt = Time.unscaledTime;
                settingsAcknowledged = false;
                SetStatus("ホストの設定を確認しています");
            }
            if (kind == ControlKind.Leave || (!message.peer &&
                (message.phase == (int)SessionPhase.Result || message.phase == (int)SessionPhase.Closed)))
            {
                peerLeft = true;
                resultReady = false;
                remote.voteA = remote.voteB = false;
                SetStatus("相手が退出しました");
            }
            if (kind == ControlKind.Cancel)
            {
                view?.InterruptLocally();
                RequestLeave("StartGame");
            }
            if (kind == ControlKind.CancelRejected)
            {
                SetStatus("対戦は開始済みです。退出する場合は降参を選んでください");
            }
            Notify();
        }

        private void SetStatus(string value)
        {
            Status = value;
            Notify();
        }

        private void Notify()
        {
            Changed?.Invoke();
        }
    }
}
