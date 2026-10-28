using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;

namespace Waribasi.Presentation
{
    [DisallowMultipleComponent]
    public sealed class PanelFade : MonoBehaviour
    {
        private CanvasGroup group;
        private CancellationTokenSource cancellation;
        private MotionHandle motion;
        private bool desired;
        private string contentKey;
        private int revision;
        private float visibleAlpha;
        private bool interactable;
        private bool blocksRaycasts;

        public static PanelFade For(GameObject target)
        {
            if (target == null)
            {
                return null;
            }
            var fade = target.GetComponent<PanelFade>();
            if (fade == null)
            {
                fade = target.AddComponent<PanelFade>();
            }
            fade.Initialize();
            return fade;
        }

        private void Initialize()
        {
            if (group != null)
            {
                return;
            }
            group = GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = gameObject.AddComponent<CanvasGroup>();
            }
            visibleAlpha = group.alpha;
            interactable = group.interactable;
            blocksRaycasts = group.blocksRaycasts;
            desired = gameObject.activeSelf;
        }

        public void SetVisible(bool visible, Action hidden = null, bool blockWhileHiding = true)
        {
            Initialize();
            if (desired == visible && (cancellation != null || gameObject.activeSelf == visible))
            {
                if (!visible && !gameObject.activeSelf)
                {
                    hidden?.Invoke();
                }
                return;
            }
            StartFade(visible, false, hidden);
            if (!visible && !blockWhileHiding && group != null)
            {
                group.blocksRaycasts = false;
            }
        }

        public void ShowContent(string key)
        {
            Initialize();
            if (contentKey == key && desired && gameObject.activeSelf)
            {
                return;
            }
            contentKey = key;
            StartFade(true, true, null);
        }

        private void StartFade(bool visible, bool restart, Action hidden)
        {
            Stop();
            desired = visible;
            if (!gameObject.activeSelf && !visible)
            {
                hidden?.Invoke();
                return;
            }
            bool wasActive = gameObject.activeSelf;
            gameObject.SetActive(true);
            if (restart || !wasActive)
            {
                group.alpha = 0;
            }
            group.interactable = false;
            PresentationVisibility.Set(this, true);
            cancellation = new CancellationTokenSource();
            Run(visible, revision, cancellation.Token, hidden).Forget();
        }

        private async UniTask Run(bool visible, int epoch, CancellationToken token, Action hidden)
        {
            try
            {
                motion = LMotion.Create(group.alpha, visible ? visibleAlpha : 0, SceneFade.Settings.PanelSeconds)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .BindToAlpha(group);
                await motion.ToUniTask(CancelBehavior.Cancel, token);
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
                if (this != null && group != null && epoch == revision)
                {
                    if (motion.IsActive())
                    {
                        motion.Cancel();
                    }
                    group.alpha = visibleAlpha;
                    group.interactable = interactable;
                    group.blocksRaycasts = blocksRaycasts;
                    var completed = cancellation;
                    cancellation = null;
                    completed?.Dispose();
                    PresentationVisibility.Set(this, false);
                    if (!visible)
                    {
                        gameObject.SetActive(false);
                        hidden?.Invoke();
                    }
                }
            }
        }

        private void Stop()
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
            PresentationVisibility.Set(this, false);
        }

        public void Finish()
        {
            Stop();
            if (group != null)
            {
                group.alpha = visibleAlpha;
                group.interactable = interactable;
                group.blocksRaycasts = blocksRaycasts;
            }
        }

        private void OnDisable()
        {
            Stop();
            desired = false;
            contentKey = null;
            if (group != null)
            {
                group.alpha = visibleAlpha;
                group.interactable = interactable;
                group.blocksRaycasts = blocksRaycasts;
            }
        }
    }
}
