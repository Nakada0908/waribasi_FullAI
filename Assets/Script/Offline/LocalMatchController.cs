using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
//追加:最強へ現在札の定義だけをコピーする。
using System.Linq;
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
        //追加:全難易度共通の演出時間。探索の計算上限とは別に調整する。
        [SerializeField, Min(0)] private float minimumFirstActionSeconds = 0.6f;
        [SerializeField, Min(0)] private float minimumCardResultSeconds = 0.8f;
        public static LocalMatchController Instance { get; private set; }
        private readonly List<MatchHandView> hands = new List<MatchHandView>();
        private readonly List<string> recentBoards = new List<string>();
        private RuleRandom choices;
        private Coroutine thinking;
        //追加:上級の探索と推定は試合単位、予算はカード後も同じ手番で共有する。
        private AdvancedNpc advancedThinking;
        private OpponentHandEstimator handEstimator;
        //変更:旧手番開始時刻からの経過計測を、計算と演出待機の別計測へ置き換える。
        //削除:private long thinkingTurn = -1;
        //削除:private double turnThinkStarted;
        private NpcTurnTiming turnTiming = new NpcTurnTiming();
        private int turnApplications;
        public NpcSearchStatistics LastSearchStatistics { get; private set; }
        public double LastThinkWallMilliseconds { get; private set; }
        public double LastTurnThinkMilliseconds { get; private set; }
        //追加:計算後の演出待ちを、CPU・分割探索の時間と分けて公開する。
        public double LastPresentationWaitMilliseconds { get; private set; }
        public double LastTurnWaitMilliseconds => turnTiming.WaitMilliseconds;
        public bool IsThinking => thinking != null;
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
            //追加:このゲーム専用キーを読み、再起動後も解放を維持する。
            NpcMatchEntry.ConfigureProgress(new NpcUnlockProgress(new PlayerPrefsNpcUnlockStore()));
            NpcMatchEntry.Register(StartNpc);
            SceneManager.sceneLoaded += OnSceneLoaded;
            //追加:外部のScene遷移でも古い探索を停止する。
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                //追加:破棄後に探索結果や仮説を残さない。
                generation++;
                StopThinking();
                NpcMatchEntry.Unregister(StartNpc);
                SceneManager.sceneLoaded -= OnSceneLoaded;
                //追加:探索寿命の購読を解除する。
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
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
            //追加:登録済み入口を迂回した呼び出しも拒否する。
            if (setup.Difficulty == NpcDifficulty.Strongest && !NpcMatchEntry.IsStrongestUnlocked)
            {
                return NpcStartResult.Locked;
            }
            var online = OnlineSessionController.Instance;
            if (Active || IsWorking || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ||
                (online != null && (online.IsWorking || online.HasSession)))
            {
                return NpcStartResult.Busy;
            }
            //変更:上級も専用探索へ接続して開始する。
            //削除:上級未実装時の開始拒否を保存する。
            //if (setup.Difficulty == NpcDifficulty.Advanced)
            //{
            //    return NpcStartResult.UnsupportedDifficulty;
            //}
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
            //追加:再戦で推定と手番予算を初期化する。
            handEstimator = new OpponentHandEstimator();
            //変更:再戦では待機期限・計算予算・待機計測も新しい試合用に初期化する。
            //削除:thinkingTurn = -1;
            turnTiming = new NpcTurnTiming();
            turnApplications = 0;
            LastSearchStatistics = null;
            LastThinkWallMilliseconds = LastTurnThinkMilliseconds = 0;
            //追加:前試合の待機時間を表示や検証へ残さない。
            LastPresentationWaitMilliseconds = 0;
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
            //追加:Scene読み込み中に探索を継続しない。
            StopThinking();
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
                        //変更:オンラインと同じ席・左右の配置と回転を使う。
                        MatchHandLayout.Place(hand.transform, (PlayerId)player, (HandSide)side);
                        //削除:配置と回転の二重実装を保存する。
                        //hand.transform.position = new Vector3(3 * Mathf.Abs(side - player), 0, player * 10);
                        //hand.transform.rotation = Quaternion.Euler(0, player * 180, 0);
                        var identity = hand.GetComponent<MatchHandView>();
                        identity.BindLocal((PlayerId)player, (HandSide)side);
                        hands.Add(identity);
                    }
                }
                var camera = Instantiate(cameraPrefab);
                //追加:NPC戦も画面比率による手の見切れを防ぐ。
                MatchCameraViewport.Attach(camera);
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
            //追加:受付開始時を起点にする。直前の攻撃演出中は次のNPCの待ち時間に数えない。
            ObserveNpcTurn();
            //変更:自動スキップも通常のNPC行動と同じ最低待ちを通す。
            //削除:if (authority.Tick(Now))
            bool allowAutomaticSkip = authority.State.CurrentPlayer != Session.Npc ||
                turnTiming.CanAct(authority.State.TurnNumber, Now);
            if (authority.Tick(Now, allowAutomaticSkip))
            {
                ApplyPoses();
                Publish();
            }
            //追加:Tickが前の演出を閉じて受付を開いたフレームから計測する。
            ObserveNpcTurn();
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

        //追加:カード後の再判断では同じ手番の計算残量と表示期限を維持する。
        private void ObserveNpcTurn()
        {
            var authority = Session.Authority;
            if (authority.State.MatchPhase == MatchPhase.Active && authority.Gate == InputGate.Accepting &&
                authority.State.CurrentPlayer == Session.Npc && turnTiming.TurnNumber != authority.State.TurnNumber)
            {
                turnTiming.ObserveTurn(authority.State.TurnNumber, Now, minimumFirstActionSeconds);
                turnApplications = 0;
            }
        }

        //追加:Scene離脱時は、正式適用を待たずに探索を破棄する。
        private void OnSceneUnloaded(Scene scene)
        {
            if (scene.name == "Game")
            {
                generation++;
                StopThinking();
            }
        }

        //追加:無効化中の探索を残さない。
        private void OnDisable()
        {
            generation++;
            StopThinking();
        }

        //変更:難易度を明示し、上級はフレームごとに探索を進める。
        private IEnumerator Think(int epoch)
        {
            var authority = Session.Authority;
            var match = authority.MatchId;
            var observation = authority.GetObservation(Session.Npc);
            //変更:カード後には初期待機と手番全体の予算をリセットしない。
            //変更:思考を先に実行し、最初の行動期限まで残った分だけ後で待つ。
            //削除:旧手番開始からの予算算出を保存する。
            //bool newTurn = thinkingTurn != observation.PublicState.TurnNumber;
            //if (newTurn)
            //{
            //    thinkingTurn = observation.PublicState.TurnNumber;
            //    turnThinkStarted = Now;
            //    turnApplications = 0;
            //}
            //削除:カード後も繰り返していた待機。
            //yield return new WaitForSecondsRealtime(0.3f);
            //削除:yield return new WaitForSecondsRealtime(newTurn ? 0.3f : 0);
            //追加:同期思考でもStartCoroutineの戻り前に完了させず、停止用ハンドルを確実に保持する。
            yield return null;
            if (!CanThink(epoch, match, observation.PublicState.StateVersion))
            {
                thinking = null;
                yield break;
            }
            //追加:初回のフレーム待ちを計算予算へ含めない。
            double calledAt = Now;
            var timer = Stopwatch.StartNew();
            GameAction action = null;
            if (Session.Setup.Difficulty == NpcDifficulty.Beginner)
            {
                action = BeginnerNpc.Choose(observation, ref choices, beginnerCardPercent);
                LastCandidateCount = 0;
            }
            //変更:中級へ流れる条件を明示する。
            //削除:従来はBeginner以外が中級だった。
            //else
            else if (Session.Setup.Difficulty == NpcDifficulty.Intermediate)
            {
                var decision = IntermediateNpc.Choose(observation, ref choices, recentBoards);
                action = decision.Action;
                LastCandidateCount = decision.CandidateCount;
            }
            //追加:上級は観測だけを渡し、各処理片の前に現在の試合・版・手番を検証する。
            //変更:最強も同じ予算と停止条件で探索し、情報の入口だけを分ける。
            else if (Session.Setup.Difficulty == NpcDifficulty.Advanced || Session.Setup.Difficulty == NpcDifficulty.Strongest)
            {
                var options = new NpcSearchOptions
                {
                    //変更:カード結果の表示待ちを手番全体1秒の計算予算から差し引かない。
                    //削除:MaxMilliseconds = Math.Max(1, Math.Min(600, (turnThinkStarted + 1 - Now) * 1000)),
                    MaxMilliseconds = Math.Min(600, turnTiming.RemainingThinkMilliseconds),
                    MaxApplications = Math.Max(1, 40000 - turnApplications)
                };
                //追加:相手観測は最強の分岐だけで読み、札の定義以外を探索へ渡さない。
                if (Session.Setup.Difficulty == NpcDifficulty.Strongest)
                {
                    var strongestOptions = StrongestNpc.CreateOptions();
                    strongestOptions.MaxMilliseconds = options.MaxMilliseconds;
                    strongestOptions.MaxApplications = options.MaxApplications;
                    var currentCards = authority.GetObservation(Session.Human).OwnHand.Select(card => card.Definition).ToArray();
                    advancedThinking = StrongestNpc.CreateSearch(observation, currentCards, choices, strongestOptions, recentBoards);
                }
                else
                {
                    advancedThinking = new AdvancedNpc(observation, choices, options, recentBoards, handEstimator);
                }
                while (!advancedThinking.IsComplete)
                {
                    if (!CanThink(epoch, match, observation.PublicState.StateVersion))
                    {
                        StopThinking();
                        yield break;
                    }
                    advancedThinking.Advance();
                    if (!advancedThinking.IsComplete)
                    {
                        yield return null;
                    }
                }
                action = advancedThinking.Decision.Action;
                choices = advancedThinking.ChoiceRandom;
                LastSearchStatistics = advancedThinking.Statistics;
                turnApplications += LastSearchStatistics.Applications;
                LastCandidateCount = LastSearchStatistics.Candidates;
                LastThinkMilliseconds = LastSearchStatistics.CpuMilliseconds;
                advancedThinking.Dispose();
                advancedThinking = null;
            }
            timer.Stop();
            //変更:上級のCPU時間とフレーム待ち込みの時間を区別する。
            //削除:同期思考のみの計測。
            //LastThinkMilliseconds = timer.Elapsed.TotalMilliseconds;
            //変更:最強の分割探索もCPU時間の計測値を維持する。
            if (Session.Setup.Difficulty != NpcDifficulty.Advanced && Session.Setup.Difficulty != NpcDifficulty.Strongest)
            {
                LastThinkMilliseconds = timer.Elapsed.TotalMilliseconds;
            }
            LastThinkWallMilliseconds = (Now - calledAt) * 1000;
            //変更:分割探索中のフレーム待ちは計算経過に含め、結果を見せる待機は別に記録する。
            //削除:LastTurnThinkMilliseconds = (Now - turnThinkStarted) * 1000;
            turnTiming.RecordThinking(LastThinkWallMilliseconds);
            LastTurnThinkMilliseconds = turnTiming.ThinkMilliseconds;
            //追加:計算が最低待ちより長ければ追加で丸ごとの待機をしない。
            double waitStarted = Now;
            while (!turnTiming.CanAct(observation.PublicState.TurnNumber, Now))
            {
                if (!CanThink(epoch, match, observation.PublicState.StateVersion))
                {
                    StopThinking();
                    yield break;
                }
                yield return null;
            }
            LastPresentationWaitMilliseconds = (Now - waitStarted) * 1000;
            turnTiming.RecordWaiting(LastPresentationWaitMilliseconds);
            thinking = null;
            //変更:現在のSessionを検証し、古いAuthorityだけの比較にしない。
            //削除:従来の適用前条件を保存する。
            //if (Active && epoch == generation && authority.MatchId == match && !IsWorking && action != null &&
            //    authority.State.StateVersion == observation.PublicState.StateVersion &&
            //    authority.State.CurrentPlayer == Session.Npc && authority.Gate == InputGate.Accepting)
            if (action != null && CanThink(epoch, match, observation.PublicState.StateVersion))
            {
                Apply(Session.Npc, action, observation.PublicState.StateVersion);
            }
        }

        //追加:分割探索の開始・再開・正式適用に同じ寿命条件を使う。
        private bool CanThink(int epoch, Guid match, long version)
        {
            return Active && epoch == generation && isActiveAndEnabled && !IsWorking &&
                SceneManager.GetActiveScene().name == "Game" && Session.Authority.MatchId == match &&
                Session.Authority.State.MatchPhase == MatchPhase.Active &&
                Session.Authority.State.StateVersion == version &&
                Session.Authority.State.CurrentPlayer == Session.Npc && Session.Authority.Gate == InputGate.Accepting;
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
            //追加:札と変化した盤面を配信した後から最低表示時間を数える。
            if (Active && actor == Session.Npc && action is UseCardAction)
            {
                turnTiming.CardPublished(Now, minimumCardResultSeconds);
            }
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
            //追加:停止直前の世代で取得した判断も、正式適用できない状態にする。
            generation++;
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
            //追加:退出時に分割探索も破棄する。
            StopThinking();
            StopAllCoroutines();
            thinking = null;
            NpcMatchEntry.ReturnToPreparation = destination == "StartGame";
            Session = null;
            View.ResetForNewMatch();
            preparing = loading = leaving = false;
            hands.Clear();
            if (destination == "Quit")
            {
                //追加:Web版の終了もオンラインと揃え、破棄済みの対戦画面に残さない。
#if UNITY_WEBGL && !UNITY_EDITOR
                SceneManager.LoadSceneAsync("Title");
#else
                Application.Quit();
#endif
            }
            else
            {
                SceneManager.LoadSceneAsync(destination);
            }
            Changed?.Invoke();
        }

        private void StopThinking()
        {
            //追加:停止した探索の結果は次回の試合や思考へ引き継がない。
            advancedThinking?.Dispose();
            advancedThinking = null;
            if (thinking != null)
            {
                StopCoroutine(thinking);
                thinking = null;
            }
        }
    }
}
