using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS
{
    // Authored physical hardware. It presents server state and never decides completion.
    public sealed class RaidMissionDevice : MonoBehaviour
    {
        [SerializeField] private string m_AnchorId;
        [SerializeField] private Renderer[] m_Indicators;
        [SerializeField] private Transform m_Rotor, m_Lever;
        [SerializeField] private GameObject[] m_Products;
        private MaterialPropertyBlock m_Block;
        private Color m_Color;
        public string AnchorId => m_AnchorId;

        public void Present(bool active, bool completed, bool running, bool failed, int products, bool operateLever = true)
        {
            var color = !active ? new Color(.12f, .15f, .2f) : failed ? new Color(.8f, .2f, .12f) : completed ?
                new Color(.24f, .65f, .47f) : running ? new Color(.83f, .49f, .18f) : new Color(.18f, .38f, .85f);
            if (m_Block == null) m_Block = new MaterialPropertyBlock();
            if (m_Color != color)
            {
                m_Color = color; m_Block.SetColor("_BaseColor", color); m_Block.SetColor("_EmissionColor", color * (active ? 1.4f : .1f));
                if (m_Indicators != null) foreach (var indicator in m_Indicators) if (indicator) indicator.SetPropertyBlock(m_Block);
            }
            if (m_Rotor) m_Rotor.localRotation = Quaternion.Euler(0, running ? Time.time * 135 : 0, 0);
            if (m_Lever) m_Lever.localRotation = Quaternion.Euler(operateLever && completed ? 35 : -15, 0, 0);
            if (m_Products != null) for (int i = 0; i < m_Products.Length; i++)
                if (m_Products[i] && m_Products[i].activeSelf != (active && i < products)) m_Products[i].SetActive(active && i < products);
        }
#if UNITY_EDITOR
        public void Author(string id, Renderer[] indicators, Transform rotor, Transform lever, GameObject[] products)
        { m_AnchorId = id; m_Indicators = indicators; m_Rotor = rotor; m_Lever = lever; m_Products = products; }
#endif
    }
}
