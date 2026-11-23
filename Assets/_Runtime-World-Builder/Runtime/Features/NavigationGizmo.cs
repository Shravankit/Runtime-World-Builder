using UnityEngine;
using UnityEngine.UIElements;

namespace RuntimeWorldBuilder.Runtime.Features
{
    /// Drives the compass + axis widgets that are DEFINED IN THE UXML (nothing is generated in code).
    /// Needs these names in RuntimeTerrain.uxml:
    ///   compass-arrow  (VisualElement/Label, rotates to point at world north)
    ///   btn-compass    (Button, click = face north)
    ///   btn-axis-x / btn-axis-y / btn-axis-z (Buttons, click = jump to that view)
    /// Put this on any GameObject. The camera needs an OrbitCameraController.
    public class NavigationGizmo : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;

        VisualElement compassArrow;
        Button compassBtn, axisX, axisY, axisZ;
        Camera cam;
        OrbitCameraController orbit;
        bool bound;
        float lastYaw = float.NaN;

        void Start()
        {
            if (uiDocument == null) uiDocument = FindFirstObjectByType<UIDocument>();
            orbit = FindFirstObjectByType<OrbitCameraController>();
        }

        bool Bind()
        {
            var root = uiDocument != null ? uiDocument.rootVisualElement : null;
            if (root == null) return false;

            compassArrow = root.Q("compass-arrow");
            compassBtn = root.Q<Button>("btn-compass");
            axisX = root.Q<Button>("btn-axis-x");
            axisY = root.Q<Button>("btn-axis-y");
            axisZ = root.Q<Button>("btn-axis-z");

            if (compassBtn != null) compassBtn.clicked += () => { if (orbit != null) orbit.SetView(0f, orbit.Pitch); };
            if (axisX != null) axisX.clicked += () => { if (orbit != null) orbit.SetView(270f, 15f); };
            if (axisY != null) axisY.clicked += () => { if (orbit != null) orbit.SetView(orbit.Yaw, 89f); };
            if (axisZ != null) axisZ.clicked += () => { if (orbit != null) orbit.SetView(0f, 15f); };
            return true;
        }

        void LateUpdate()
        {
            if (!bound) { bound = Bind(); if (!bound) return; }
            if (cam == null) cam = Camera.main;
            if (cam == null || compassArrow == null) return;

            float yaw = cam.transform.eulerAngles.y;
            if (Mathf.Approximately(yaw, lastYaw)) return;
            lastYaw = yaw;
            compassArrow.style.rotate = new Rotate(new Angle(-yaw, AngleUnit.Degree));   // north arrow turns opposite to the camera
        }
    }
}