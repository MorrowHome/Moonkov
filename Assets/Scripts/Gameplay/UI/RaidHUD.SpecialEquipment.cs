// 指南针和测距仪的 HUD 读数。先从随身特殊槽检查工具，再用本地角色相机读朝向和准星射线。
using Unity.Entities;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    public sealed partial class RaidHUD {
        private Label m_SurveyLabel;
        private Camera m_SurveyCamera;
        private Transform m_SurveyActor;
        private World m_SurveyWorld;
        private Entity m_SurveyOwner;
        private float m_NextSurvey;
        private readonly RaycastHit[] m_SurveyHits = new RaycastHit[32];
        private static readonly string[] SurveySlots = { "Special1", "Special2", "Special3" };
        private static readonly string[] SurveyBearings = { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };

        private void InitializeSpecialEquipment() {
            m_SurveyLabel = m_Root.Q<Label>("raidSurveyReadout");
            m_SurveyCamera = null;
            m_SurveyActor = null;
            m_SurveyWorld = null;
            m_SurveyOwner = Entity.Null;
            m_NextSurvey = 0;
        }

        private void UpdateSpecialEquipment() {
            if (!QuickAlive || PointerRequested || GameSettings.Instance.IsPauseMenuOpen) {
                m_SurveyLabel.style.display = DisplayStyle.None;
                return;
            }
            // 工具读数不是战斗输入。隔一小段时间更新即可，避免测距仪的射线每帧都打。
            if (Time.unscaledTime < m_NextSurvey) return;
            m_NextSurvey = Time.unscaledTime + .2f;
            var graph = QuickGraph;
            bool compass = false, rangefinder = false;
            if (graph != null)
                // 只有确实装在特殊装备槽的工具才启用读数。装在普通背包里的同种物品还不能直接使用。
                foreach (string slot in SurveySlots) {
                    string code = graph.Equipped(slot)?.Code;
                    compass |= Inventory.SpecialEquipmentRules.Compass(code);
                    rangefinder |= Inventory.SpecialEquipmentRules.Rangefinder(code);
                }
            if ((!compass && !rangefinder) || !TryGetLocalPlayerEntity(out var owner)) {
                m_SurveyLabel.style.display = DisplayStyle.None;
                return;
            }
            // 本地角色改变时重新绑定镜头和角色模型。射线从当前视角出发，并排除打到自己的部位。
            if (owner != m_SurveyOwner || m_SurveyWorld != m_World || !m_SurveyCamera) {
                m_SurveyOwner = owner;
                m_SurveyWorld = m_World;
                m_SurveyCamera = null;
                m_SurveyActor = null;
                if (m_World.EntityManager.HasComponent<GhostGameObjectLink>(owner)) {
                    var link = m_World.EntityManager.GetComponentObject<GhostGameObjectLink>(owner);
                    if (link.LinkedInstance &&
                        link.LinkedInstance.TryGetComponent<DollSingerNetworkPresentation>(out var presentation)) {
                        m_SurveyCamera = presentation.OwnedCamera;
                        m_SurveyActor = presentation.transform;
                    }
                }
            }
            if (!m_SurveyCamera) {
                m_SurveyLabel.style.display = DisplayStyle.None;
                return;
            }
            string readout = "";
            if (compass) {
                var forward = m_SurveyCamera.transform.forward;
                // 从相机朝向的地面分量算方位角，再从八个方向选近似名称。地图朝北时与这组读数一致。
                float degrees = Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360);
                readout = "方位 " + SurveyBearings[Mathf.RoundToInt(degrees / 45) % 8] + " " +
                          (Mathf.RoundToInt(degrees) % 360).ToString("000") + "°";
            }
            if (rangefinder) {
                var ray = m_SurveyCamera.ViewportPointToRay(new Vector3(.5f, .5f));
                float nearest = float.PositiveInfinity;
                int hits = UnityEngine.Physics.RaycastNonAlloc(ray, m_SurveyHits, 500,
                    m_SurveyCamera.cullingMask & UnityEngine.Physics.DefaultRaycastLayers & ~(1 << 5),
                    QueryTriggerInteraction.Ignore);
                for (int n = 0; n < hits; n++)
                    if ((!m_SurveyActor || !m_SurveyHits[n].transform.IsChildOf(m_SurveyActor)) &&
                        m_SurveyHits[n].distance < nearest)
                        nearest = m_SurveyHits[n].distance;
                // A full buffer may omit a closer surface; never present a false precise distance.
                // 射线结果写进固定数组。数组满时可能还有更近物体没装进去，所以此时只显示遮挡复杂。
                string distance = hits == m_SurveyHits.Length ? "测距遮挡复杂" :
                    float.IsPositiveInfinity(nearest) ? "测距 >500 m／无实体表面" : "测距 " + nearest.ToString("0.0") + " m";
                readout += (readout.Length == 0 ? "" : "  /  ") + distance;
            }
            m_SurveyLabel.style.display = DisplayStyle.Flex;
            if (m_SurveyLabel.text != readout) MoonkovLocalization.Set(m_SurveyLabel, readout);
        }
    }
}