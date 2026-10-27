using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Waribasi.Presentation
{
    public sealed class HandPointerTarget : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        private NetworkObject hand;
        private long gesture;

        private void Awake()
        {
            hand = GetComponent<NetworkObject>();
        }

        public void OnPointerDown(PointerEventData data)
        {
            long candidate = GameInputRouter.Instance != null ? GameInputRouter.Instance.Begin(data) : 0;
            if (candidate != 0)
            {
                gesture = candidate;
            }
        }

        public void OnPointerUp(PointerEventData data)
        {
            GameInputRouter.Instance?.End(data, gesture);
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (GameInputRouter.Instance != null && GameInputRouter.Instance.Consume(gesture))
            {
                MatchInteractionController.Instance?.SelectHand(hand);
            }
        }
    }
}
