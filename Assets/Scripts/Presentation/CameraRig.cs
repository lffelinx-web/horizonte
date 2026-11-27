using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>Orbit camera over the map: a focus point on the ground, a heading, and a zoom distance.</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public const float MinDistance = 40, MaxDistance = 650;

        public Camera Cam { get; private set; }
        public Vector3 Focus;
        public float Yaw = 30;
        public float Distance = 170;
        /// <summary>Height of the sun above the horizon, and how far it has swung from its usual place beside the view (set by DayNight).</summary>
        public float SunPitch = 34, SunTurn;
        Vector2 limits;

        public void Init(Camera cam, Vector2 mapSize)
        {
            Cam = cam;
            limits = mapSize;
            Focus = new Vector3(mapSize.x * .5f, 0, mapSize.y * .5f);
            cam.fieldOfView = 32;
            cam.nearClipPlane = 3;
            cam.farClipPlane = 3000;
            Apply();
        }

        /// <summary>Closer views look across the city; far ones look down on it.</summary>
        float Pitch => Mathf.Lerp(34, 62, Mathf.InverseLerp(MinDistance, MaxDistance, Distance));

        public void Pan(Vector3 worldDelta)
        {
            Focus += new Vector3(worldDelta.x, 0, worldDelta.z);
            Focus.x = Mathf.Clamp(Focus.x, 0, limits.x);
            Focus.z = Mathf.Clamp(Focus.z, 0, limits.y);
        }

        /// <summary>How far the player may zoom out; it grows with the city (set by GameController).</summary>
        public float Reach = MaxDistance;

        public void Zoom(float factor) => Distance = Mathf.Clamp(Distance * factor, MinDistance, Mathf.Max(MinDistance, Reach));

        public void Rotate(float degrees) => Yaw = Mathf.Repeat(Yaw + degrees, 360);

        void LateUpdate() => Apply();

        public void Apply()
        {
            if (Cam == null) return;
            var rot = Quaternion.Euler(Pitch, Yaw, 0);
            Cam.transform.SetPositionAndRotation(Focus + rot * new Vector3(0, 0, -Distance), rot);
            // Shadows only need to cover what is in view; a short range keeps them sharp up close.
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                urp.shadowDistance = Mathf.Clamp(Distance * 1.9f, 120, 700);
            // The sun turns with the view and shines from behind on the left, so the sides we see are lit
            // and shadows fall beside buildings instead of hiding behind them.
            if (RenderSettings.sun != null) RenderSettings.sun.transform.rotation = Quaternion.Euler(SunPitch, Yaw + 65 + SunTurn, 0);
        }

        /// <summary>Point on the ground (y = 0) under a screen position.</summary>
        public bool GroundPoint(Vector2 screen, out Vector3 point)
        {
            var ray = Cam.ScreenPointToRay(screen);
            point = default;
            if (ray.direction.y >= -1e-4f) return false;
            float t = -ray.origin.y / ray.direction.y;
            point = ray.origin + ray.direction * t;
            return true;
        }
    }
}
