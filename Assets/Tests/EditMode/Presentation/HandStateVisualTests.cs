using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Presentation.Tests
{
    public sealed class HandStateVisualTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void OwnHandLayoutIsCenteredFitsTheCanvasAndDoesNotDrift(int count)
        {
            const string path = "Assets/Scenes/Game.unity";
            Assert.That(SceneManager.GetSceneByPath(path).isLoaded, Is.False, "Use an isolated additive scene for layout mutation.");
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var drawing = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                    .Single(c => c != null && c.GetType().Name == "CardButtonDraw");
                var cards = Enumerable.Range(0, count).Select(i => new HandCard(i + 1, new CardDefinition(CardKind.Defense))).ToArray();
                var method = drawing.GetType().GetMethod("DrawOwnHand");
                method.Invoke(drawing, new object[] { cards });
                var buttons = (Button[])drawing.GetType().GetField("buttons").GetValue(drawing);
                var positions = buttons.Take(count).Select(b => ((RectTransform)b.transform).anchoredPosition).ToArray();
                method.Invoke(drawing, new object[] { cards });
                Assert.That(buttons.Count(b => b.gameObject.activeSelf), Is.EqualTo(count));
                Assert.That(buttons.Take(count).Select(b => ((RectTransform)b.transform).anchoredPosition), Is.EqualTo(positions));
                if (count > 0)
                {
                    Assert.That(positions.Average(p => p.x), Is.EqualTo(0).Within(0.01));
                    var container = (RectTransform)buttons[0].transform.parent;
                    Assert.That(container.localScale, Is.EqualTo(Vector3.one));
                    foreach (var button in buttons.Take(count))
                    {
                        var rect = (RectTransform)button.transform;
                        Assert.That(Math.Abs(rect.anchoredPosition.x) + rect.rect.width * 0.5f, Is.LessThan(960));
                    }
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Type TypeNamed(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
        }

        private static void Set(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void DefenseSealAndSelectionAreCombinedWithoutOverwritingEachOther(bool defense, bool seal, bool selected)
        {
            var viewOwner = new GameObject("VisualState");
            var view = viewOwner.AddComponent(TypeNamed("Waribasi.Networking.MatchClientState"));
            var hand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hand.SetActive(false);
            hand.AddComponent(TypeNamed("Unity.Netcode.NetworkObject"));
            var side = hand.AddComponent(TypeNamed("HandRL"));
            Set(side, "handType", Enum.ToObject(TypeNamed("HandType"), 1));
            var renderer = hand.GetComponent<Renderer>();
            Material original = renderer.sharedMaterial;
            var selectionMarker = new GameObject("Select");
            selectionMarker.transform.SetParent(hand.transform, false);
            var sealMarker = new GameObject("Seal");
            sealMarker.transform.SetParent(hand.transform, false);
            var highlight = hand.AddComponent(TypeNamed("HandHighlight"));
            Set(highlight, "stateOpaque", AssetDatabase.LoadAssetAtPath<Material>("Assets/Visuals/HandStates/HandOpaque.mat"));
            Set(highlight, "stateTransparent", AssetDatabase.LoadAssetAtPath<Material>("Assets/Visuals/HandStates/HandTransparent.mat"));
            Set(highlight, "selectionMarker", selectionMarker);
            Set(highlight, "sealMarker", sealMarker);
            var match = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerA,
                playerAEffects: new PlayerEffects(new HandEffects(defense ? 2 : 0, seal ? 2 : 0), default)), 30, 100);
            var state = MatchMessages.Public(match, 0, 7, 3, 100);
            view.GetType().GetMethod("ReceivePublic", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { state, 0UL });
            hand.SetActive(true);
            highlight.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(highlight, null);
            Set(highlight, "view", view);
            //追加:共通の手識別が既存の公開盤面を参照できることも確認する。
            var identity = hand.GetComponent(TypeNamed("Waribasi.Presentation.MatchHandView"));
            var identityArguments = new object[] { view, PlayerId.PlayerA };
            try
            {
                Assert.That(identity, Is.Not.Null);
                Assert.That(identity.GetType().GetMethod("ResolvePlayer").Invoke(identity, identityArguments), Is.True);
                Assert.That(identityArguments[1], Is.EqualTo(PlayerId.PlayerA));
                highlight.GetType().GetMethod("SetHighlight").Invoke(highlight, new object[] { selected });
                Assert.That(selectionMarker.activeSelf, Is.EqualTo(selected));
                Assert.That(sealMarker.activeSelf, Is.EqualTo(seal));
                if (!defense && !seal && !selected)
                {
                    Assert.That(renderer.sharedMaterial, Is.SameAs(original));
                }
                else
                {
                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    Assert.That(block.GetFloat("_Rainbow"), Is.EqualTo(defense ? 1 : 0));
                    Assert.That(block.GetFloat("_Selected"), Is.EqualTo(selected && !defense ? 1 : 0));
                    Assert.That(block.GetFloat("_Opacity"), Is.EqualTo(seal ? 0.45f : 1).Within(0.001f));
                }
                state.hands[0].count = 0;
                state.hands[0].defenseUntil = state.hands[0].sealUntil = 0;
                highlight.GetType().GetMethod("SetHighlight").Invoke(highlight, new object[] { selected });
                Assert.That(selectionMarker.activeSelf || sealMarker.activeSelf, Is.False);
                Assert.That(renderer.sharedMaterial, Is.SameAs(original));
            }
            finally
            {
                foreach (string field in new[] { "opaqueInstance", "transparentInstance" })
                {
                    var material = (Material)highlight.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(highlight);
                    UnityEngine.Object.DestroyImmediate(material);
                    Set(highlight, field, null);
                }
                UnityEngine.Object.DestroyImmediate(hand);
                UnityEngine.Object.DestroyImmediate(viewOwner);
            }
        }

        [TestCase("Title")]
        [TestCase("StartGame")]
        [TestCase("Game")]
        [TestCase("End")]
        public void EverySceneUsesOneNewInputModuleWithExplicitUiActions(string name)
        {
            string path = "Assets/Scenes/" + name + ".unity";
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            }
            try
            {
                var systems = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EventSystem>(true)).ToArray();
                Assert.That(systems.Length, Is.EqualTo(1));
                Assert.That(systems[0].GetComponent<StandaloneInputModule>(), Is.Null);
                var module = systems[0].GetComponent<InputSystemUIInputModule>();
                Assert.That(module, Is.Not.Null);
                Assert.That(module.actionsAsset, Is.Not.Null);
                Assert.That(module.point.action.name, Is.EqualTo("Point"));
                Assert.That(module.leftClick.action.name, Is.EqualTo("Click"));
                Assert.That(module.pointerBehavior, Is.EqualTo(UIPointerBehavior.AllPointersAsIs));
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero, transform.name);
                    }
                }
                if (name == "StartGame")
                {
                    var mode = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                        .Single(c => c != null && c.GetType().Name == "RelayModeChange");
                    Assert.That(new SerializedObject(mode).FindProperty("changeButton").objectReferenceValue, Is.Not.Null);
                }
                if (name == "Game")
                {
                    var controller = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                        .Single(c => c != null && c.GetType().Name == "MatchInteractionController");
                    var serialized = new SerializedObject(controller);
                    foreach (string field in new[] { "selectionPanel", "selectionText", "confirmButton", "cancelButton", "attackCancelButton", "redistributeButton", "swapButton" })
                    {
                        Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
                    }
                    var array = serialized.FindProperty("optionButtons");
                    Assert.That(array.arraySize, Is.EqualTo(4));
                    for (int i = 0; i < array.arraySize; i++)
                    {
                        Assert.That(array.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null);
                    }
                }
            }
            finally
            {
                if (opened)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
