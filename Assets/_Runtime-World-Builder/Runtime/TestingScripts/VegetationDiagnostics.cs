using System.Text;
using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Biomes;
using RuntimeWorldBuilder.SO.Vegetation;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Testing
{
    /// Press F9 in Play mode (or use the component's context menu) to print WHY grass / trees are not showing.
    /// Assign the same VegetationSettings and TerrainBiomeSettings that TerrainWorldTester uses.
    public class VegetationDiagnostics : MonoBehaviour
    {
        [SerializeField] VegetationSettings vegetation;
        [SerializeField] TerrainBiomeSettings biome;
        [SerializeField] KeyCode key = KeyCode.F9;

        void Update()
        {
            if (Input.GetKeyDown(key)) Run();
        }

        [ContextMenu("Run Diagnostics")]
        public void Run()
        {
            var sb = new StringBuilder("[VegetationDiagnostics]\n");
            int problems = 0;
            void Bad(string m) { problems++; sb.AppendLine("  PROBLEM: " + m); }
            void Info(string m) => sb.AppendLine("  " + m);

            // ---------- settings assets ----------
            if (vegetation == null)
            {
                Bad("No VegetationSettings assigned on this component. Also check TerrainWorldTester > Vegetation Settings: " +
                    "if that field is empty NOTHING is ever planted.");
            }
            else
            {
                var rules = vegetation.vegetationRules;
                int nL = biome != null && biome.layers != null ? biome.layers.Length : -1;
                if (nL < 0) Info("(assign the TerrainBiomeSettings here to also check layer indices)");

                if (rules == null || rules.Length == 0) Bad("VegetationSettings has no vegetation rules.");
                else
                {
                    int grass = 0;
                    for (int i = 0; i < rules.Length; i++)
                    {
                        var r = rules[i];
                        if (r == null) { Bad($"Rule {i} is empty."); continue; }
                        string n = $"Rule {i} ({r.vegetationKind})";

                        if (r.density <= 0f) Bad($"{n}: density is 0.");
                        if (nL >= 0 && (r.biomeLayerIndex < 0 || r.biomeLayerIndex >= nL))
                            Bad($"{n}: Biome Layer Index {r.biomeLayerIndex} does not exist (biome has {nL} layers). The rule is skipped.");

                        if (r.vegetationKind == VegetationKind.GrassMesh)
                        {
                            grass++;
                            if (r.prefab == null) Bad($"{n}: no prefab assigned.");
                            else
                            {
                                var mr = r.prefab.GetComponentInChildren<MeshRenderer>();
                                if (mr == null) Bad($"{n}: the prefab has no MeshRenderer.");
                                else if (mr.sharedMaterial != null && !mr.sharedMaterial.enableInstancing)
                                    Bad($"{n}: the prefab's material needs 'Enable GPU Instancing' ticked, otherwise the mesh grass is invisible.");
                            }
                        }
                        else if (r.vegetationKind == VegetationKind.GrassTexture)
                        {
                            grass++;
                            if (r.texture == null) Bad($"{n}: no texture assigned.");
                        }
                        else if (r.prefab == null) Bad($"{n}: no tree prefab assigned.");
                    }
                    if (grass == 0) Bad("No grass rule at all (kinds GrassMesh / GrassTexture). Only trees can appear.");
                }

                if (vegetation.detailDistance <= 0f) Bad("VegetationSettings Detail Distance is 0.");
                if (vegetation.detailDensityScale <= 0f) Bad("VegetationSettings Detail Density Scale is 0.");
                if (vegetation.maxDetailPerCell <= 0) Bad("VegetationSettings Max Detail Per Cell is 0.");
            }

            // ---------- what the scene actually has ----------
            if (!ServiceRegistry.TryResolve<TerrainWorld>(out var world))
            {
                Bad("No TerrainWorld exists yet (terrain not created).");
            }
            else
            {
                Info($"Vegetation work still queued: {world.PendingWorkCount} (0 = finished)");

                int chunks = 0, withProtos = 0, withDetails = 0;
                Terrain sample = null;
                foreach (var c in world.Chunks)
                {
                    if (c.Terrain == null) continue;
                    chunks++;
                    sample = sample != null ? sample : c.Terrain;
                    var td = c.Terrain.terrainData;
                    if (td.detailPrototypes.Length > 0) withProtos++;
                    if (td.detailPrototypes.Length > 0 &&
                        td.GetSupportedLayers(0, 0, td.detailWidth, td.detailHeight).Length > 0) withDetails++;
                }
                Info($"Chunks: {chunks} | with grass prototypes: {withProtos} | with grass actually painted: {withDetails}");

                if (sample != null)
                {
                    Info($"Terrain: drawTreesAndFoliage={sample.drawTreesAndFoliage}, detailObjectDistance={sample.detailObjectDistance}, " +
                         $"detailObjectDensity={sample.detailObjectDensity}, treeDistance={sample.treeDistance}");
                    if (!sample.drawTreesAndFoliage) Bad("Terrain.drawTreesAndFoliage is off. In the Nature panel both Trees and Grass toggles are off.");
                    if (sample.detailObjectDistance <= 0f) Bad("Grass draw distance is 0. The Nature panel's grass distance slider (VegetationTuner) is at 0 or the Grass toggle is off.");
                    if (sample.detailObjectDensity <= 0f) Bad("Grass density is 0. The Nature panel's density slider (VegetationTuner) is at 0.");
                }

                if (chunks > 0 && withProtos == 0 && world.PendingWorkCount == 0)
                    Bad("Finished, but no chunk got any grass prototype: no valid grass rule, or TerrainWorldTester has no Vegetation Settings assigned.");
                else if (withProtos > 0 && withDetails == 0 && world.PendingWorkCount == 0)
                    Bad("Grass prototypes exist but nothing was painted: the rules' height / slope / layer-weight limits exclude the whole terrain. " +
                        "Lower Min Layer Weight, widen Max Slope / height range, or check the Biome Layer Index points at a layer that is actually painted.");
            }

            sb.AppendLine(problems == 0
                ? "  No problems found in the settings. If grass is still missing, look closer: it only draws within the grass distance of the camera."
                : $"  {problems} problem(s) found.");
            Debug.Log(sb.ToString());
        }
    }
}