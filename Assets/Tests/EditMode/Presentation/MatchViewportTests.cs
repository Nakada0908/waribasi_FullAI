using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Waribasi.Presentation.Tests
{
    public sealed class MatchViewportTests
    {
        private static Type ViewportType => AppDomain.CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "Assembly-CSharp").GetType("Waribasi.Presentation.MatchCameraViewport", true);

        [TestCase(1280, 720, 0, 0, 1280, 720)]
        [TestCase(390, 844, 0, 0, 390, 844)]
        [TestCase(844, 390, 0, 0, 844, 390)]
        [TestCase(1024, 768, 0, 0, 1024, 768)]
        [TestCase(2400, 1080, 80, 24, 2220, 1032)]
        [TestCase(1280, 720, 0, 0, 0, 0)]
        public void BoardKeepsReferenceAspectInsideTheSafeArea(int width, int height, int x, int y, int safeWidth, int safeHeight)
        {
            var area = new Rect(x, y, safeWidth, safeHeight);
            var fitted = (Rect)ViewportType.GetMethod("Fit").Invoke(null, new object[] { area, width, height });
            var pixels = new Rect(fitted.x * width, fitted.y * height, fitted.width * width, fitted.height * height);
            if (safeWidth == 0 || safeHeight == 0)
            {
                area = new Rect(0, 0, width, height);
            }
            Assert.That(pixels.width / pixels.height, Is.EqualTo(16f / 9f).Within(0.0001));
            Assert.That(pixels.xMin, Is.GreaterThanOrEqualTo(area.xMin - 0.01));
            Assert.That(pixels.yMin, Is.GreaterThanOrEqualTo(area.yMin - 0.01));
            Assert.That(pixels.xMax, Is.LessThanOrEqualTo(area.xMax + 0.01));
            Assert.That(pixels.yMax, Is.LessThanOrEqualTo(area.yMax + 0.01));
            Assert.That(pixels.center.x, Is.EqualTo(area.center.x).Within(0.01));
            Assert.That(pixels.center.y, Is.EqualTo(area.center.y).Within(0.01));
        }

        [TestCase(390, 844)]
        [TestCase(844, 390)]
        [TestCase(1280, 720)]
        public void ProjectionFramesTheBoardWithoutRestrictingTheClearedViewport(int width, int height)
        {
            var frame = (Rect)ViewportType.GetMethod("Fit").Invoke(null, new object[] { new Rect(0, 0, width, height), width, height });
            var projection = (Matrix4x4)ViewportType.GetMethod("FitProjection").Invoke(null, new object[] { Matrix4x4.identity, frame });
            foreach (float x in new[] { -1f, 1f })
            {
                foreach (float y in new[] { -1f, 1f })
                {
                    var point = projection * new Vector4(x, y, 0, 1);
                    Assert.That((point.x + 1) / 2, Is.EqualTo(x < 0 ? frame.xMin : frame.xMax).Within(0.0001));
                    Assert.That((point.y + 1) / 2, Is.EqualTo(y < 0 ? frame.yMin : frame.yMax).Within(0.0001));
                }
            }
            var owner = new GameObject("Viewport test", typeof(Camera));
            try
            {
                var component = owner.AddComponent(ViewportType);
                ViewportType.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                Assert.That(owner.GetComponent<Camera>().rect, Is.EqualTo(new Rect(0, 0, 1, 1)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [TestCase("StartGame", "MatchSetupCanvas")]
        [TestCase("Game", "MatchFlowCanvas")]
        [TestCase("Game", "TurnAndHandChangeCanvas")]
        [TestCase("End", "MatchFlowCanvas")]
        public void FlowControlsFitBothNarrowAndWideScreens(string sceneName, string canvasName)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + sceneName + ".unity", OpenSceneMode.Additive);
            try
            {
                var canvas = scene.GetRootGameObjects().Single(root => root.name == canvasName);
                Assert.That(canvas.GetComponent<CanvasScaler>().screenMatchMode, Is.EqualTo(CanvasScaler.ScreenMatchMode.Expand));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void HistoryCloseButtonDoesNotShareTheMenuButtonHitArea()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var hud = roots.Single(root => root.name == "TurnAndHandChangeCanvas");
                var panel = hud.transform.Find("MatchHud/HistoryPanel");
                panel.gameObject.SetActive(true);
                Canvas.ForceUpdateCanvases();
                Rect Bounds(Transform item)
                {
                    var corners = new Vector3[4];
                    ((RectTransform)item).GetWorldCorners(corners);
                    return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
                }
                var close = Bounds(panel.Find("CloseHistory"));
                var menu = Bounds(roots.Single(root => root.name == "MatchFlowCanvas").transform.Find("OpenMenu"));
                var scroll = Bounds(panel.Find("Scroll"));
                Assert.That(close.Overlaps(menu), Is.False, "History close must not activate the menu above it.");
                Assert.That(close.Overlaps(scroll), Is.False, "The close button must not cover history content.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
