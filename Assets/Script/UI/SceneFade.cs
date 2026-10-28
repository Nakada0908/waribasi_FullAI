using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Waribasi.Presentation
{
    [DefaultExecutionOrder(-1000)]
    public sealed class SceneFade : MonoBehaviour
    {
        private static SceneFade instance;
        private static FadeSettings settings;
        private CanvasGroup cover;
        private CancellationTokenSource cancellation;
        private MotionHandle motion;
        private bool busy;
        private bool localLoading;
        private double coverDeadline;
        private int revision;

        public static bool InputBlocked => instance != null && instance.busy;
        public static FadeSettings Settings
        {
            get
            {
                if (settings == null)
                {
                    settings = Resources.Load<FadeSettings>("T20FadeSettings");
                    if (settings == null)
                    {
                        settings = ScriptableObject.CreateInstance<FadeSettings>();
                    }
                }
                return settings;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            settings = null;
            PresentationVisibility.Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            Ensure();
        }

        private static SceneFade Ensure()
        {
            if (instance == null)
            {
                new GameObject("SceneFade").AddComponent<SceneFade>();
            }
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            var canvasObject = new GameObject("FadeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            cover = canvasObject.GetComponent<CanvasGroup>();
            var imageObject = new GameObject("Cover", typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(canvasObject.transform, false);
            var rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            imageObject.GetComponent<Image>().color = Color.black;
            busy = true;
            cover.alpha = 1;
            cover.blocksRaycasts = true;
            coverDeadline = Time.realtimeSinceStartupAsDouble + Settings.NetworkCoverTimeoutSeconds;
            PresentationVisibility.Set(this, true);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private CancellationToken Begin()
        {
            CancelMotion();
            busy = true;
            cover.blocksRaycasts = true;
            coverDeadline = Time.realtimeSinceStartupAsDouble + Settings.NetworkCoverTimeoutSeconds;
            PresentationVisibility.Set(this, true);
            cancellation = new CancellationTokenSource();
            return cancellation.Token;
        }

        private async UniTask Tween(float alpha, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (cover == null)
            {
                throw new OperationCanceledException();
            }
            motion = LMotion.Create(cover.alpha, alpha, Settings.SceneSeconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .BindToAlpha(cover);
            await motion.ToUniTask(CancelBehavior.Cancel, token);
            token.ThrowIfCancellationRequested();
            if (cover != null)
            {
                cover.alpha = alpha;
            }
        }

        public static async UniTask<bool> LoadLocalAsync(string scene, CancellationToken owner = default)
        {
            var fade = Ensure();
            if (fade.busy || owner.IsCancellationRequested)
            {
                return false;
            }
            var token = fade.Begin();
            int epoch = fade.revision;
            bool loaded = false;
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, owner))
                {
                    await fade.Tween(1, linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                }
                fade.localLoading = true;
                var load = SceneManager.LoadSceneAsync(scene);
                if (load == null)
                {
                    throw new InvalidOperationException("Scene load was not started: " + scene);
                }
                //開始済みのUnityロード自体は取り消せない。旧Sceneの破棄後も常駐側で表示を戻す。
                await load.ToUniTask(cancellationToken: token);
                loaded = true;
                await UniTask.NextFrame(cancellationToken: token);
                await fade.Tween(0, token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            finally
            {
                if (fade != null && epoch == fade.revision)
                {
                    fade.Release();
                }
            }
            return loaded;
        }

        public static async UniTask CoverNetworkAsync(CancellationToken owner)
        {
            var fade = Ensure();
            //同じホスト通知とNGO Loadイベントが重なっても暗転をやり直さない。
            if (!fade.busy)
            {
                fade.Cover().Forget();
            }
            await UniTask.WaitUntil(() => fade == null || !fade.busy || fade.cover == null || fade.cover.alpha >= 0.999f,
                cancellationToken: owner);
            owner.ThrowIfCancellationRequested();
            if (fade == null || !fade.busy || fade.cover == null)
            {
                throw new OperationCanceledException();
            }
        }

        public static void BeginNetworkCover()
        {
            var fade = Ensure();
            if (!fade.busy)
            {
                fade.Cover().Forget();
            }
        }

        private async UniTask Cover()
        {
            var token = Begin();
            int epoch = revision;
            try
            {
                await Tween(1, token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (this != null && epoch == revision)
                {
                    Release();
                }
            }
        }

        public static void AbortNetworkCover()
        {
            if (instance != null && !instance.localLoading)
            {
                instance.Release();
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (isActiveAndEnabled && !localLoading && mode == LoadSceneMode.Single)
            {
                Reveal().Forget();
            }
        }

        private async UniTask Reveal()
        {
            var token = Begin();
            int epoch = revision;
            try
            {
                await UniTask.NextFrame(cancellationToken: token);
                await Tween(0, token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            finally
            {
                if (this != null && epoch == revision)
                {
                    Release();
                }
            }
        }

        private void Update()
        {
            if (busy && !localLoading && Time.realtimeSinceStartupAsDouble >= coverDeadline)
            {
                Debug.LogWarning("画面遷移の通知が完了しなかったため、フェードの入力遮断を解除しました。");
                Release();
            }
        }

        private void CancelMotion()
        {
            revision++;
            var previous = cancellation;
            cancellation = null;
            if (previous != null)
            {
                previous.Cancel();
                previous.Dispose();
            }
            if (motion.IsActive())
            {
                motion.Cancel();
            }
        }

        private void Release()
        {
            CancelMotion();
            busy = localLoading = false;
            if (cover != null)
            {
                cover.alpha = 0;
                cover.blocksRaycasts = false;
            }
            PresentationVisibility.Set(this, false);
        }

        private void OnDisable()
        {
            Release();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Release();
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
