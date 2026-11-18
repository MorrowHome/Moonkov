// 打开背包时把正确的本地角色相机交给 RaidInventoryBlur。关包、死亡或切 World 时清掉效果，防止模糊留到下一局。
using Unity.Entities;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS {
    public sealed partial class RaidHUD {
        private Camera m_InventoryCamera;
        private Entity m_InventoryCameraOwner;
        private World m_InventoryCameraWorld;

        private void ClearInventoryBackdrop() {
            // 关闭时连已经记录的拥有者和 World 一起清除。重开背包能从当前角色重新找到镜头。
            RaidInventoryBlur.Clear();
            m_InventoryCamera = null;
            m_InventoryCameraOwner = Entity.Null;
            m_InventoryCameraWorld = null;
        }

        private void UpdateInventoryBackdrop() {
            // 只有活着且正常打开背包时才请求背景模糊。暂停、死亡和过渡镜头拥有各自显示状态。
            if (!m_InventoryVisible || !QuickAlive || CinematicInputBlocked || GameSettings.Instance.IsPauseMenuOpen ||
                !TryGetLocalPlayerEntity(out var owner)) {
                ClearInventoryBackdrop();
                return;
            }
            // 优先复用已知的本地相机。拥有者或 World 一变就重查，避免模糊用在观战或者上一局镜头上。
            if (!m_InventoryCamera || owner != m_InventoryCameraOwner || m_InventoryCameraWorld != m_World) {
                m_InventoryCamera = null;
                m_InventoryCameraOwner = owner;
                m_InventoryCameraWorld = m_World;
                if (m_World.EntityManager.HasComponent<GhostGameObjectLink>(owner)) {
                    var link = m_World.EntityManager.GetComponentObject<GhostGameObjectLink>(owner);
                    if (link.LinkedInstance &&
                        link.LinkedInstance.TryGetComponent<DollSingerNetworkPresentation>(out var presentation))
                        m_InventoryCamera = presentation.OwnedCamera;
                }
            }
            RaidInventoryBlur.Publish(m_InventoryCamera);
        }
    }
}