using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class HalosMainRoom
    {
        private VisualElement m_CharacterHost, m_Preview, m_PreviewStage;
        private Image m_HomePortrait;
        private Button m_CharacterHit, m_MoonHit, m_PreviewBack;
        private MenuCharacterView m_HomeCharacter, m_PreviewCharacter;
        private MenuMoonView m_PreviewMoon;
        private InventoryGraph m_Equipment;
        private IVisualElementScheduledItem m_PreviewFade;
        private bool m_PreviewMoonSelected;
        public bool IsPreviewOpen { get; private set; }

        private void InitializeSubjects()
        {
            m_CharacterHost = Root.Q("halosRoomCharacter");
            m_HomePortrait = Root.Q<Image>("halosRoomPortrait");
            m_CharacterHit = Root.Q<Button>("halosRoomInspectCharacter");
            m_MoonHit = Root.Q<Button>("halosRoomInspectMoon");
            // The window hit area and actor sit behind the original monitor controls.
            m_MoonHit.RemoveFromHierarchy(); m_World.Add(m_MoonHit);
            m_CharacterHost.RemoveFromHierarchy(); m_World.Add(m_CharacterHost);
            m_CharacterHit.clicked += OpenCharacterPreview;
            m_MoonHit.clicked += OpenMoonPreview;
            m_Preview = Root.Q("halosSubjectPreview");
            m_PreviewStage = Root.Q("halosSubjectStage");
            m_PreviewBack = Root.Q<Button>("halosSubjectBack");
            m_PreviewBack.clicked += () => ClosePreview();
            Root.Q<Button>("halosSubjectReset").clicked += () =>
            {
                m_PreviewCharacter?.ResetInspection(); m_PreviewMoon?.ResetView();
            };
            MoonkovLocalization.Bind(m_Preview);
        }

        public void PresentEquipment(InventoryGraph inventory)
        {
            m_Equipment = inventory;
            m_HomeCharacter?.SetEquipmentInventory(inventory);
            m_PreviewCharacter?.SetEquipmentInventory(inventory);
        }

        private void UpdateSubjectInput(bool ready)
        {
            m_CharacterHit.SetEnabled(ready); m_MoonHit.SetEnabled(ready);
            m_CharacterHit.style.display = m_MoonHit.style.display = ready ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateHomeCharacter()
        {
            bool visible = m_Visible && !m_Disposed && !IsPreviewOpen && Root.panel != null && Mathf.Abs(m_Camera - 1920) < 1550;
            m_CharacterHost.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) { ReleaseHomeCharacter(); return; }
            if (m_HomeCharacter != null) return;
            m_HomeCharacter = new MenuCharacterView(element: m_HomePortrait, homePose: true);
            m_HomePortrait.RemoveFromClassList("terminal-character-art");
            m_HomeCharacter.SetEquipmentInventory(m_Equipment);
        }

        private void ReleaseHomeCharacter()
        {
            m_HomeCharacter?.Dispose(); m_HomeCharacter = null;
        }

        public void OpenCharacterPreview() => OpenPreview(false);
        public void OpenMoonPreview() => OpenPreview(true);

        private void OpenPreview(bool moon)
        {
            if (m_Disposed || !m_Visible || m_Busy || Room != 1 || IsMoving || IsOpening || IsPreviewOpen || m_TabletState > 0) return;
            IsPreviewOpen = true; m_PreviewMoonSelected = moon; m_Edge = 0;
            ResetParallax();
            m_Owner.AddToClassList("halos-subject-open");
            ReleaseHomeCharacter(); UpdateInput();
            m_Viewport.style.display = DisplayStyle.None;
            m_Preview.style.display = DisplayStyle.Flex;
            MoonkovLocalization.Set(m_Preview.Q<Label>("halosSubjectTitle"), moon ? "月球预览" : "角色预览");
            Image art;
            if (moon)
            {
                m_PreviewMoon = new MenuMoonView(() => { }) { Observing = true, Paused = true };
                art = m_PreviewMoon.Element;
                art.RemoveFromClassList("moon-backdrop-view");
            }
            else
            {
                m_PreviewCharacter = new MenuCharacterView();
                m_PreviewCharacter.SetEquipmentInventory(m_Equipment);
                art = m_PreviewCharacter.Element;
                art.RemoveFromClassList("terminal-character-art");
            }
            art.name = "halosSubjectArt"; art.AddToClassList("halos-subject-art"); m_PreviewStage.Add(art);
            m_PreviewBack.Focus();
            if (Animate)
                m_PreviewFade = TerminalMotion.Tween(m_Preview, .25f, t => m_Preview.style.opacity = TerminalMotion.OutCubic(t));
            else m_Preview.style.opacity = 1;
        }

        public void ClosePreview() => ClosePreview(true);
        private void ClosePreview(bool restore)
        {
            if (!IsPreviewOpen) return;
            IsPreviewOpen = false; m_PreviewFade?.Pause(); m_PreviewFade = null;
            m_Owner.RemoveFromClassList("halos-subject-open");
            m_PreviewCharacter?.Dispose(); m_PreviewCharacter = null;
            m_PreviewMoon?.Dispose(); m_PreviewMoon = null;
            m_PreviewStage.Clear(); m_Preview.style.display = DisplayStyle.None;
            m_Viewport.style.display = DisplayStyle.Flex; m_Edge = 0; m_LastTime = TerminalMotion.Now;
            if (!restore || m_Disposed) return;
            Paint();
            (m_PreviewMoonSelected ? m_MoonHit : m_CharacterHit).Focus();
        }
    }
}
