using UnityEngine;

namespace Waribasi.Presentation
{
    [CreateAssetMenu(menuName = "Waribasi/Fade Settings")]
    public sealed class FadeSettings : ScriptableObject
    {
        [SerializeField, Range(0.05f, 1)] private float sceneSeconds = 0.25f;
        [SerializeField, Range(0.05f, 1)] private float panelSeconds = 0.25f;
        [SerializeField, Min(5)] private float networkCoverTimeoutSeconds = 30;

        public float SceneSeconds => Mathf.Clamp(sceneSeconds, 0.05f, 1);
        public float PanelSeconds => Mathf.Clamp(panelSeconds, 0.05f, 1);
        public float NetworkCoverTimeoutSeconds => Mathf.Max(5, networkCoverTimeoutSeconds);
    }
}
