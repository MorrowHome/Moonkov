// 在角色预览里展示近战装备。仓库读装备槽，局内读角色快照，最后把对应武器外观交给预览模型。
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS.Client {
    public sealed partial class MenuCharacterView {
        private DollSingerMeleePresentation m_EquipmentMelee;
        public MeleeWeaponKind PreviewMeleeWeapon { get; private set; }

        private void SelectMeleeInventory(InventoryGraph graph) {
            // 仓库没有实时角色，按选中的物品代码判断近战外观。没选中光环又没有近战武器时显示徒手。
            var kind = MeleeInventory.Kind(graph?.Find(PreviewWeaponItemId)?.Code);
            PreviewMeleeWeapon = EquippedPreviewWeapon == DollSingerHaloAim.HaloWeapon.None
                ? kind == MeleeWeaponKind.None ? MeleeWeaponKind.Fists : kind
                : MeleeWeaponKind.None;
            PresentMeleeEquipment();
        }

        private void SelectMeleeSnapshot(in PredictedPlayerGhost snapshot) {
            // 局内已有服务器同步的当前武器，直接听快照。不必用仓库的默认选择重新猜一次。
            PreviewMeleeWeapon = (MeleeWeaponKind)snapshot.MeleeWeapon;
            PresentMeleeEquipment();
        }

        private void PresentMeleeEquipment() {
            if (m_Model == null || m_Animator == null) return;
            if (m_EquipmentMelee == null && PreviewMeleeWeapon != MeleeWeaponKind.None) {
                m_EquipmentMelee = m_Model.AddComponent<DollSingerMeleePresentation>();
                // 复用正式近战的取姿势能力，关闭它的运行更新。预览会在自己需要时送表现数据。
                m_EquipmentMelee.enabled = false;
                var source = m_Settings.HaloSource
                    ? m_Settings.HaloSource.GetComponent<DollSingerMeleePresentation>()
                    : null;
                m_EquipmentMelee.gestureBake = source ? source.gestureBake : null;
                m_EquipmentMelee.Configure(m_Animator, null, null);
                foreach (var child in m_Model.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = ViewLayer;
            }
            if (m_EquipmentMelee == null) return;
            // Without an attack direction, the idle pose uses the model's current facing.
            // A world-space direction captured here would stay fixed while the portrait turns.
            m_EquipmentMelee.SetPresentation(new MeleeViewState {
                Weapon = m_SharedStage == null && !m_HomePose ? PreviewMeleeWeapon : MeleeWeaponKind.None,
                Alive = true
            });
        }
    }
}