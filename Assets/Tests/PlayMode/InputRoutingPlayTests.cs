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
        //追加:小さいGame Viewでも、UIの外かつ画面内の盤面を指定する。
        private Vector2 BoardPoint => center + new Vector2(Mathf.Min(180, Screen.width * 0.375f), 0);
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
            //削除:UI設定前には初期位置の読取り設定を行わない。
            //systemOwner.AddComponent(RuntimeType("Waribasi.Presentation.GameInputRouter"));
            var module = systemOwner.AddComponent<InputSystemUIInputModule>();
            module.enabled = false;
            actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var ui = new InputActionMap("UI");
            actions.AddActionMap(ui);
            var point = ui.AddAction("Point", InputActionType.PassThrough, expectedControlLayout: "Vector2");
            //追加:製品のPoint設定を使い、初期位置の読取りを含めて検証する。
#if UNITY_EDITOR
            point.wantsInitialStateCheck = UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/InputSystem_Actions.inputactions").FindAction("UI/Point").wantsInitialStateCheck;
#endif
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
            //変更:実Sceneと同様に、Binding解決の前にRouterのAwakeを実行する。
            systemOwner.AddComponent(RuntimeType("Waribasi.Presentation.GameInputRouter"));
            //変更:製品と同様に、Project-wideのActionが先に有効な状態を再現する。
            actions.Enable();
            module.enabled = true;
            //削除:module.enabled = true;の後にactions.Enable()していた順序。
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
            //変更:固定サイズがGame View全体を覆わないように検証UIを収める。
            rect.sizeDelta = new Vector2(Mathf.Min(240, Screen.width * 0.5f), Mathf.Min(180, Screen.height * 0.5f));
            //削除:rect.sizeDelta = new Vector2(240, 180);
            var button = (Button)buttonOwner.AddComponent(RuntimeType("Waribasi.Presentation.MatchPointerButton"));
            button.targetGraphic = buttonOwner.GetComponent<Image>();
            button.onClick.AddListener(() => uiClicks++);
            uiClicks = 0;
            center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            //追加:検証用Colliderの移動を物理ワールドへ反映してから入力を送る。
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
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

        //追加:UIを開き直しても、同じ座標の最初のマウスクリックを受け付ける。
        [UnityTest]
        public IEnumerator StationaryMouseWorksAfterUiModuleIsReenabled()
        {
            MouseEvent(center, false);
            yield return null;
            var module = owners.First(owner => owner.name == "RoutingTestSystem").GetComponent<InputSystemUIInputModule>();
            module.enabled = false;
            //追加:Scene切替と同じくRouterを作り直し、Action自体は有効なまま残す。
            UnityEngine.Object.Destroy(module.GetComponent(RuntimeType("Waribasi.Presentation.GameInputRouter")));
            yield return null;
            module.gameObject.AddComponent(RuntimeType("Waribasi.Presentation.GameInputRouter"));
            module.enabled = true;
            yield return null;
            yield return null;
            MouseEvent(center, true);
            yield return null;
            MouseEvent(center, false);
            yield return null;
            Assert.That(uiClicks, Is.EqualTo(1));
            Assert.That(board.Clicks, Is.Zero);
        }

        //追加:Sceneを跨いで触れたままの指から、新しいUIへクリックを発生させない。
        [UnityTest]
        public IEnumerator HeldTouchDoesNotClickTheNewUiWhenItsPositionIsInitialized()
        {
            TouchEvent(301, UnityEngine.InputSystem.TouchPhase.Began);
            yield return null;
            var owner = owners.First(item => item.name == "RoutingTestSystem");
            var module = owner.GetComponent<InputSystemUIInputModule>();
            module.enabled = false;
            //変更:Project-wideのActionを残し、次のSceneで同じ指が押されたままの場合を確認する。
            UnityEngine.Object.Destroy(owner.GetComponent(RuntimeType("Waribasi.Presentation.GameInputRouter")));
            yield return null;
            owner.AddComponent(RuntimeType("Waribasi.Presentation.GameInputRouter"));
            module.enabled = true;
            yield return null;
            yield return null;
            TouchEvent(301, UnityEngine.InputSystem.TouchPhase.Ended);
            yield return null;
            Assert.That(uiClicks, Is.Zero);
            TouchEvent(302, UnityEngine.InputSystem.TouchPhase.Began);
            yield return null;
            TouchEvent(302, UnityEngine.InputSystem.TouchPhase.Ended);
            yield return null;
            Assert.That(uiClicks, Is.EqualTo(1));
        }

        //追加:Sceneを開いたままのマウス押下を、次の画面へ持ち越さない。
        [UnityTest]
        public IEnumerator HeldMouseDoesNotClickTheNewUiWhenItsPositionIsInitialized()
        {
            MouseEvent(center, true);
            yield return null;
            var owner = owners.First(item => item.name == "RoutingTestSystem");
            var module = owner.GetComponent<InputSystemUIInputModule>();
            module.enabled = false;
            UnityEngine.Object.Destroy(owner.GetComponent(RuntimeType("Waribasi.Presentation.GameInputRouter")));
            yield return null;
            owner.AddComponent(RuntimeType("Waribasi.Presentation.GameInputRouter"));
            module.enabled = true;
            yield return null;
            yield return null;
            MouseEvent(center, false);
            yield return null;
            Assert.That(uiClicks, Is.Zero);
            MouseEvent(center, true);
            yield return null;
            MouseEvent(center, false);
            yield return null;
            Assert.That(uiClicks, Is.EqualTo(1));
            Assert.That(board.Clicks, Is.Zero);
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
            //変更:画面外へ出ない座標で、UI外の盤面入力を確認する。
            MouseEvent(BoardPoint, true);
            //削除:MouseEvent(center + new Vector2(180, 0), true);
            yield return null;
            //変更:同じ盤面座標で指を離す。
            MouseEvent(BoardPoint, false);
            //削除:MouseEvent(center + new Vector2(180, 0), false);
            yield return null;
            //追加:既存SceneのUIが検証用盤面を覆っていないか、失敗時に判別する。
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = BoardPoint }, hits);
            Assert.That(board.Clicks, Is.EqualTo(1), string.Join(",", hits.Select(hit => hit.gameObject.name)) +
                ", screen=" + Screen.width + "x" + Screen.height);
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
