using System;
using UnityEngine;

namespace RuntimeWorldBuilder.SO.Vegetation
{
    public enum VegetationKind
    {
        Tree,
        GrassMesh,
        GrassTexture
    }

    [Serializable]
    public class VegetationRule
    {
        public string name = "Vegetation";
        public VegetationKind vegetationKind = VegetationKind.Tree;

        [Header("Asset")]
        public GameObject prefab;              // Tree or GrassMesh
        public Texture2D texture;              // GrassTexture

        [Header("Where (driven by the painted terrain layer)")]
        [Tooltip("Index into TerrainBiomeSettings.layers (0 = Grass, 1 = Soil, 2 = Rock in your asset)")]
        public int biomeLayerIndex = 0;
        [Range(0f, 1f)] public float minLayerWeight = 0.5f;   // biome weight needed to grow here
        [Range(0f, 1f)] public float minHeight01 = 0f;
        [Range(0f, 1f)] public float maxHeight01 = 1f;
        [Range(0f, 90f)] public float maxSlope = 35f;

        [Header("How many")]
        [Tooltip("Tree: instances per hectare at full weight. Grass: blades per detail cell (capped by VegetationSettings.maxDetailPerCell).")]
        public float density = 40f;
        [Tooltip("Higher = sharper cutoff at biome edges")]
        [Range(0.1f, 4f)] public float weightPower = 1.5f;

        [Header("Look")]
        public Vector2 widthScale = new(0.8f, 1.2f);
        public Vector2 heightScale = new(0.8f, 1.3f);
        public Color healthyColor = new(0.35f, 0.6f, 0.2f);   // grass
        public Color dryColor = new(0.6f, 0.55f, 0.25f);      // grass
        [Range(0f, 1f)] public float noiseSpread = 0.4f;      // grass patchiness

        // ---- Effective values ----
        // Array elements added in the inspector can serialize as 0 (field initializers are not run),
        // which silently disabled all vegetation. The populator reads these instead of the raw fields.
        static readonly Color DefaultHealthy = new(0.35f, 0.6f, 0.2f, 1f);
        static readonly Color DefaultDry = new(0.6f, 0.55f, 0.25f, 1f);

        public float EffMaxHeight01 => maxHeight01 <= minHeight01 ? 1f : maxHeight01;
        public float EffMaxSlope => maxSlope <= 0f ? 90f : maxSlope;
        public float EffWeightPower => Mathf.Max(0.1f, weightPower);
        public Vector2 EffWidthScale => widthScale.y <= 0f ? new Vector2(0.8f, 1.2f) : widthScale;
        public Vector2 EffHeightScale => heightScale.y <= 0f ? new Vector2(0.8f, 1.3f) : heightScale;
        public Color EffHealthyColor => healthyColor.a <= 0f ? DefaultHealthy : healthyColor;
        public Color EffDryColor => dryColor.a <= 0f ? DefaultDry : dryColor;
    }

    [CreateAssetMenu(fileName = "VegetationSettings", menuName = "Runtime Terrain Builder/VegetationSettings")]
    public class VegetationSettings : ScriptableObject
    {
        public int seed = 777;
        public VegetationRule[] vegetationRules;

        [Header("Performance caps")]
        [Tooltip("Hard cap on trees per chunk")]
        [Range(100, 5000)] public int maxTreesPerChunk = 1500;
        [Tooltip("Hard cap on grass blades per detail cell. This is the biggest GPU cost, keep it 2-6.")]
        [Range(1, 16)] public int maxDetailPerCell = 4;

        [Header("Detail (grass) grid")]
        [Tooltip("Cells per chunk side. 256 on a 256m chunk = 1m cells")]
        public int detailResolution = 256;
        public int detailResolutionPerPatch = 16;

        [Header("Draw distances")]
        public float treeDistance = 600f;
        public float treeBillboardDistance = 200f;
        public float detailDistance = 80f;
        [Range(0f, 1f)] public float detailDensityScale = 1f;
    }
}