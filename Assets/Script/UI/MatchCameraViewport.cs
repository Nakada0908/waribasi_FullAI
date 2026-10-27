using UnityEngine;

namespace Waribasi.Presentation
{
    [RequireComponent(typeof(Camera))]
    public sealed class MatchCameraViewport : MonoBehaviour
    {
        private Camera target;
        private int width;
        private int height;
        private Rect safeArea;
        private Matrix4x4 referenceProjection;

        public static void Attach(Camera camera)
        {
            if (camera.GetComponent<MatchCameraViewport>() == null)
            {
                camera.gameObject.AddComponent<MatchCameraViewport>();
            }
        }

        public static Rect Fit(Rect area, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return new Rect(0, 0, 1, 1);
            }
            float left = Mathf.Clamp(area.xMin, 0, screenWidth);
            float bottom = Mathf.Clamp(area.yMin, 0, screenHeight);
            float right = Mathf.Clamp(area.xMax, left, screenWidth);
            float top = Mathf.Clamp(area.yMax, bottom, screenHeight);
            if (right <= left || top <= bottom)
            {
                left = bottom = 0;
                right = screenWidth;
                top = screenHeight;
            }
            float fittedWidth = Mathf.Min(right - left, (top - bottom) * (16f / 9f));
            float fittedHeight = fittedWidth * (9f / 16f);
            return new Rect((left + (right - left - fittedWidth) / 2) / screenWidth,
                (bottom + (top - bottom - fittedHeight) / 2) / screenHeight,
                fittedWidth / screenWidth, fittedHeight / screenHeight);
        }

        public static Matrix4x4 FitProjection(Matrix4x4 reference, Rect frame)
        {
            var fit = Matrix4x4.identity;
            fit.m00 = frame.width;
            fit.m11 = frame.height;
            fit.m03 = frame.x * 2 + frame.width - 1;
            fit.m13 = frame.y * 2 + frame.height - 1;
            return fit * reference;
        }

        private void OnEnable()
        {
            target = GetComponent<Camera>();
            referenceProjection = target.orthographic
                ? Matrix4x4.Ortho(-target.orthographicSize * (16f / 9f), target.orthographicSize * (16f / 9f),
                    -target.orthographicSize, target.orthographicSize, target.nearClipPlane, target.farClipPlane)
                : Matrix4x4.Perspective(target.fieldOfView, 16f / 9f, target.nearClipPlane, target.farClipPlane);
            Apply();
        }

        private void OnDisable()
        {
            if (target != null)
            {
                target.ResetProjectionMatrix();
            }
        }

        private void Update()
        {
            if (width != Screen.width || height != Screen.height || safeArea != Screen.safeArea)
            {
                Apply();
            }
        }

        private void Apply()
        {
            width = Screen.width;
            height = Screen.height;
            safeArea = Screen.safeArea;
            target.rect = new Rect(0, 0, 1, 1);
            target.projectionMatrix = FitProjection(referenceProjection, Fit(safeArea, width, height));
        }
    }
}
