using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Waribasi.Presentation
{
    public sealed class MatchPointerButton : Button
    {
        private long gesture;

        public override void OnPointerDown(PointerEventData data)
        {
            if (!IsActive() || !IsInteractable())
            {
                return;
            }
            long candidate = GameInputRouter.Instance != null ? GameInputRouter.Instance.Begin(data) : 0;
            if (candidate != 0)
            {
                gesture = candidate;
                base.OnPointerDown(data);
            }
        }

        public override void OnPointerUp(PointerEventData data)
        {
            GameInputRouter.Instance?.End(data, gesture);
            base.OnPointerUp(data);
        }

        public override void OnPointerClick(PointerEventData data)
        {
            if (GameInputRouter.Instance != null && GameInputRouter.Instance.Consume(gesture))
            {
                base.OnPointerClick(data);
            }
        }

        public override void OnSubmit(BaseEventData data)
        {
            if (GameInputRouter.Instance != null && GameInputRouter.Instance.ConsumeKeyboard())
            {
                base.OnSubmit(data);
            }
        }
    }
}
