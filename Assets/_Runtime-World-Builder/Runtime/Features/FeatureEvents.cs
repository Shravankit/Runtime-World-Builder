using System;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Features
{
    /// Fired when the whole terrain is replaced (heightmap import, load world).
    /// Anything built on top of the old terrain (roads, rivers, lakes, markers, placed models)
    /// subscribes and removes itself.
    public static class FeatureEvents
    {
        public static event Action ClearRequested;

        public static void RequestClear() => ClearRequested?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ClearRequested = null;
    }
}