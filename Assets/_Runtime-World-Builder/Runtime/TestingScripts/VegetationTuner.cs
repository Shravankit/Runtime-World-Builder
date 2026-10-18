using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Runtime.World;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Testing
{
    public class VegetationTuner : MonoBehaviour
    {
        bool trees = true, grass = true, shadows = true;
        float treeDist = 600f, billboardDist = 200f, detailDist = 80f, density = 1f;

        void Apply()
        {
            if (!ServiceRegistry.TryResolve<TerrainWorld>(out var world)) return;
            foreach (var c in world.Chunks)
            {
                var t = c.Terrain;
                if (t == null) continue;
                t.drawTreesAndFoliage = trees || grass;
                t.treeDistance = trees ? treeDist : 0f;                 // 0 = no trees drawn
                t.treeBillboardDistance = billboardDist;
                t.detailObjectDistance = grass ? detailDist : 0f;       // 0 = no grass drawn
                t.detailObjectDensity = density;
            }
            QualitySettings.shadows = shadows ? ShadowQuality.All : ShadowQuality.Disable;
        }

        float Slider(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));
            float n = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.Label(n.ToString("F1"), GUILayout.Width(50));
            GUILayout.EndHorizontal();
            return n;
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(Screen.width - 330, 400, 320, 260), GUI.skin.box);
            GUILayout.Label($"Veg tuner   {1000f / Mathf.Max(Time.unscaledDeltaTime * 1000f, 0.01f):F0} fps");
            GUI.changed = false;

            trees = GUILayout.Toggle(trees, "Trees");
            grass = GUILayout.Toggle(grass, "Grass");
            shadows = GUILayout.Toggle(shadows, "Shadows (global)");
            treeDist = Slider("Tree dist", treeDist, 0f, 1000f);
            billboardDist = Slider("Billboard dist", billboardDist, 0f, 500f);
            detailDist = Slider("Grass dist", detailDist, 0f, 200f);
            density = Slider("Grass density", density, 0f, 1f);

            if (GUI.changed) Apply();
            GUILayout.EndArea();
        }
    }
}