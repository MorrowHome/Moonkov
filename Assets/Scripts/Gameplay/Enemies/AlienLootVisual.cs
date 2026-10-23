using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>Persistent client marker for the existing server-owned loot bag.
    /// CharacterIndex -1 is alien remains; human corpse records retain 0..2.</summary>
    public sealed class AlienLootVisual : MonoBehaviour
    {
        private Material m_Material;
        public static GameObject Create(Transform parent, RaidCorpseRpc bag)
        {
            // A serialized Resources material keeps the exact keyword-free Lit variant in builds.
            var template = Resources.Load<Material>("Moonkov/AlienRemains");
            if (!template || !template.shader || !template.shader.isSupported)
                throw new System.InvalidOperationException("Missing or unsupported Moonkov/AlienRemains material.");
            var root = new GameObject("Alien remains / " + bag.LootId);
            root.transform.SetParent(parent, false); root.transform.SetPositionAndRotation(bag.Position, bag.Rotation);
            var view = root.AddComponent<AlienLootVisual>();
            view.m_Material = new Material(template);
            for (int i = 0; i < 5; i++)
            {
                var lobe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lobe.name = "Collapsed alien tissue"; lobe.transform.SetParent(root.transform, false);
                float angle = i * Mathf.PI * 2 / 5;
                lobe.transform.localPosition = new Vector3(Mathf.Cos(angle) * .19f, .1f, Mathf.Sin(angle) * .19f);
                lobe.transform.localScale = new Vector3(.38f, .18f, .27f);
                lobe.GetComponent<Renderer>().sharedMaterial = view.m_Material;
                Destroy(lobe.GetComponent<Collider>());
            }
            return root;
        }
        private void OnDestroy() { if(m_Material) Destroy(m_Material); }
    }
}
