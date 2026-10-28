using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Waribasi.Presentation
{
    [DefaultExecutionOrder(-200)]
    public sealed class GameInputRouter : MonoBehaviour
    {
        public static GameInputRouter Instance { get; private set; }
        [SerializeField, Range(0, 0.75f)] private float touchMouseSuppression = 0.35f;
        private long nextGesture;
        private long heldGesture;
        private long releasedGesture;
        private long consumedGesture;
        private int heldPointer;
        private int releasedFrame = -1;
        private int keyboardFrame = -1;
        private float lastTouch = -10;
        private bool holding;
        //追加:新しいSceneとフォーカス復帰の入力を、押下解除と初期処理の後に受け付ける。
        private bool pointerReady;
        private int pointerReadyAfterFrame;
        private readonly HashSet<int> rejectedPointers = new HashSet<int>();

        //変更:直接参照する画面別のキー処理も暗転中は実行しない。
        public static bool StartPressed => !SceneFade.InputBlocked && Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
        //変更:直接参照する画面別のキー処理も暗転中は実行しない。
        public static bool RestartPressed => !SceneFade.InputBlocked && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        //変更:直接参照する画面別のキー処理も暗転中は実行しない。
        public static bool EscapePressed => !SceneFade.InputBlocked && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        public static bool EscapeReservedForText { get; private set; }
        public static bool IsEditingText => EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() is TMPro.TMP_InputField field && field.isFocused;

        private void Awake()
        {
            Instance = this;
            //追加:前の画面を開いた押下を、新しい画面のクリックとして扱わない。
            pointerReadyAfterFrame = Time.frameCount;
        }

        //追加:プロジェクト全体で有効なPointを、新しいSceneのUIへ再通知する。
        private void Start()
        {
            var point = GetComponent<InputSystemUIInputModule>()?.point?.action;
            if (point != null && point.enabled && (Mouse.current != null || Pen.current != null))
            {
                point.Disable();
                point.Enable();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public long Begin(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left)
            {
                return 0;
            }
            bool touch = data is ExtendedPointerEventData extended && extended.pointerType == UIPointerType.Touch;
            if (touch)
            {
                lastTouch = Time.unscaledTime;
            }
            //変更:Scene切替直後に残った入力も重複操作として拒否する。
            //変更:暗転中の押下を新しい画面へ持ち越さない。
            if (SceneFade.InputBlocked || !pointerReady || holding || rejectedPointers.Count > 0 ||
                (!touch && Time.unscaledTime - lastTouch < touchMouseSuppression))
            {
                rejectedPointers.Add(data.pointerId);
                return 0;
            }
            holding = true;
            heldPointer = data.pointerId;
            heldGesture = ++nextGesture;
            return heldGesture;
        }

        public void End(PointerEventData data, long gesture)
        {
            rejectedPointers.Remove(data.pointerId);
            if (gesture == 0 || gesture != heldGesture || data.pointerId != heldPointer)
            {
                return;
            }
            holding = false;
            releasedGesture = gesture;
            releasedFrame = Time.frameCount;
        }

        public bool Consume(long gesture)
        {
            //変更:暗転開始前に離された操作も、遷移中は確定しない。
            if (SceneFade.InputBlocked || gesture == 0 || gesture != releasedGesture || gesture == consumedGesture || releasedFrame != Time.frameCount)
            {
                return false;
            }
            consumedGesture = gesture;
            return true;
        }

        public bool ConsumeKeyboard()
        {
            //変更:新しい画面の初期処理中は、引き継がれたSubmitも実行しない。
            //変更:ポインターと同じ遷移ガードをSubmitにも適用する。
            if (SceneFade.InputBlocked || !pointerReady || holding || keyboardFrame == Time.frameCount)
            {
                return false;
            }
            keyboardFrame = Time.frameCount;
            return true;
        }

        private void Update()
        {
            EscapeReservedForText = EscapePressed && IsEditingText;
            //追加:フェードを跨いだ長押しは、解除後の新しい押下まで受け付けない。
            if (SceneFade.InputBlocked)
            {
                pointerReady = false;
                pointerReadyAfterFrame = Time.frameCount;
                holding = false;
                heldGesture = releasedGesture = 0;
            }
        }

        private void LateUpdate()
        {
            bool touchHeld = false;
            if (Touchscreen.current != null)
            {
                foreach (var touch in Touchscreen.current.touches)
                {
                    touchHeld |= touch.press.isPressed;
                }
            }
            if (!touchHeld && (Mouse.current == null || !Mouse.current.leftButton.isPressed) &&
                (Pen.current == null || !Pen.current.tip.isPressed))
            {
                holding = false;
                rejectedPointers.Clear();
                //追加:少なくとも次のフレームで全ポインターが離れてから、新しい操作を受け付ける。
                if (Time.frameCount > pointerReadyAfterFrame)
                {
                    pointerReady = true;
                }
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            //追加:復帰時に再通知される押下を、新しい操作へ持ち越さない。
            pointerReady = false;
            pointerReadyAfterFrame = Time.frameCount;
            if (!focused)
            {
                holding = false;
                heldGesture = releasedGesture = 0;
                rejectedPointers.Clear();
                MatchInteractionController.Instance?.CancelSelection();
            }
        }
    }
}

//削除:T08のScene初期入力ガードを追加する前の条件を参照用に保存する。
//            if (holding || rejectedPointers.Count > 0 ||
//                (!touch && Time.unscaledTime - lastTouch < touchMouseSuppression))
//            if (holding || keyboardFrame == Time.frameCount)
