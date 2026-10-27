using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Waribasi.Presentation
{
    public sealed class HandPointerTarget : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        //変更:ネット未接続の手も同じポインター操作へ接続する。
        private MatchHandView hand;
        private long gesture;

        private void Awake()
        {
            //変更:既存オンラインPrefabにも共通の手識別を用意する。
            hand = GetComponent<MatchHandView>() ?? gameObject.AddComponent<MatchHandView>();
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
                //変更:通信生成の有無によらず同じ選択入口を使う。
                MatchInteractionController.Instance?.SelectMatchHand(hand);
            }
        }
    }
}

//削除:T06の共通入力・ローカル接続へ移行する前の箇所を参照用に保存する。
//        private NetworkObject hand;
//            hand = GetComponent<NetworkObject>();
//MatchInteractionController.Instance?.SelectHand(hand);
