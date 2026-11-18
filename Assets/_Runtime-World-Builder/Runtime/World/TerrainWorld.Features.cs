using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.World
{
    public partial class TerrainWorld
    {
        /// Return the new normalized (0..1) height for the sample at world (wx, wz). cur01 is the current height.
        public delegate float HeightFunc(float wx, float wz, float cur01);

        /// Deform terrain inside a world-space XZ rectangle with a per-sample function.
        /// One undo step. Shared chunk edges get the same result because f depends only on world position + current height.
        /// Chunks inside the rectangle that f never changes are not snapshotted, uploaded or repainted.
        public void ModifyHeights(string label, Vector2 min, Vector2 max, HeightFunc f)
        {
            float sp = s.CellSpacing;
            int res = s.heightmapResolution, last = res - 1;
            var cMin = s.WorldToChunk(new Vector3(min.x - sp, 0, min.y - sp));
            var cMax = s.WorldToChunk(new Vector3(max.x + sp, 0, max.y + sp));

            BeginEdit(label);
            for (int cz = cMin.y; cz <= cMax.y; cz++)
                for (int cx = cMin.x; cx <= cMax.x; cx++)
                {
                    if (!chunks.TryGetValue(new Vector2Int(cx, cz), out var d)) continue;

                    int baseX = d.Coord.x * last, baseZ = d.Coord.y * last;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(min.x / sp) - baseX - 1, 0, last);
                    int x1 = Mathf.Clamp(Mathf.CeilToInt(max.x / sp) - baseX + 1, 0, last);
                    int z0 = Mathf.Clamp(Mathf.FloorToInt(min.y / sp) - baseZ - 1, 0, last);
                    int z1 = Mathf.Clamp(Mathf.CeilToInt(max.y / sp) - baseZ + 1, 0, last);

                    bool touched = false;
                    int rx0 = int.MaxValue, rz0 = int.MaxValue, rx1 = -1, rz1 = -1;
                    var H = d.Heights;
                    for (int z = z0; z <= z1; z++)
                    {
                        float wz = (baseZ + z) * sp;
                        for (int x = x0; x <= x1; x++)
                        {
                            float cur = H[z, x];
                            float nv = Mathf.Clamp01(f((baseX + x) * sp, wz, cur));
                            if (Mathf.Abs(nv - cur) < 1e-6f) continue;

                            if (!touched) { Touch(d); touched = true; }   // lazy snapshot: still equals the "before" state here
                            H[z, x] = nv;
                            if (x < rx0) rx0 = x; if (x > rx1) rx1 = x;
                            if (z < rz0) rz0 = z; if (z > rz1) rz1 = z;
                        }
                    }
                    if (rx1 >= 0) CommitRegion(d, new Region(rx0, rz0, rx1, rz1));
                }
            EndEdit();
        }
    }
}