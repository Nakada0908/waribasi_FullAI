using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Presentation.Tests
{
    public sealed class MatchFlowUiTests
    {
        private static Type RuntimeType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp").GetType(name, true);
        }

        [TestCase("StartGame", "MatchSetupPresenter")]
        [TestCase("Game", "MatchFlowPresenter")]
        [TestCase("End", "MatchFlowPresenter")]
        public void FlowScenesHaveTheirRequiredReferencesAndPointerButtons(string sceneName, string componentName)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + sceneName + ".unity", OpenSceneMode.Additive);
            try
            {
                var components = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
                var presenter = components.Single(component => component != null && component.GetType().Name == componentName);
                var serialized = new SerializedObject(presenter);
                string[] fields = sceneName == "StartGame" ? new[] { "joinInput", "statusText", "codeText", "settingsText", "npcMessage", "retryButton", "cancelButton" } :
                    sceneName == "Game" ? new[] { "preparationPanel", "preparationText", "preparationCancel", "menuPanel", "surrenderChoice", "menuButton" } :
                    new[] { "menuPanel", "surrenderChoice", "resultDetail", "rematchStatus", "rematchButton", "rematchLabel" };
                foreach (string field in fields)
                {
                    Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
                }
                foreach (var button in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Button>(true)))
                {
                    Assert.That(button.GetType().Name, Is.EqualTo("MatchPointerButton"), button.name);
                    for (int index = 0; index < button.onClick.GetPersistentEventCount(); index++)
                    {
                        Assert.That(button.onClick.GetPersistentTarget(index), Is.Not.Null, button.name);
                    }
                }
                if (sceneName == "StartGame")
                {
                    Assert.That(serialized.FindProperty("timeButtons").arraySize, Is.EqualTo(8));
                    Assert.That(serialized.FindProperty("firstButtons").arraySize, Is.EqualTo(3));
                    Assert.That(serialized.FindProperty("difficultyButtons").arraySize, Is.EqualTo(3));
                    Assert.That(serialized.FindProperty("npcFirstButtons").arraySize, Is.EqualTo(3));
                    Assert.That(components.Count(component => component != null && component.GetType().Name == "OnlineSessionController"), Is.EqualTo(1));
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void ModalBlocksActionsWithoutChangingAuthorityStateOrDeadline()
        {
            var owner = new GameObject("FlowModalState");
            var selectionOwner = new GameObject("FlowModalSelection");
            selectionOwner.SetActive(false);
            var view = owner.AddComponent(RuntimeType("Waribasi.Networking.MatchClientState"));
            var selection = selectionOwner.AddComponent(RuntimeType("Waribasi.Presentation.MatchInteractionController"));
            var authority = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerA), 30, 100);
            object Call(object target, string name, params object[] arguments) => target.GetType().GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, arguments);
            try
            {
                Call(view, "ReceivePublic", MatchMessages.Public(authority, 0, 7, 3, 100), 0UL);
                Call(view, "ReceiveOwn", MatchMessages.OwnHand(authority, PlayerId.PlayerA));
                Call(selection, "Bind", view);
                Assert.That(selection.GetType().GetProperty("CanInteract").GetValue(selection), Is.True);
                Call(selection, "OpenRedistribution");
                Call(selection, "SetModalBlocked", true);
                Assert.That(selection.GetType().GetProperty("CanInteract").GetValue(selection), Is.False);
                Assert.That(selection.GetType().GetProperty("HasSelection").GetValue(selection), Is.False);
                Assert.That(authority.State.StateVersion, Is.Zero);
                Assert.That(authority.Deadline, Is.EqualTo(130));
                Call(selection, "SetModalBlocked", false);
                Assert.That(selection.GetType().GetProperty("CanInteract").GetValue(selection), Is.True);
            }
            finally
            {
                Call(selection, "Bind", (object)null);
                UnityEngine.Object.DestroyImmediate(selectionOwner);
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void SessionSnapshotRejectsOldSessionRoundAndChangedSettings()
        {
            var owner = new GameObject("SessionSnapshotBoundary");
            var type = RuntimeType("Waribasi.Networking.OnlineSessionController");
            var session = owner.AddComponent(type);
            var messageType = type.GetNestedType("ControlMessage", BindingFlags.NonPublic);
            string id = Guid.NewGuid().ToString("N");
            object Message(long serial, string sessionId, int round = 1, int seconds = 30)
            {
                var message = Activator.CreateInstance(messageType, true);
                void Set(string name, object value) => messageType.GetField(name).SetValue(message, value);
                Set("kind", 0);
                Set("session", sessionId);
                Set("serial", serial);
                Set("round", round);
                Set("seconds", seconds);
                Set("peer", true);
                return message;
            }
            var receive = type.GetMethod("ReceiveSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            var remote = type.GetField("remote", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                var accepted = Message(5, id);
                messageType.GetField("peer").SetValue(accepted, false);
                receive.Invoke(session, new[] { accepted });
                Assert.That(remote.GetValue(session), Is.SameAs(accepted));
                Assert.That(type.GetProperty("OpponentLeft").GetValue(session), Is.False, "An offer before peer registration is not a departure.");
                foreach (var bad in new[] { Message(4, id), Message(6, Guid.NewGuid().ToString("N")), Message(6, id, 3), Message(6, id, 1, 15), Message(6, "invalid") })
                {
                    receive.Invoke(session, new[] { bad });
                    Assert.That(remote.GetValue(session), Is.SameAs(accepted));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }
    }
}
