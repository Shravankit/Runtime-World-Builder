using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Runtime.Import;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuntimeWorldBuilder.Runtime.Features
{
    /// Drop a viewpoint marker on the terrain, then walk it in first person to see the terrain
    /// the way a player would. Esc / V returns to the editor (orbit) camera.
    ///
    ///   M      toggle "place marker" mode, then left-click the terrain (orbit the camera first to pick a spot)
    ///   V      enter / leave the player view at the marker
    ///   WASD   walk | Shift run | mouse look | Esc leave
    ///
    /// Optional UI Toolkit buttons (add to your UXML if you want them): btn-place-marker, btn-player-view.
    public class PlayerPreviewTool : MonoBehaviour
    {
        /// True while placing a marker or walking. Other editing tools should ignore the mouse while this is set.
        public static bool Active { get; private set; }
        public static bool Walking { get; private set; }

        [Header("Player")]
        [SerializeField] float eyeHeight = 1.7f;
        [SerializeField] float walkSpeed = 4f;
        [SerializeField] float runSpeed = 9f;
        [SerializeField] float mouseSensitivity = 2f;
        [SerializeField] float fieldOfView = 70f;
        [Tooltip("Highest ledge (metres) the player steps onto, e.g. a bridge deck or road edge.")]
        [SerializeField] float stepHeight = 1.2f;
        [Tooltip("Steeper ground than this blocks walking.")]
        [SerializeField, Range(20f, 80f)] float maxSlopeDegrees = 55f;

        [Header("Keys")]
        [SerializeField] KeyCode placeKey = KeyCode.M;
        [SerializeField] KeyCode walkKey = KeyCode.V;

        [Header("UI Toolkit (optional)")]
        [SerializeField] UIDocument uiDocument;

        bool placing, hasMarker;
        Vector3 markerPos, feet;
        float markerYaw, yaw, pitch, groundY;
        GameObject marker;
        Camera cam;
        OrbitCameraController orbit;
        float savedFov, savedNear;
        string hint = "";
        float hintUntil;
        GUIStyle hudStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = false; Walking = false; }

        void OnEnable() => FeatureEvents.ClearRequested += ClearMarker;

        void OnDisable()
        {
            FeatureEvents.ClearRequested -= ClearMarker;
            if (Walking) ExitWalk();
            placing = false;
            RefreshActive();
        }

        void Start()
        {
            if (uiDocument == null) uiDocument = FindFirstObjectByType<UIDocument>();
            var root = uiDocument != null ? uiDocument.rootVisualElement : null;
            if (root == null) return;

            var place = root.Q<Button>("btn-place-marker");
            if (place != null) place.clicked += () => SetPlacing(!placing);
            var view = root.Q<Button>("btn-player-view");
            if (view != null) view.clicked += EnterWalk;
        }

        void Update()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            if (Walking) { UpdateWalking(); return; }

            if (!Typing())
            {
                if (Input.GetKeyDown(placeKey)) SetPlacing(!placing);
                if (Input.GetKeyDown(walkKey)) EnterWalk();
            }
            if (placing && Input.GetKeyDown(KeyCode.Escape)) SetPlacing(false);

            if (placing && Input.GetMouseButtonDown(0) && !HeightmapImportTool.OverPanel)
                PlaceMarkerFromMouse();

            UpdateMarkerScale();
        }

        // ---------------- marker ----------------
        void SetPlacing(bool on)
        {
            if (Walking) return;
            placing = on;
            RefreshActive();
            if (on) Hint("Click the terrain to place the viewpoint marker.", 3f);
        }

        void PlaceMarkerFromMouse()
        {
            if (!Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit, 20000f)) return;

            markerPos = hit.point;
            markerYaw = cam.transform.eulerAngles.y;     // the player starts out facing the way the editor camera looks
            hasMarker = true;

            EnsureMarker();
            marker.transform.SetPositionAndRotation(markerPos, Quaternion.Euler(0f, markerYaw, 0f));
            marker.SetActive(true);

            Hint("Marker placed. Press V to walk it as a player (click again to move it).", 4f);
        }

        void EnsureMarker()
        {
            if (marker != null) return;
            marker = new GameObject("PlayerViewMarker");
            var mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.1f, 0.9f, 0.9f) };
            Part(PrimitiveType.Cylinder, new Vector3(0f, 1f, 0f), new Vector3(0.2f, 1f, 0.2f), mat);       // pole (2 m tall)
            Part(PrimitiveType.Sphere, new Vector3(0f, 2.2f, 0f), Vector3.one * 0.7f, mat);                // head
            Part(PrimitiveType.Cube, new Vector3(0f, 2.2f, 0.8f), new Vector3(0.25f, 0.25f, 1.1f), mat);   // arrow = facing
        }

        void Part(PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
        {
            var p = GameObject.CreatePrimitive(type);
            Destroy(p.GetComponent<Collider>());          // must never block terrain raycasts
            p.layer = 2;                                  // Ignore Raycast
            p.transform.SetParent(marker.transform, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            p.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        void UpdateMarkerScale()
        {
            if (marker == null || !marker.activeSelf) return;
            float d = Vector3.Distance(cam.transform.position, markerPos);
            marker.transform.localScale = Vector3.one * Mathf.Max(1f, d * 0.01f);   // stays visible when zoomed far out
        }

        void ClearMarker()
        {
            if (Walking) ExitWalk();
            hasMarker = false;
            placing = false;
            if (marker != null) Destroy(marker);
            marker = null;
            RefreshActive();
        }

        // ---------------- player view ----------------
        void EnterWalk()
        {
            if (Walking) return;
            if (!hasMarker) { Hint("Place a marker first: press M, then click the terrain.", 4f); return; }
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            orbit = cam.GetComponent<OrbitCameraController>();
            if (orbit != null) orbit.enabled = false;

            savedFov = cam.fieldOfView;
            savedNear = cam.nearClipPlane;
            cam.fieldOfView = fieldOfView;
            cam.nearClipPlane = 0.1f;

            placing = false;
            Walking = true;
            RefreshActive();

            feet = markerPos;
            groundY = markerPos.y;
            if (TryGround(feet, out float gy)) groundY = gy;
            feet.y = groundY;
            yaw = markerYaw;
            pitch = 0f;

            if (marker != null) marker.SetActive(false);
            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            UnityEngine.Cursor.visible = false;
            ApplyCamera();
        }

        void ExitWalk()
        {
            Walking = false;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

            if (cam != null)
            {
                cam.fieldOfView = savedFov;
                cam.nearClipPlane = savedNear;
            }
            if (orbit != null) orbit.enabled = true;       // orbit resumes exactly where you left it
            if (marker != null && hasMarker) marker.SetActive(true);
            RefreshActive();
        }

        void UpdateWalking()
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(walkKey)) { ExitWalk(); return; }

            float dt = Time.deltaTime;
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -85f, 85f);

            float fwd = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float rgt = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(rgt, 0f, fwd);
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            if (dir.sqrMagnitude > 1e-6f)
            {
                bool run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                Vector3 step = dir * ((run ? runSpeed : walkSpeed) * dt);
                Vector3 next = feet + step;

                if (TryGround(next, out float gy))
                {
                    float slope = (gy - groundY) / Mathf.Max(step.magnitude, 1e-4f);
                    if (slope <= Mathf.Tan(maxSlopeDegrees * Mathf.Deg2Rad))
                    {
                        feet.x = next.x;
                        feet.z = next.z;
                        groundY = gy;
                    }
                }
            }

            feet.y = Mathf.Lerp(feet.y, groundY, 1f - Mathf.Exp(-14f * dt));   // smooth over bumps and steps
            ApplyCamera();
        }

        /// Ground height at p. Terrain height, or a road / bridge deck collider within step reach if it is higher.
        /// Returns false outside the terrain, so the player stops at the edge.
        bool TryGround(Vector3 p, out float y)
        {
            y = 0f;
            if (!ServiceRegistry.TryResolve<TerrainWorld>(out var world) ||
                !ServiceRegistry.TryResolve<TerrainSettings>(out var s)) return false;
            if (!world.TrySampleHeight01(p.x, p.z, out float h01)) return false;

            y = h01 * s.maxHeight;
            var origin = new Vector3(p.x, groundY + stepHeight, p.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, stepHeight + 6f))
                y = Mathf.Max(y, hit.point.y);
            return true;
        }

        void ApplyCamera() =>
            cam.transform.SetPositionAndRotation(feet + Vector3.up * eyeHeight, Quaternion.Euler(pitch, yaw, 0f));

        // ---------------- helpers ----------------
        void RefreshActive() => Active = placing || Walking;

        void Hint(string text, float seconds)
        {
            hint = text;
            hintUntil = Time.unscaledTime + seconds;
        }

        bool Typing()
        {
            var root = uiDocument != null ? uiDocument.rootVisualElement : null;
            var f = root?.focusController?.focusedElement as VisualElement;
            return f != null && (f is TextField || f.GetFirstAncestorOfType<TextField>() != null);
        }

        void OnGUI()
        {
            string msg = Walking ? "WASD move   |   Shift run   |   Mouse look   |   Esc or V: back to editor"
                : placing ? "Click the terrain to place the viewpoint marker   |   V: walk it   |   M or Esc: cancel"
                : Time.unscaledTime < hintUntil ? hint : "";
            if (msg.Length == 0) return;

            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
                hudStyle.normal.textColor = Color.white;
            }
            float w = Mathf.Min(Screen.width - 40f, 720f);
            GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height - 56f, w, 36f), msg, hudStyle);
        }
    }
}