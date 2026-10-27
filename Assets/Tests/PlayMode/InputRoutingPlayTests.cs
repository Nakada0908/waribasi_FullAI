using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Waribasi.Presentation.PlayTests
{
    public sealed class PointerClickProbe : MonoBehaviour, IPointerClickHandler
    {
        public int Clicks { get; private set; }

        public void OnPointerClick(PointerEventData data)
        {
            Clicks++;
        }
    }

    public sealed class InputRoutingPlayTests
    {
        private readonly List<GameObject> owners = new List<GameObject>();
        private EventSystem[] otherSystems;
        private Mouse mouse;
        private Touchscreen touch;
        private InputActionAsset actions;
        private InputActionReference pointReference;
        private InputActionReference clickReference;
        private PointerClickProbe board;
        private int uiClicks;
        private Vector2 center;
        //追加:非表示のバッチ実行でも検証用入力を受け取り、終了時に元へ戻す。
        private InputSettings.BackgroundBehavior savedBackground;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode savedEditorRouting;
#endif

        private static Type RuntimeType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp").GetType(name, true);
        }

        private GameObject Owner(string name, params Type[] types)
        {
            var owner = new GameObject(name, types);
            owners.Add(owner);
            return owner;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            //追加:Game Viewのフォーカスに依存せず、同じポインター経路を検証する。
            savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            savedEditorRouting = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            otherSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            foreach (var system in otherSystems)
            {
                system.enabled = false;
            }
            mouse = InputSystem.AddDevice<Mouse>();
            touch = InputSystem.AddDevice<Touchscreen>();
            var systemOwner = Owner("RoutingTestSystem", typeof(EventSystem));
            systemOwner.AddComponent(RuntimeType("Waribasi.Presentation.GameInputRouter"));
            var module = systemOwner.AddComponent<InputSystemUIInputModule>();
            module.enabled = false;
            actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var ui = new InputActionMap("UI");
            actions.AddActionMap(ui);
            var point = ui.AddAction("Point", InputActionType.PassThrough, expectedControlLayout: "Vector2");
            point.AddBinding("<Mouse>/position");
            point.AddBinding("<Touchscreen>/touch*/position");
            var click = ui.AddAction("Click", InputActionType.PassThrough, expectedControlLayout: "Button");
            click.AddBinding("<Mouse>/leftButton");
            click.AddBinding("<Touchscreen>/touch*/press");
            pointReference = InputActionReference.Create(point);
            clickReference = InputActionReference.Create(click);
            module.actionsAsset = actions;
            module.point = pointReference;
            module.leftClick = clickReference;
            module.move = module.submit = module.cancel = null;
            module.scrollWheel = module.middleClick = module.rightClick = null;
            module.pointerBehavior = UIPointerBehavior.AllPointersAsIs;
            module.enabled = true;
            actions.Enable();
            var cameraOwner = Owner("RoutingTestCamera", typeof(Camera), typeof(PhysicsRaycaster));
            cameraOwner.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            var boardOwner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            owners.Add(boardOwner);
            boardOwner.name = "RoutingTestBoard";
            boardOwner.transform.position = new Vector3(0, 0, 5);
            boardOwner.transform.localScale = new Vector3(20, 20, 1);
            board = boardOwner.AddComponent<PointerClickProbe>();
            var canvasOwner = Owner("RoutingTestCanvas", typeof(Canvas), typeof(GraphicRaycaster));
            canvasOwner.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var buttonOwner = Owner("RoutingTestButton", typeof(RectTransform), typeof(Image));
            buttonOwner.transform.SetParent(canvasOwner.transform, false);
            var rect = (RectTransform)buttonOwner.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(240, 180);
            var button = (Button)buttonOwner.AddComponent(RuntimeType("Waribasi.Presentation.MatchPointerButton"));
            button.targetGraphic = buttonOwner.GetComponent<Image>();
            button.onClick.AddListener(() => uiClicks++);
            uiClicks = 0;
            center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var owner in owners)
            {
                UnityEngine.Object.Destroy(owner);
            }
            owners.Clear();
            yield return null;
            actions.Disable();
            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(touch);
            UnityEngine.Object.Destroy(actions);
            UnityEngine.Object.Destroy(pointReference);
            UnityEngine.Object.Destroy(clickReference);
            foreach (var system in otherSystems)
            {
                if (system != null)
                {
                    system.enabled = true;
                }
            }
            //追加:検証のための受付設定を保存していた値へ戻す。
            InputSystem.settings.backgroundBehavior = savedBackground;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorRouting;
#endif
        }

        private void MouseEvent(Vector2 position, bool pressed)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left, pressed));
        }

        private void TouchEvent(int id, UnityEngine.InputSystem.TouchPhase phase)
        {
            InputSystem.QueueStateEvent(touch, new TouchState { touchId = id, phase = phase, position = center });
        }

        [UnityTest]
        public IEnumerator MouseUiClickDoesNotReachTheBoardBehindIt()
        {
            MouseEvent(center, true);
            yield return null;
            MouseEvent(center, false);
            yield return null;
            Assert.That(uiClicks, Is.EqualTo(1));
            Assert.That(board.Clicks, Is.Zero);
            MouseEvent(center + new Vector2(180, 0), true);
            yield return null;
            MouseEvent(center + new Vector2(180, 0), false);
            yield return null;
            Assert.That(board.Clicks, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TwoTouchesProduceOnlyOneUiAction()
        {
            TouchEvent(101, UnityEngine.InputSystem.TouchPhase.Began);
            yield return null;
            TouchEvent(102, UnityEngine.InputSystem.TouchPhase.Began);
            yield return null;
            TouchEvent(101, UnityEngine.InputSystem.TouchPhase.Ended);
            yield return null;
            TouchEvent(102, UnityEngine.InputSystem.TouchPhase.Ended);
            yield return null;
            Assert.That(uiClicks, Is.EqualTo(1));
            Assert.That(board.Clicks, Is.Zero);
        }

        [UnityTest]
        public IEnumerator TouchAndMouseEchoDoNotDoubleClick()
        {
            TouchEvent(201, UnityEngine.InputSystem.TouchPhase.Began);
            yield return null;
            MouseEvent(center, true);
            yield return null;
            TouchEvent(201, UnityEngine.InputSystem.TouchPhase.Ended);
            yield return null;
            MouseEvent(center, false);
            yield return null;
            Assert.That(uiClicks, Is.EqualTo(1));
            Assert.That(board.Clicks, Is.Zero);
        }
    }
}
