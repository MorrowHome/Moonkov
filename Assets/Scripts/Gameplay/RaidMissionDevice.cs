// 任务设备的画面部件。RaidMissionSite 把设备的工作状态传进来，它在场景里切换灯、转子、扳手和产物模型。
// 设备状态和产物数量由任务快照驱动。
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS {
    // 场景设备按服务端状态做显示。是否完成由服务端任务规则决定。
    public sealed class RaidMissionDevice : MonoBehaviour {
        private static readonly System.Collections.Generic.List<RaidMissionDevice> s_Active = new();
        public static System.Collections.Generic.IReadOnlyList<RaidMissionDevice> Active => s_Active;

        private void OnEnable() {
            if (!s_Active.Contains(this)) s_Active.Add(this);
        }

        private void OnDisable() { s_Active.Remove(this); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() { s_Active.Clear(); }

        [SerializeField] private string m_AnchorId;
        [SerializeField] private Renderer[] m_Indicators;
        [SerializeField] private Transform m_Rotor, m_Lever;
        [SerializeField] private GameObject[] m_Products;
        private MaterialPropertyBlock m_Block;
        private Color m_Color;
        // 场景挂点通过同一个 ID 和任务定义关联。准星交互查当前登记的设备时也用这个 ID。
        public string AnchorId => m_AnchorId;
        public void Present(
            bool active,
            bool completed,
            bool running,
            bool failed,
            int products,
            bool operateLever = true) {
            // 灯色区分未启用、失败、完成、运行和可交互。颜色变了才改渲染器属性。
            var color = !active ? new Color(.12f, .15f, .2f) :
                failed ? new Color(.8f, .2f, .12f) :
                completed ? new Color(.24f, .65f, .47f) :
                running ? new Color(.83f, .49f, .18f) : new Color(.18f, .38f, .85f);
            if (m_Block == null) m_Block = new MaterialPropertyBlock();
            if (m_Color != color) {
                m_Color = color;
                m_Block.SetColor("_BaseColor", color);
                m_Block.SetColor("_EmissionColor", color * (active ? 1.4f : .1f));
                if (m_Indicators != null)
                    foreach (var indicator in m_Indicators)
                        if (indicator)
                            indicator.SetPropertyBlock(m_Block);
            }
            // 转子按是否工作决定要不要转。拉杆和可见的产物数量也直接跟随当前任务状态。
            if (m_Rotor) m_Rotor.localRotation = Quaternion.Euler(0, running ? Time.time * 135 : 0, 0);
            if (m_Lever) m_Lever.localRotation = Quaternion.Euler(operateLever && completed ? 35 : -15, 0, 0);
            if (m_Products != null)
                for (int i = 0; i < m_Products.Length; i++)
                    if (m_Products[i] && m_Products[i].activeSelf != (active && i < products))
                        m_Products[i].SetActive(active && i < products);
        }
#if UNITY_EDITOR
        public void Author(string id, Renderer[] indicators, Transform rotor, Transform lever, GameObject[] products) {
            m_AnchorId = id;
            m_Indicators = indicators;
            m_Rotor = rotor;
            m_Lever = lever;
            m_Products = products;
        }
#endif
    }
}