using Unity.Entities;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private Camera m_InventoryCamera;
        private Entity m_InventoryCameraOwner;
        private World m_InventoryCameraWorld;

        private void ClearInventoryBackdrop()
        {
            RaidInventoryBlur.Clear();
            m_InventoryCamera = null; m_InventoryCameraOwner = Entity.Null; m_InventoryCameraWorld = null;
        }

        private void UpdateInventoryBackdrop()
        {
            if (!m_InventoryVisible || !QuickAlive || CinematicInputBlocked || GameSettings.Instance.IsPauseMenuOpen ||
                !TryGetLocalPlayerEntity(out var owner))
            { ClearInventoryBackdrop(); return; }
            if (!m_InventoryCamera || owner != m_InventoryCameraOwner || m_InventoryCameraWorld != m_World)
            {
                m_InventoryCamera = null; m_InventoryCameraOwner = owner; m_InventoryCameraWorld = m_World;
                if (m_World.EntityManager.HasComponent<GhostGameObjectLink>(owner))
                {
                    var link = m_World.EntityManager.GetComponentObject<GhostGameObjectLink>(owner);
                    if (link.LinkedInstance && link.LinkedInstance.TryGetComponent<DollSingerNetworkPresentation>(out var presentation))
                        m_InventoryCamera = presentation.OwnedCamera;
                }
            }
            RaidInventoryBlur.Publish(m_InventoryCamera);
        }
    }
}
