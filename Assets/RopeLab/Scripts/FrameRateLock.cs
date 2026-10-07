using UnityEngine;

namespace RopeLab
{
    /// <summary>Caps the frame rate (default 60 FPS) while this scene is running.</summary>
    public class FrameRateLock : MonoBehaviour
    {
        public int targetFps = 60;

        void Awake() => Apply();
        void OnValidate() { if (Application.isPlaying) Apply(); }

        void Apply()
        {
            // targetFrameRate is ignored while VSync is on, so turn VSync off.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFps;
        }
    }
}
