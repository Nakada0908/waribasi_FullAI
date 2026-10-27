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
        private readonly HashSet<int> rejectedPointers = new HashSet<int>();

        public static bool StartPressed => Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
        public static bool RestartPressed => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        public static bool EscapePressed => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        public static bool EscapeReservedForText { get; private set; }
        public static bool IsEditingText => EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() is TMPro.TMP_InputField field && field.isFocused;

        private void Awake()
        {
            Instance = this;
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
            if (holding || rejectedPointers.Count > 0 ||
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
            if (gesture == 0 || gesture != releasedGesture || gesture == consumedGesture || releasedFrame != Time.frameCount)
            {
                return false;
            }
            consumedGesture = gesture;
            return true;
        }

        public bool ConsumeKeyboard()
        {
            if (holding || keyboardFrame == Time.frameCount)
            {
                return false;
            }
            keyboardFrame = Time.frameCount;
            return true;
        }

        private void Update()
        {
            EscapeReservedForText = EscapePressed && IsEditingText;
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
            }
        }

        private void OnApplicationFocus(bool focused)
        {
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
