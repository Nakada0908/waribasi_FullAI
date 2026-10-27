using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Waribasi.MatchFlow;
using Waribasi.Networking;
using Waribasi.Npc;
using Waribasi.Rules;

namespace Waribasi.Presentation
{
    public sealed class LocalMatchController : MonoBehaviour
    {
        [SerializeField] private GameObject leftHandPrefab;
        [SerializeField] private GameObject rightHandPrefab;
        [SerializeField] private Camera cameraPrefab;
        [SerializeField, Range(0, 100)] private int beginnerCardPercent = 25;
        public static LocalMatchController Instance { get; private set; }
        private readonly List<MatchHandView> hands = new List<MatchHandView>();
        private readonly List<string> recentBoards = new List<string>();
        private RuleRandom choices;
        private Coroutine thinking;
        private bool loading;
        private bool preparing;
        private int generation;
        private int stage;
        private MatchHandView moving;
        private Vector3 moveOrigin;
        private Vector3 moveContact;
        private double moveAt;
        private double endedAt;
        private bool leaving;
        public LocalMatchSession Session { get; private set; }
        public MatchClientState View { get; private set; }
        public bool Active => Session != null;
        public bool IsWorking => loading || preparing || leaving;
        public bool CanRematch => Active && !IsWorking && View.HasConfirmedResult && SceneManager.GetActiveScene().name == "End";
        public double LastThinkMilliseconds { get; private set; }
        public int LastCandidateCount { get; private set; }
        public event Action Changed;
        private double Now => Time.realtimeSinceStartupAsDouble;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            NpcMatchEntry.Register(StartNpc);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                NpcMatchEntry.Unregister(StartNpc);
                SceneManager.sceneLoaded -= OnSceneLoaded;
                Instance = null;
            }
        }

        private PlayerId RandomFirst()
        {
            var random = RuleRandom.Create();
            return random.Next(2, out _) == 0 ? PlayerId.PlayerA : PlayerId.PlayerB;
        }

        private NpcStartResult StartNpc(NpcMatchSetup setup)
        {
            var online = OnlineSessionController.Instance;
            if (Active || IsWorking || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ||
                (online != null && (online.IsWorking || online.HasSession)))
            {
                return NpcStartResult.Busy;
            }
            if (setup.Difficulty == NpcDifficulty.Advanced)
            {
                return NpcStartResult.UnsupportedDifficulty;
            }
            if (leftHandPrefab == null || rightHandPrefab == null || cameraPrefab == null)
            {
                return NpcStartResult.NotConnected;
            }
            Session = new LocalMatchSession(setup, RandomFirst());
            if (View == null)
            {
                View = gameObject.AddComponent<MatchClientState>();
            }
            BeginRound();
            return NpcStartResult.Accepted;
        }

        private void BeginRound()
        {
            StopThinking();
            generation++;
            choices = RuleRandom.Create();
            LastThinkMilliseconds = 0;
            LastCandidateCount = 0;
            recentBoards.Clear();
            hands.Clear();
            moving = null;
            stage = 0;
            endedAt = -1;
            View.ResetForNewMatch();
            Publish();
            Load("Game");
        }

        private void Load(string scene)
        {
            loading = true;
            SceneManager.LoadSceneAsync(scene);
            Changed?.Invoke();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Active)
            {
                return;
            }
            loading = false;
            if (scene.name == "Game")
            {
                for (int player = 0; player < 2; player++)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        var hand = Instantiate(side == 0 ? leftHandPrefab : rightHandPrefab);
                        SceneManager.MoveGameObjectToScene(hand, scene);
                        hand.transform.position = new Vector3(3 * Mathf.Abs(side - player), 0, player * 10);
                        hand.transform.rotation = Quaternion.Euler(0, player * 180, 0);
                        var identity = hand.GetComponent<MatchHandView>();
                        identity.BindLocal((PlayerId)player, (HandSide)side);
                        hands.Add(identity);
                    }
                }
                var camera = Instantiate(cameraPrefab);
                camera.transform.SetPositionAndRotation(new Vector3(1.5f, 3, 2), Quaternion.Euler(50, 0, 0));
                SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                StartCoroutine(Prepare(generation));
            }
            Changed?.Invoke();
        }

        private IEnumerator Prepare(int epoch)
        {
            preparing = true;
            ApplyPoses();
            Publish();
            yield return new WaitForSecondsRealtime(0.8f);
            if (!Active || epoch != generation || leaving)
            {
                yield break;
            }
            Session.Authority.DealInitialCards();
            stage = 1;
            Publish();
            yield return new WaitForSecondsRealtime(0.8f);
            if (!Active || epoch != generation || leaving)
            {
                yield break;
            }
            Session.Authority.Start(Now);
            preparing = false;
            stage = 2;
            Publish();
        }

        private void Update()
        {
            if (!Active || IsWorking || SceneManager.GetActiveScene().name != "Game")
            {
                return;
            }
            var authority = Session.Authority;
            Animate();
            if (authority.Tick(Now))
            {
                ApplyPoses();
                Publish();
            }
            if (authority.State.MatchPhase == MatchPhase.Finished)
            {
                if (endedAt < 0)
                {
                    endedAt = Now;
                }
                if (Now - endedAt >= 1.05)
                {
                    hands.Clear();
                    Load("End");
                }
            }
            else if (authority.Gate == InputGate.Accepting && authority.State.CurrentPlayer == Session.Npc && thinking == null)
            {
                thinking = StartCoroutine(Think(generation));
            }
        }

        private IEnumerator Think(int epoch)
        {
            var authority = Session.Authority;
            var match = authority.MatchId;
            var observation = authority.GetObservation(Session.Npc);
            yield return new WaitForSecondsRealtime(0.3f);
            var timer = Stopwatch.StartNew();
            GameAction action;
            if (Session.Setup.Difficulty == NpcDifficulty.Beginner)
            {
                action = BeginnerNpc.Choose(observation, ref choices, beginnerCardPercent);
                LastCandidateCount = 0;
            }
            else
            {
                var decision = IntermediateNpc.Choose(observation, ref choices, recentBoards);
                action = decision.Action;
                LastCandidateCount = decision.CandidateCount;
            }
            timer.Stop();
            LastThinkMilliseconds = timer.Elapsed.TotalMilliseconds;
            thinking = null;
            if (Active && epoch == generation && authority.MatchId == match && !IsWorking && action != null &&
                authority.State.StateVersion == observation.PublicState.StateVersion &&
                authority.State.CurrentPlayer == Session.Npc && authority.Gate == InputGate.Accepting)
            {
                Apply(Session.Npc, action, observation.PublicState.StateVersion);
            }
        }

        public bool Send(GameAction action, long version)
        {
            return Active && !IsWorking && View.CanAct && Apply(Session.Human, action, version);
        }

        private bool Apply(PlayerId actor, GameAction action, long version)
        {
            var response = Session.Submit(actor, action, version, Now);
            if (!response.Accepted)
            {
                Publish();
                return false;
            }
            if (action is AttackAction attack)
            {
                moving = hands.Find(hand => hand.TryGetPlayer(out var player) && player == actor && hand.Side == attack.Source);
                var target = hands.Find(hand => hand.TryGetPlayer(out var player) && player != actor && hand.Side == attack.Target);
                moveOrigin = moving.Home;
                var from = moving.transform.Find("targetpos");
                var to = target.transform.Find("targetpos");
                moveContact = moveOrigin + to.position - from.position;
                moveAt = Now;
            }
            else
            {
                ApplyPoses();
            }
            if (!(action is UseCardAction))
            {
                recentBoards.Add(NpcSimulation.BoardKey(Session.Authority.State));
                if (recentBoards.Count > 8)
                {
                    recentBoards.RemoveAt(0);
                }
            }
            Publish();
            return true;
        }

        private void Animate()
        {
            if (moving == null)
            {
                return;
            }
            float progress = (float)((Now - moveAt) / MatchAuthority.PresentationSeconds);
            if (progress >= 1)
            {
                moving.transform.position = moveOrigin;
                moving = null;
                ApplyPoses();
                return;
            }
            moving.transform.position = progress < 0.5f ? Vector3.Lerp(moveOrigin, moveContact, progress * 2) :
                Vector3.Lerp(moveContact, moveOrigin, (progress - 0.5f) * 2);
        }

        private void ApplyPoses()
        {
            foreach (var hand in hands)
            {
                if (hand == null || !hand.TryGetPlayer(out var player))
                {
                    continue;
                }
                int count = Session.Authority.State.GetHands(player).GetCount(hand.Side);
                hand.GetComponentInChildren<HandFingerController>().SetFinger(count == 0 ? 5 : count);
                var stay = hand.transform.Find("staypos");
                hand.transform.position = count == 0 ? hand.Home + hand.transform.TransformVector(stay.localPosition) : hand.Home;
            }
        }

        private void Publish()
        {
            View.ApplyLocal(Session.Authority, Session.Human, stage, Now);
            Changed?.Invoke();
        }

        public bool Surrender()
        {
            if (!Active || IsWorking || Session.Authority.State.MatchPhase != MatchPhase.Active)
            {
                return false;
            }
            StopThinking();
            var result = Session.Surrender(Session.Authority.State.StateVersion, Now);
            Publish();
            return result.Accepted;
        }

        public void Rematch()
        {
            if (CanRematch)
            {
                Session.Restart(RandomFirst());
                BeginRound();
            }
        }

        public void Leave(string destination)
        {
            if (!Active || leaving || (destination != "StartGame" && destination != "Title" && destination != "Quit"))
            {
                return;
            }
            if (Session.Authority.State.MatchPhase == MatchPhase.Active)
            {
                Surrender();
            }
            leaving = true;
            generation++;
            StopAllCoroutines();
            thinking = null;
            NpcMatchEntry.ReturnToPreparation = destination == "StartGame";
            Session = null;
            View.ResetForNewMatch();
            preparing = loading = leaving = false;
            hands.Clear();
            if (destination == "Quit")
            {
                Application.Quit();
            }
            else
            {
                SceneManager.LoadSceneAsync(destination);
            }
            Changed?.Invoke();
        }

        private void StopThinking()
        {
            if (thinking != null)
            {
                StopCoroutine(thinking);
                thinking = null;
            }
        }
    }
}
