using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS.Client
{
    public sealed partial class MenuCharacterView
    {
        private DollSingerMeleePresentation m_EquipmentMelee;
        public MeleeWeaponKind PreviewMeleeWeapon { get; private set; }

        private void SelectMeleeInventory(InventoryGraph graph)
        {
            var kind = MeleeInventory.Kind(graph?.Find(PreviewWeaponItemId)?.Code);
            PreviewMeleeWeapon = EquippedPreviewWeapon == DollSingerHaloAim.HaloWeapon.None
                ? kind == MeleeWeaponKind.None ? MeleeWeaponKind.Fists : kind : MeleeWeaponKind.None;
            PresentMeleeEquipment();
        }

        private void SelectMeleeSnapshot(in PredictedPlayerGhost snapshot)
        {
            PreviewMeleeWeapon = (MeleeWeaponKind)snapshot.MeleeWeapon;
            PresentMeleeEquipment();
        }

        private void PresentMeleeEquipment()
        {
            if (m_Model == null || m_Animator == null) return;
            if (m_EquipmentMelee == null && PreviewMeleeWeapon != MeleeWeaponKind.None)
            {
                m_EquipmentMelee = m_Model.AddComponent<DollSingerMeleePresentation>();
                m_EquipmentMelee.enabled = false;
                var source = m_Settings.HaloSource ? m_Settings.HaloSource.GetComponent<DollSingerMeleePresentation>() : null;
                m_EquipmentMelee.gestureBake = source ? source.gestureBake : null;
                m_EquipmentMelee.Configure(m_Animator, null, null);
                foreach (var child in m_Model.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = ViewLayer;
            }
            if (m_EquipmentMelee == null) return;
            m_EquipmentMelee.SetPresentation(new MeleeViewState { Weapon = PreviewMeleeWeapon,
                Alive = true, AttackDirection = m_Model.transform.forward });
        }
    }
}
