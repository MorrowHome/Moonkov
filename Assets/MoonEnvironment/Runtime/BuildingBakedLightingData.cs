using System;
using UnityEngine;

namespace Unity.MP_FPS.Moon
{
    /// <summary>
    /// Non-directional, linear HDR lighting bases sharing one atlas/probe layout.
    /// A room state contains that room switched on and every other room switched off.
    /// Runtime lighting is environment + sum(enabled room state - baseline).
    /// Environment contains the fixed ambient contribution with every room light off.
    /// Scene renderer/light references belong to BuildingBakedLighting, not this asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Moon/Building Baked Lighting Data")]
    public sealed class BuildingBakedLightingData : ScriptableObject
    {
        [Serializable] public sealed class State
        {
            public Texture2D[] colorMaps = Array.Empty<Texture2D>();
            // Preserve native probe positions/tetrahedralization as well as SH data.
            public LightProbes probes;
        }

        [Serializable] public sealed class Room
        {
            public string id;
            public State onState;
        }

        public State baseline;
        public State environment;
        public Room[] rooms = Array.Empty<Room>();
    }
}
