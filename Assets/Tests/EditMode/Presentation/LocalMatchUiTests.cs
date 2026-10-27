using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Presentation.Tests
{
    public sealed class LocalMatchUiTests
    {
        private static Type RuntimeType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "Assembly-CSharp").GetType(name, true);
        }

        [TestCase("Left")]
        [TestCase("Right")]
        public void LocalHandsKeepVisualsAndPointerButHaveNoNetworkComponents(string side)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Object/Local" + side + "Hand.prefab");
            Assert.That(prefab, Is.Not.Null);
            var components = prefab.GetComponentsInChildren<Component>(true);
            Assert.That(components.All(component => component != null), Is.True);
            Assert.That(components.Any(component => component.GetType().Namespace == "Unity.Netcode" ||
                component.GetType().Namespace == "Unity.Netcode.Components"), Is.False);
            Assert.That(components.Any(component => component.GetType().Name == "HandPointerTarget"), Is.True);
            Assert.That(components.Any(component => component.GetType().Name == "HandHighlight"), Is.True);
            Assert.That(components.Any(component => component is Animator), Is.True);
            Assert.That(components.Any(component => component is Collider), Is.True);
            Assert.That(prefab.transform.Find("targetpos"), Is.Not.Null);
            Assert.That(prefab.transform.Find("staypos"), Is.Not.Null);
        }

        [Test]
        public void StartSceneHasExactlyOneConfiguredLocalController()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/StartGame.unity", OpenSceneMode.Additive);
            try
            {
                var controllers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                    .Where(component => component != null && component.GetType().Name == "LocalMatchController").ToArray();
                Assert.That(controllers.Length, Is.EqualTo(1));
                var serialized = new SerializedObject(controllers[0]);
                foreach (var field in new[] { "leftHandPrefab", "rightHandPrefab", "cameraPrefab" })
                {
                    Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void LocalProjectionUsesAnExplicitSeatAndKeepsConfirmedResult()
        {
            var owner = new GameObject("LocalProjection");
            var type = RuntimeType("Waribasi.Networking.MatchClientState");
            var view = owner.AddComponent(type);
            var authority = new MatchAuthority(Guid.NewGuid(), PlayerId.PlayerB, 0);
            var apply = type.GetMethod("ApplyLocal", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                authority.DealInitialCards();
                authority.Start(0);
                apply.Invoke(view, new object[] { authority, PlayerId.PlayerA, 2, 0d });
                Assert.That(type.GetProperty("LocalPlayer").GetValue(view), Is.EqualTo(0));
                Assert.That(type.GetProperty("HandIsCurrent").GetValue(view), Is.True);
                var state = (PublicMatchMessage)type.GetProperty("Public").GetValue(view);
                Assert.That(state.playerAClientId, Is.EqualTo(state.playerBClientId));
                Assert.That(state.hasDeadline, Is.False);
                authority.Submit(PlayerId.PlayerA, new MatchRequest(authority.MatchId, 1, 2, MatchOperation.Surrender), 0);
                apply.Invoke(view, new object[] { authority, PlayerId.PlayerA, 2, 0d });
                var confirmed = type.GetProperty("Public").GetValue(view);
                apply.Invoke(view, new object[] { new MatchAuthority(Guid.NewGuid(), PlayerId.PlayerA, 0), PlayerId.PlayerA, 0, 0d });
                Assert.That(type.GetProperty("Public").GetValue(view), Is.SameAs(confirmed));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }
    }
}
