using System;
using UnityEngine;

namespace Unity.MP_FPS.Moon
{
    /// <summary>
    /// Baked rendering for explicitly fixed architecture. Original GameObjects,
    /// mesh references and collision remain available. No Update or camera polling.
    /// A destruction/detachment system MUST call ReleaseFor before changing a source;
    /// this restores that small cluster's original renderers without rebuilding meshes.
    /// Disabling this component restores the source representation for comparison.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BuildingRenderClusters : MonoBehaviour
    {
        [Serializable]
        public sealed class Group
        {
            public MeshRenderer combined;
            public MeshRenderer[] sources = Array.Empty<MeshRenderer>();
            [NonSerialized] public bool released;
        }

        [SerializeField] private Group[] groups = Array.Empty<Group>();

        // Authoring data is baked by the editor tool; no runtime mesh allocation.
        public void Configure(Group[] value)
        {
            Apply(false);
            groups = value ?? Array.Empty<Group>();
            Apply(isActiveAndEnabled);
        }

        private void OnEnable() => Apply(true);
        private void OnDisable() => Apply(false);

        public bool ReleaseFor(Renderer source)
        {
            if (!source) return false;
            foreach (var group in groups)
            {
                if (Array.IndexOf(group.sources, source) < 0) continue;
                group.released = true;
                ApplyGroup(group, false);
                return true;
            }
            return false;
        }

        private void Apply(bool combine)
        {
            foreach (var group in groups) ApplyGroup(group, combine && !group.released);
        }

        private static void ApplyGroup(Group group, bool combine)
        {
            // Missing combined geometry must fail open to the original geometry.
            var filter = group.combined ? group.combined.GetComponent<MeshFilter>() : null;
            bool useCombined = combine && group.combined && group.combined.sharedMaterial && filter && filter.sharedMesh;
            if (group.combined) group.combined.enabled = useCombined;
            foreach (var source in group.sources)
                if (source) source.enabled = !useCombined;
        }
    }
}
