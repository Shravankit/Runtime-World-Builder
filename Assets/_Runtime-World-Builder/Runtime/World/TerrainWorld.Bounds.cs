using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.World
{
    public partial class TerrainWorld
    {
        /// World-space box around the whole terrain, using the REAL height range (not the settings' maxHeight),
        /// so a camera can frame exactly what exists. sampleStep = how many height samples to skip when scanning.
        public Bounds GetBounds(int sampleStep = 8)
        {
            float sz = s.chunkSize;
            var min = new Vector3(Min.x * sz, 0f, Min.y * sz);
            var max = new Vector3((Max.x + 1) * sz, 0f, (Max.y + 1) * sz);

            int res = s.heightmapResolution;
            sampleStep = Mathf.Max(1, sampleStep);
            float lo = 1f, hi = 0f;

            foreach (var d in chunks.Values)
            {
                var H = d.Heights;
                for (int z = 0; z < res; z += sampleStep)
                    for (int x = 0; x < res; x += sampleStep)
                    {
                        float h = H[z, x];
                        if (h < lo) lo = h;
                        if (h > hi) hi = h;
                    }
            }
            if (hi < lo) { lo = 0f; hi = 0f; }

            min.y = lo * s.maxHeight;
            max.y = Mathf.Max(hi * s.maxHeight, min.y + 1f);

            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }
    }
}