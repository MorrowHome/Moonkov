using System;
using System.Collections.Generic;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Client
{
    public sealed partial class MenuCharacterView
    {
        // The inventory remains owned by its existing caller. The preview only
        // reads it; in particular it must never invoke SyncEquipment/SaveActiveAmmo.
        private GameObject m_EquipmentAssembly;
        private DollSingerHaloAim m_EquipmentSource;
        private RifleHaloVisual m_EquipmentRifle;
        private ShotgunHaloVisual m_EquipmentShotgun;
        private RevolverHaloVisual m_EquipmentRevolver;
        private SniperHaloVisual m_EquipmentSniper;
        private ArsenalHaloVisual[] m_EquipmentArsenal = Array.Empty<ArsenalHaloVisual>();
        private HaloModuleVisual m_EquipmentRifleModules, m_EquipmentShotgunModules,
            m_EquipmentRevolverModules, m_EquipmentSniperModules;
        private LineRenderer[] m_EquipmentCore = Array.Empty<LineRenderer>(), m_EquipmentGlow = Array.Empty<LineRenderer>();
        private Light m_EquipmentLight, m_EquipmentFaceFront, m_EquipmentFaceBack;
        private HaloDischargeVisual m_EquipmentDischarge;
        private Color m_EquipmentHaloColor = Color.white;
        private float m_EquipmentHeadOffset, m_EquipmentReloadProgress;
        private bool m_EquipmentReloading, m_EquipmentNeedsSettle;
        private int m_EquipmentReloadTarget, m_EquipmentBurstIndex;
        private uint m_EquipmentShotTick, m_EquipmentReloadTick, m_EquipmentAppliedWeapon = DollSingerWeapons.None;
        private string m_EquipmentAppliedItem;
        private bool m_EquipmentHasSnapshot, m_EquipmentLightEnabled = true;
        private uint m_EquipmentLightingSettings, m_EquipmentPendingDischarge;
        private PredictedPlayerGhost m_EquipmentSnapshot;

        public DollSingerHaloAim.HaloWeapon EquippedPreviewWeapon { get; private set; }
        public uint PreviewWeaponId { get; private set; } = DollSingerWeapons.None;
        public string PreviewWeaponItemId { get; private set; } = "";
        public ushort PreviewModules { get; private set; }
        public int PreviewLoadedAmmo { get; private set; }
        public int PreviewMagazineCapacity { get; private set; }
        public Color PreviewHaloColor => m_EquipmentHaloColor;

        [Serializable] private struct SerializedHaloPose { public float hhh; }

        private WeaponRegistry EquipmentRegistry => m_Settings && m_Settings.WeaponRegistry
            ? m_Settings.WeaponRegistry : WeaponManager.Instance ? WeaponManager.Instance.WeaponRegistry : null;

        public void SetEquipmentInventory(InventoryGraph graph, string activeWeaponItemId = null)
        {
            if (m_Disposed) return;
            m_ProtectionInventory = graph;
            // Returning from a live snapshot to inventory data starts a fresh
            // visual baseline. Network tick ordering must not reject that state.
            if (m_EquipmentHasSnapshot) m_EquipmentAppliedItem = null;
            m_EquipmentHasSnapshot = false; m_EquipmentLightingSettings = 0;
            m_EquipmentLightEnabled = true; m_EquipmentPendingDischarge = 0;
            InventoryItem item = null;
            // An explicit empty ID represents empty hands, including the melee
            // equipment mode. Null means the normal initial deployment choice.
            if (activeWeaponItemId != null)
            {
                var requested = graph?.Find(activeWeaponItemId);
                if (requested != null && requested.Parent == InventoryCatalog.Equipment &&
                    (requested.Region == "Primary" || requested.Region == "Secondary" || requested.Region == "Pistol" || requested.Region == MeleeInventory.Slot))
                    item = requested;
            }
            if (activeWeaponItemId == null || activeWeaponItemId.Length > 0 && item == null)
            {
                foreach (string slot in new[] { "Primary", "Secondary", "Pistol" })
                {
                    var candidate = graph?.Equipped(slot);
                    if (DollSingerWeapons.WeaponId(candidate?.Code) == DollSingerWeapons.None) continue;
                    item = candidate; break;
                }
                item ??= MeleeInventory.Equipped(graph);
            }
            uint weaponId = DollSingerWeapons.WeaponId(item?.Code);
            ushort modules = (ushort)HaloModules.Mask(graph, item);
            var registry = EquipmentRegistry;
            var original = registry ? registry.GetWeaponData(weaponId) : null;
            var weapon = registry ? registry.GetWeaponData(weaponId, modules) : null;
            // This mirrors the server's legacy -1 interpretation without writing
            // a default back to the player's actual InventoryItem.LoadedAmmo.
            int ammo = item == null ? 0 : item.LoadedAmmo < 0 ? original?.MagazineSize ?? 0 : Mathf.Max(0, item.LoadedAmmo);
            SetEquipmentSelection(weaponId, item?.Id ?? "", modules, ammo, weapon?.MagazineSize ?? 0);
            SelectMeleeInventory(graph);
            m_EquipmentReloading = false; m_EquipmentReloadProgress = 0;
            m_EquipmentReloadTarget = PreviewLoadedAmmo;
            m_EquipmentShotTick = m_EquipmentReloadTick = 0; m_EquipmentBurstIndex = 0;
            ApplyEquipmentVisualState();
            if (m_Model != null)
            {
                SampleEquipmentVisuals(0);
                ApplyProtectionInventory(graph);
            }
            m_ViewDirty = true;
        }

        public void SetEquipmentSnapshot(in PredictedPlayerGhost snapshot)
        {
            if (m_Disposed) return;
            if (m_EquipmentHasSnapshot && PreviewWeaponId == snapshot.EquippedWeaponID &&
                snapshot.LastShotTick != 0 && unchecked((int)(snapshot.LastShotTick - m_EquipmentShotTick)) > 0)
                m_EquipmentPendingDischarge = snapshot.LastShotTick;
            m_EquipmentHasSnapshot = true; m_EquipmentSnapshot = snapshot;
            m_EquipmentLightingSettings = snapshot.ControllerState.HaloLightingSettings;
            m_EquipmentLightEnabled = snapshot.ControllerState.HaloLightEnabled;
            var registry = EquipmentRegistry;
            var weapon = registry ? registry.GetWeaponData(snapshot.EquippedWeaponID, snapshot.EquippedModules) : null;
            SetEquipmentSelection(snapshot.EquippedWeaponID, snapshot.EquippedWeaponItem.ToString(),
                snapshot.EquippedModules, snapshot.CurrentAmmo, weapon?.MagazineSize ?? 0);
            SelectMeleeSnapshot(snapshot);
            m_EquipmentReloading = snapshot.ControllerState.IsReloadingState && weapon != null;
            m_EquipmentReloadProgress = HaloReload.Progress(snapshot, weapon);
            m_EquipmentReloadTarget = snapshot.ReloadTargetAmmo;
            m_EquipmentShotTick = snapshot.LastShotTick; m_EquipmentReloadTick = snapshot.LastReloadTick;
            m_EquipmentBurstIndex = snapshot.BurstSequence;
            ApplyEquipmentVisualState();
            if (m_Model != null)
            {
                SampleEquipmentVisuals(0);
                ApplyEquipmentProtectionSnapshot();
            }
            m_ViewDirty = true;
        }

        private void ApplyEquipmentProtectionSnapshot()
        {
            if (!m_EquipmentHasSnapshot || m_Model == null) return;
            var snapshot = m_EquipmentSnapshot;
            if (!m_Protection && (snapshot.HeadDomeEquipped || snapshot.ShieldEquipped || snapshot.SubspaceBackpack != 0))
                m_Protection = m_Model.GetComponent<EnergyShieldPresentation>() ?? m_Model.AddComponent<EnergyShieldPresentation>();
            if (!m_Protection) return;
            m_Protection.PresentationAudioEnabled = false;
            m_Protection.SetPreviewHaloColor(PreviewHaloColor);
            m_Protection.Present(snapshot, false, false, null);
            m_Protection.SimulatePresentation();
        }

        private void SetEquipmentSelection(uint weaponId, string itemId, ushort modules, int ammo, int capacity)
        {
            var selected = DollSingerWeapons.IsHalo(weaponId)
                ? (DollSingerHaloAim.HaloWeapon)(weaponId - 1) : DollSingerHaloAim.HaloWeapon.None;
            bool changed = EquippedPreviewWeapon != selected || PreviewWeaponItemId != itemId;
            PreviewWeaponId = selected == DollSingerHaloAim.HaloWeapon.None ? DollSingerWeapons.None : weaponId;
            EquippedPreviewWeapon = selected; PreviewWeaponItemId = itemId;
            PreviewModules = modules; PreviewLoadedAmmo = Mathf.Max(0, ammo); PreviewMagazineCapacity = Mathf.Max(0, capacity);
            if (!changed) return;
            m_ActionBag = null; m_BagRemaining = 0;
            m_EquipmentNeedsSettle = true;
            // A new weapon must not inherit the old weapon's showcase pose.
            if (m_Graph.IsValid())
            {
                m_Gestures?.RestoreBeforeAnimation();
                m_Motion = 0; m_Slot = 0; m_Transition = false; m_Blend = 0;
                SetClip(0, 0); SetClip(1, 0);
                m_Mixer.SetInputWeight(0, 1); m_Mixer.SetInputWeight(1, 0);
                m_Graph.Evaluate(0);
                m_Remaining = IdleDuration();
            }
        }

        // Arsenal weapons have their own baked gestures, but share the rifle's
        // aim/fire/reload entries in the existing menu action catalogue.
        private bool EquipmentMotionAllowed(int index)
        {
            var option = m_Settings.Motion(index);
            int weapon = (int)EquippedPreviewWeapon;
            if (index == 1) return weapon > 0;
            return option.Weapon == 0 || option.Weapon == (weapon >= 5 ? 1 : weapon) && weapon > 0;
        }

        private int EquipmentGestureWeapon(int index) => EquipmentMotionAllowed(index) && m_Settings.Motion(index).Weapon > 0
            ? (int)EquippedPreviewWeapon : 0;

        private void InitializeEquipmentVisuals()
        {
            ReleaseEquipmentVisuals();
            // Retire the baked menu halo even when the equipped inventory is empty.
            foreach (var transform in m_Model.GetComponentsInChildren<Transform>(true))
                if (transform.name == "HaloCrosshair") transform.gameObject.SetActive(false);
            m_Halo = null;
            m_EquipmentSource = m_Settings.HaloSource;
            if (!m_EquipmentSource || !m_EquipmentSource.haloVisual) return;
            m_EquipmentHeadOffset = JsonUtility.FromJson<SerializedHaloPose>(JsonUtility.ToJson(m_EquipmentSource)).hhh;
            m_EquipmentAssembly = new GameObject("Actual equipped halo preview");
            m_EquipmentAssembly.SetActive(false);
            m_EquipmentAssembly.transform.SetParent(m_Model.transform, false);
            var clone = UnityEngine.Object.Instantiate(m_EquipmentSource.haloVisual.gameObject, m_EquipmentAssembly.transform, false);
            m_Halo = clone.transform;
            m_Halo.localPosition = Vector3.zero; m_Halo.localRotation = Quaternion.identity; m_Halo.localScale = Vector3.one;
            foreach (var behaviour in clone.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
            foreach (var collider in clone.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            { renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; }
            m_EquipmentRifle = CloneEquipmentComponent(m_EquipmentSource.rifleVisual);
            m_EquipmentShotgun = CloneEquipmentComponent(m_EquipmentSource.shotgunVisual);
            m_EquipmentRevolver = CloneEquipmentComponent(m_EquipmentSource.revolverVisual);
            m_EquipmentSniper = CloneEquipmentComponent(m_EquipmentSource.sniperVisual);
            var sourceArsenal = m_EquipmentSource.arsenalVisuals ?? Array.Empty<ArsenalHaloVisual>();
            m_EquipmentArsenal = new ArsenalHaloVisual[sourceArsenal.Length];
            for (int i = 0; i < sourceArsenal.Length; i++) m_EquipmentArsenal[i] = CloneEquipmentComponent(sourceArsenal[i]);
            m_EquipmentCore = CloneEquipmentComponents(m_EquipmentSource.haloStrokes);
            m_EquipmentGlow = CloneEquipmentComponents(m_EquipmentSource.haloGlowStrokes);
            m_EquipmentLight = CloneEquipmentComponent(m_EquipmentSource.haloLight);
            m_EquipmentFaceFront = CloneEquipmentComponent(m_EquipmentSource.haloFaceLightFront);
            m_EquipmentFaceBack = CloneEquipmentComponent(m_EquipmentSource.haloFaceLightBack);
            m_EquipmentRifleModules = CreateEquipmentModule(m_EquipmentRifle, true);
            m_EquipmentShotgunModules = CreateEquipmentModule(m_EquipmentShotgun, false);
            m_EquipmentRevolverModules = CreateEquipmentModule(m_EquipmentRevolver, false);
            m_EquipmentSniperModules = CreateEquipmentModule(m_EquipmentSniper, true);
            if (m_EquipmentSource.beamCore && m_EquipmentSource.beamCore.sharedMaterial)
            {
                var discharge = new GameObject("Actual halo pressure release"); discharge.transform.SetParent(m_Halo, false);
                m_EquipmentDischarge = discharge.AddComponent<HaloDischargeVisual>(); m_EquipmentDischarge.enabled = false;
                m_EquipmentDischarge.Configure(m_EquipmentSource.beamCore.sharedMaterial);
            }
            foreach (var transform in m_EquipmentAssembly.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = ViewLayer;
            UnequipEquipmentHalos();
            m_EquipmentNeedsSettle = true;
            ApplyEquipmentVisualState();
            m_EquipmentAssembly.SetActive(true);
        }

        private T CloneEquipmentComponent<T>(T source) where T : Component
        {
            if (!source || !source.transform.IsChildOf(m_EquipmentSource.haloVisual)) return null;
            var route = new Stack<int>();
            for (var node = source.transform; node != m_EquipmentSource.haloVisual; node = node.parent) route.Push(node.GetSiblingIndex());
            var target = m_Halo;
            while (route.Count > 0) target = target.GetChild(route.Pop());
            return target.GetComponent<T>();
        }

        private T[] CloneEquipmentComponents<T>(T[] source) where T : Component
        {
            if (source == null) return Array.Empty<T>();
            var result = new T[source.Length];
            for (int i = 0; i < source.Length; i++) result[i] = CloneEquipmentComponent(source[i]);
            return result;
        }

        private HaloModuleVisual CreateEquipmentModule(Component parent, bool rifle)
        {
            if (!parent) return null;
            var node = new GameObject("Actual halo modification layers"); node.transform.SetParent(parent.transform, false);
            var visual = node.AddComponent<HaloModuleVisual>(); visual.enabled = false; visual.Configure(rifle);
            return visual;
        }

        private void UnequipEquipmentHalos()
        {
            m_EquipmentRifle?.SetEquipped(false, 0); m_EquipmentShotgun?.SetEquipped(false, 0);
            m_EquipmentRevolver?.SetEquipped(false, 0); m_EquipmentSniper?.SetEquipped(false, 0);
            foreach (var visual in m_EquipmentArsenal) if (visual) { visual.SetEquipped(true); visual.SetEquipped(false); }
        }

        private ArsenalHaloVisual EquippedArsenal()
        {
            foreach (var visual in m_EquipmentArsenal) if (visual && visual.weapon == EquippedPreviewWeapon) return visual;
            return null;
        }

        private void ApplyEquipmentVisualState()
        {
            if (!m_Halo) return;
            if (m_EquipmentAppliedWeapon != PreviewWeaponId || m_EquipmentAppliedItem != PreviewWeaponItemId)
            {
                UnequipEquipmentHalos();
                m_EquipmentDischarge?.Clear();
                m_EquipmentAppliedWeapon = PreviewWeaponId; m_EquipmentAppliedItem = PreviewWeaponItemId;
            }
            var weapon = EquippedPreviewWeapon;
            m_Halo.gameObject.SetActive(weapon != DollSingerHaloAim.HaloWeapon.None);
            m_EquipmentRifle?.SetMagazineCapacity(PreviewMagazineCapacity);
            m_EquipmentShotgun?.SetModuleProfile(weapon == DollSingerHaloAim.HaloWeapon.Shotgun ? PreviewModules : (ushort)0,
                weapon == DollSingerHaloAim.HaloWeapon.Shotgun ? PreviewMagazineCapacity : 4);
            m_EquipmentRifle?.SetEquipped(weapon == DollSingerHaloAim.HaloWeapon.Rifle, PreviewLoadedAmmo);
            m_EquipmentShotgun?.SetEquipped(weapon == DollSingerHaloAim.HaloWeapon.Shotgun, PreviewLoadedAmmo);
            m_EquipmentRevolver?.SetEquipped(weapon == DollSingerHaloAim.HaloWeapon.Revolver, PreviewLoadedAmmo);
            m_EquipmentSniper?.SetEquipped(weapon == DollSingerHaloAim.HaloWeapon.Sniper, PreviewLoadedAmmo);
            foreach (var visual in m_EquipmentArsenal) if (visual) visual.SetEquipped(visual.weapon == weapon);
            foreach (var stroke in m_EquipmentCore) if (stroke) stroke.enabled = weapon == DollSingerHaloAim.HaloWeapon.Rifle && !m_EquipmentRifle;
            foreach (var stroke in m_EquipmentGlow) if (stroke) stroke.enabled = weapon == DollSingerHaloAim.HaloWeapon.Rifle && !m_EquipmentRifle;
            m_EquipmentRifle?.SetNetworkState(PreviewLoadedAmmo, m_EquipmentReloading, m_EquipmentReloadProgress,
                m_EquipmentShotTick, m_EquipmentReloadTick, m_EquipmentReloadTarget);
            m_EquipmentShotgun?.SetNetworkState(PreviewLoadedAmmo, m_EquipmentReloading, m_EquipmentReloadProgress,
                m_EquipmentShotTick, m_EquipmentReloadTick, m_EquipmentReloadTarget);
            m_EquipmentRevolver?.SetNetworkState(PreviewLoadedAmmo, m_EquipmentReloading, m_EquipmentReloadProgress,
                m_EquipmentShotTick, m_EquipmentReloadTick);
            m_EquipmentSniper?.SetNetworkState(PreviewLoadedAmmo, m_EquipmentReloading, m_EquipmentReloadProgress,
                m_EquipmentShotTick, m_EquipmentReloadTick, m_EquipmentReloadTarget);
            EquippedArsenal()?.SetNetworkState(PreviewLoadedAmmo, PreviewMagazineCapacity, m_EquipmentReloading,
                m_EquipmentReloadProgress, m_EquipmentShotTick, m_EquipmentReloadTick, m_EquipmentReloadTarget, m_EquipmentBurstIndex);
            var module = weapon == DollSingerHaloAim.HaloWeapon.Rifle ? m_EquipmentRifleModules :
                weapon == DollSingerHaloAim.HaloWeapon.Shotgun ? m_EquipmentShotgunModules :
                weapon == DollSingerHaloAim.HaloWeapon.Revolver ? m_EquipmentRevolverModules :
                weapon == DollSingerHaloAim.HaloWeapon.Sniper ? m_EquipmentSniperModules : null;
            module?.SetState(PreviewModules, PreviewLoadedAmmo, PreviewMagazineCapacity, m_EquipmentReloading,
                m_EquipmentReloadProgress, m_EquipmentReloadTarget, m_EquipmentShotTick, m_EquipmentBurstIndex);
        }

        private void SampleEquipmentVisuals(float dt)
        {
            if (!m_Halo || !m_EquipmentSource || !m_Head || !m_Finger) return;
            dt = float.IsFinite(dt) ? Mathf.Max(0, dt) : 0;
            float pose = DollSingerView.AimPoseBlend(HaloAimWeight);
            m_EquipmentRifle?.SetAimBlend(pose); m_EquipmentShotgun?.SetAimBlend(pose);
            m_EquipmentRevolver?.SetAimBlend(pose); m_EquipmentSniper?.SetAimBlend(pose);
            var arsenal = EquippedArsenal(); arsenal?.SetAimBlend(pose);
            m_EquipmentRifleModules?.SetAimBlend(pose); m_EquipmentShotgunModules?.SetAimBlend(pose);
            m_EquipmentRevolverModules?.SetAimBlend(pose); m_EquipmentSniperModules?.SetAimBlend(pose);
            float settle = m_EquipmentNeedsSettle ? 1 : dt;
            if (m_EquipmentRifle && m_EquipmentRifle.gameObject.activeSelf) m_EquipmentRifle.Step(dt);
            if (m_EquipmentShotgun && m_EquipmentShotgun.gameObject.activeSelf) m_EquipmentShotgun.Step(settle);
            if (m_EquipmentRevolver && m_EquipmentRevolver.gameObject.activeSelf) m_EquipmentRevolver.Step(settle);
            if (m_EquipmentSniper && m_EquipmentSniper.gameObject.activeSelf) m_EquipmentSniper.Step(dt);
            arsenal?.Step(dt);
            if (m_EquipmentRifle && m_EquipmentRifle.gameObject.activeSelf) m_EquipmentRifleModules?.Step(dt);
            if (m_EquipmentShotgun && m_EquipmentShotgun.gameObject.activeSelf) m_EquipmentShotgunModules?.Step(dt);
            if (m_EquipmentRevolver && m_EquipmentRevolver.gameObject.activeSelf) m_EquipmentRevolverModules?.Step(dt);
            if (m_EquipmentSniper && m_EquipmentSniper.gameObject.activeSelf) m_EquipmentSniperModules?.Step(dt);
            m_EquipmentDischarge?.Step(dt);
            m_EquipmentNeedsSettle = false;
            float t = pose * pose * (3f - 2f * pose);
            var root = m_Model.transform;
            var head = m_Head.position + root.up * (DollSingerHaloAim.IdleHeadHeight + m_EquipmentHeadOffset) +
                root.right * Mathf.Sin((float)m_BaseTime * 1.8f) * .008f;
            var finger = m_Finger.position + root.forward * m_EquipmentSource.fingertipHaloGap;
            var control = head + root.up * .17f + root.right * .16f;
            m_Halo.position = (1 - t) * (1 - t) * head + 2 * (1 - t) * t * control + t * t * finger;
            m_Halo.rotation = Quaternion.Slerp(Quaternion.LookRotation(root.up, root.forward), Quaternion.LookRotation(root.forward, root.up), t);
            float scale = Mathf.Lerp(m_EquipmentSource.idleHaloScale, m_EquipmentSource.aimedHaloScale, t);
            var parentScale = m_Halo.parent.lossyScale;
            m_Halo.localScale = new Vector3(scale / Mathf.Max(.001f, parentScale.x), scale / Mathf.Max(.001f, parentScale.y), scale / Mathf.Max(.001f, parentScale.z));
            m_EquipmentHaloColor = arsenal ? arsenal.lightColor : EquippedPreviewWeapon == DollSingerHaloAim.HaloWeapon.Rifle && m_EquipmentRifle ? m_EquipmentRifle.lightColor :
                EquippedPreviewWeapon == DollSingerHaloAim.HaloWeapon.Shotgun && m_EquipmentShotgun ? m_EquipmentShotgun.lightColor :
                EquippedPreviewWeapon == DollSingerHaloAim.HaloWeapon.Revolver && m_EquipmentRevolver ? m_EquipmentRevolver.lightColor :
                EquippedPreviewWeapon == DollSingerHaloAim.HaloWeapon.Sniper && m_EquipmentSniper ? m_EquipmentSniper.lightColor :
                Color.Lerp(m_EquipmentSource.idleHaloColor, m_EquipmentSource.aimedHaloColor, t);
            foreach (var stroke in m_EquipmentCore) if (stroke) stroke.startColor = stroke.endColor = m_EquipmentHaloColor;
            var glow = m_EquipmentHaloColor; glow.a = m_EquipmentSource.glowAlpha;
            foreach (var stroke in m_EquipmentGlow) if (stroke) stroke.startColor = stroke.endColor = glow;
            if (m_EquipmentPendingDischarge != 0)
            { m_EquipmentDischarge?.Play(EquippedPreviewWeapon, m_EquipmentHaloColor, m_EquipmentPendingDischarge); m_EquipmentPendingDischarge = 0; }
            float idleIntensity = m_EquipmentSource.idleLightIntensity * HaloLighting.IdlePercent(m_EquipmentLightingSettings) / 100f;
            float aimScale = HaloLighting.AimPercent(m_EquipmentLightingSettings) / 100f;
            float idleRange = HaloLighting.Range(m_EquipmentLightingSettings, m_EquipmentSource.lightRange);
            float intensity = Mathf.Lerp(idleIntensity, m_EquipmentSource.aimedLightIntensity * aimScale, t);
            float flash = m_EquipmentDischarge ? m_EquipmentDischarge.Flash : 0;
            PoseEquipmentLight(m_EquipmentLight, intensity + flash * 5, Mathf.Lerp(idleRange, m_EquipmentSource.lightRange, t));
            PoseEquipmentLight(m_EquipmentFaceFront, Mathf.Lerp(intensity * m_EquipmentSource.faceLightIntensityScale, m_EquipmentSource.flashlightIntensity * aimScale, t),
                Mathf.Lerp(idleRange, HaloLighting.AimRange(m_EquipmentLightingSettings, m_EquipmentSource.flashlightRange), t));
            PoseEquipmentLight(m_EquipmentFaceBack, intensity * m_EquipmentSource.faceLightIntensityScale + flash * 2, Mathf.Lerp(idleRange, m_EquipmentSource.lightRange, t));
            if (m_EquipmentFaceFront)
            {
                m_EquipmentFaceFront.spotAngle = Mathf.Lerp(m_EquipmentSource.faceSpotAngle, m_EquipmentSource.flashlightAngle, t);
                m_EquipmentFaceFront.innerSpotAngle = m_EquipmentFaceFront.spotAngle * Mathf.Lerp(.55f, .72f, t);
            }
            if (m_EquipmentFaceBack)
            { m_EquipmentFaceBack.spotAngle = m_EquipmentSource.faceSpotAngle; m_EquipmentFaceBack.innerSpotAngle = m_EquipmentSource.faceSpotAngle * .55f; }
        }

        private void PoseEquipmentLight(Light light, float intensity, float range)
        {
            if (!light) return;
            light.color = new Color(m_EquipmentHaloColor.r, m_EquipmentHaloColor.g, m_EquipmentHaloColor.b, 1);
            light.intensity = intensity; light.range = range;
            light.enabled = m_EquipmentLightEnabled && m_EquipmentSource.haloLightEnabled && EquippedPreviewWeapon != DollSingerHaloAim.HaloWeapon.None;
        }

        private void ReleaseEquipmentVisuals()
        {
            if (m_EquipmentAssembly) { m_EquipmentAssembly.SetActive(false); Destroy(m_EquipmentAssembly); }
            m_EquipmentAssembly = null; m_EquipmentSource = null;
            m_EquipmentRifle = null; m_EquipmentShotgun = null; m_EquipmentRevolver = null; m_EquipmentSniper = null;
            m_EquipmentArsenal = Array.Empty<ArsenalHaloVisual>();
            m_EquipmentRifleModules = m_EquipmentShotgunModules = m_EquipmentRevolverModules = m_EquipmentSniperModules = null;
            m_EquipmentCore = m_EquipmentGlow = Array.Empty<LineRenderer>();
            m_EquipmentLight = m_EquipmentFaceFront = m_EquipmentFaceBack = null;
            m_EquipmentDischarge = null;
            m_EquipmentAppliedWeapon = DollSingerWeapons.None; m_EquipmentAppliedItem = null;
            m_Halo = null;
        }
    }
}
