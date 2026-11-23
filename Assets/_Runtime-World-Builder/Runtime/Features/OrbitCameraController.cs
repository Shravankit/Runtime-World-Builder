using RuntimeWorldBuilder.Runtime.Import;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Features
{
    /// Orbit / pan / zoom camera for viewing the runtime terrain from any angle.
    /// Put it on the Main Camera (and disable any other camera-move script on it).
    /// Because the tools raycast from Camera.main, points can be added from any view.
    ///
    /// Mouse:  Middle-drag = orbit | Shift + Middle-drag = pan | Scroll wheel / trackpad two-finger scroll = zoom
    /// Keys:   Arrows = orbit | Shift + Arrows = pan | PageUp/PageDown = zoom
    ///         Home = frame the whole terrain (north up) | End / Keypad7 = top-down | Keypad1 = front | Keypad3 = right | Keypad9 = opposite side
    /// Scroll ALWAYS zooms the camera. Brush / stamp size is changed with + and - (see SizeKeys).
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class OrbitCameraController : MonoBehaviour
    {
        [Header("Default view")]
        [Tooltip("Tilt of the opening view. 0 = level with the ground, 90 = straight down.")]
        [SerializeField, Range(5f, 89f)] float defaultPitch = 40f;
        [Tooltip("Spare room around the terrain. 1 = touching the screen edges, 1.1 = 10% border.")]
        [SerializeField] float frameMargin = 1.08f;

        [Header("Orbit")]
        [SerializeField] float orbitSpeed = 3f;            // degrees per mouse unit
        [SerializeField] float keyOrbitSpeed = 90f;        // degrees per second
        [SerializeField] Vector2 pitchLimits = new(5f, 89f);

        [Header("Pan / Zoom")]
        [SerializeField] float panSpeed = 1f;
        [SerializeField] float zoomSpeed = 0.1f;           // fraction of distance per scroll notch
        [SerializeField] float minDistance = 20f;
        [SerializeField] float maxDistance = 8000f;

        [Header("Feel")]
        [SerializeField] float smoothing = 14f;            // higher = snappier, 0 = instant

        Camera cam;

        Vector3 target;
        float yaw, pitch = 40f, distance = 800f;
        Vector3 cTarget; float cYaw, cPitch, cDistance;    // smoothed (current) values
        bool focused;
        bool dragOrbit, dragPan;

        public float Yaw { get => yaw; set => yaw = Mathf.Repeat(value, 360f); }
        public float Pitch { get => pitch; set => pitch = Mathf.Clamp(value, pitchLimits.x, pitchLimits.y); }
        public float Distance { get => distance; set => distance = Mathf.Clamp(value, minDistance, maxDistance); }

        void Awake() => cam = GetComponent<Camera>();

        void Start()
        {
            if (!focused)
            {
                // adopt whatever pose the camera already has
                var e = transform.eulerAngles;
                yaw = e.y;
                pitch = Mathf.Clamp(e.x > 180f ? e.x - 360f : e.x, pitchLimits.x, pitchLimits.y);
                target = transform.position + transform.forward * distance;
                Snap();
            }
        }

        /// Opening view: looking north (+Z points up the screen), tilted, with the WHOLE terrain inside the screen.
        /// The distance is computed from the camera's field of view and aspect ratio and the real terrain box.
        public void FrameBounds(Bounds b)
        {
            if (cam == null) cam = GetComponent<Camera>();
            focused = true;

            yaw = 0f;
            pitch = Mathf.Clamp(defaultPitch, pitchLimits.x, pitchLimits.y);
            target = b.center;

            var inv = Quaternion.Inverse(Quaternion.Euler(pitch, yaw, 0f));
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * cam.aspect;

            // Every corner of the box must fit: |x| <= depth * tanH and |y| <= depth * tanV, with depth = distance + z
            float need = minDistance;
            Vector3 e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 c = inv * corner;   // camera space, relative to the target
                need = Mathf.Max(need, Mathf.Abs(c.x) / tanH - c.z, Mathf.Abs(c.y) / tanV - c.z);
            }

            distance = need * frameMargin;
            maxDistance = Mathf.Max(maxDistance, distance * 3f);
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, (distance + b.size.magnitude) * 1.5f);
            Snap();
        }

        /// Kept for older callers. Prefer FrameBounds.
        public void FocusOn(Vector3 center, float span) =>
            FrameBounds(new Bounds(center, new Vector3(span, Mathf.Max(1f, span * 0.1f), span)));

        /// Smoothly turn to a heading / tilt (used by the compass and axis gizmo).
        public void SetView(float yawDegrees, float pitchDegrees)
        {
            Yaw = yawDegrees;
            Pitch = pitchDegrees;
        }

        void Snap()
        {
            cTarget = target; cYaw = yaw; cPitch = pitch; cDistance = distance;
            Apply();
        }

        void Update()
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool overUI = HeightmapImportTool.OverPanel;

            // ---- mouse drag (a drag that started on terrain keeps going even if it crosses a panel) ----
            if (Input.GetMouseButtonDown(2) && !overUI) { dragOrbit = !shift; dragPan = shift; }
            if (Input.GetMouseButtonUp(2)) dragOrbit = dragPan = false;

            float mx = Input.GetAxis("Mouse X"), my = Input.GetAxis("Mouse Y");
            if (dragOrbit)
            {
                Yaw += mx * orbitSpeed;
                Pitch -= my * orbitSpeed;
            }
            else if (dragPan)
            {
                Pan(-mx, -my, distance * 0.02f * panSpeed);
            }

            // ---- zoom: scroll wheel and trackpad two-finger scroll, always ----
            float scroll = Mathf.Clamp(Input.mouseScrollDelta.y, -3f, 3f);
            if (scroll != 0f && !overUI)
                Distance = distance * (1f - scroll * zoomSpeed);
            if (Input.GetKey(KeyCode.PageUp)) Distance = distance * (1f - 1.5f * Time.unscaledDeltaTime);
            if (Input.GetKey(KeyCode.PageDown)) Distance = distance * (1f + 1.5f * Time.unscaledDeltaTime);

            // ---- keyboard orbit / pan ----
            float dt = Time.unscaledDeltaTime;
            float h = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float v = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            if (h != 0f || v != 0f)
            {
                if (shift) Pan(h * dt, v * dt, distance * 0.6f * panSpeed);
                else { Yaw += h * keyOrbitSpeed * dt; Pitch += v * keyOrbitSpeed * dt; }
            }

            // ---- view presets ----
            if (Input.GetKeyDown(KeyCode.Home)) ReframeFromWorld();
            if (Input.GetKeyDown(KeyCode.End) || Input.GetKeyDown(KeyCode.Keypad7)) Pitch = pitchLimits.y;
            if (Input.GetKeyDown(KeyCode.Keypad1)) { Yaw = 0f; Pitch = 15f; }
            if (Input.GetKeyDown(KeyCode.Keypad3)) { Yaw = 270f; Pitch = 15f; }
            if (Input.GetKeyDown(KeyCode.Keypad9)) Yaw += 180f;
        }

        void LateUpdate()
        {
            float k = smoothing <= 0f ? 1f : 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            cTarget = Vector3.Lerp(cTarget, target, k);
            cYaw = Mathf.LerpAngle(cYaw, yaw, k);
            cPitch = Mathf.Lerp(cPitch, pitch, k);
            cDistance = Mathf.Lerp(cDistance, distance, k);
            Apply();
        }

        void Apply()
        {
            var rot = Quaternion.Euler(cPitch, cYaw, 0f);
            transform.SetPositionAndRotation(cTarget + rot * new Vector3(0f, 0f, -cDistance), rot);
        }

        /// Moves the pivot along the camera's right axis and the ground-plane forward axis.
        void Pan(float right, float forward, float scale)
        {
            var yawRot = Quaternion.Euler(0f, yaw, 0f);
            target += (yawRot * Vector3.right * right + yawRot * Vector3.forward * forward) * scale;
        }

        void ReframeFromWorld()
        {
            if (!Core.Service.ServiceRegistry.TryResolve<World.TerrainWorld>(out var world)) return;
            FrameBounds(world.GetBounds());
        }
    }
}