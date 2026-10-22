using System;
using Unity.MP_FPS.Client;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    // One lightweight UI scope; no capture camera, render texture or simulation pause.
    public sealed class RaidMapView : IDisposable
    {
        private readonly VisualElement m_Scope, m_Overlay;
        private readonly BountyClashMapView m_Map;
        private readonly Label m_Hint, m_Status, m_Zoom, m_Legend;
        private readonly Button m_Close, m_Center, m_Full, m_In, m_Out;
        private readonly Action m_OnClose;
        public RaidMapView(VisualElement host, BountyClashMapView map, Action close)
        {
            m_Map = map; m_OnClose = close;
            m_Scope = Resources.Load<VisualTreeAsset>("Moonkov/UI/RaidMapView").CloneTree();
            m_Scope.name = "raidMapScope"; m_Scope.AddToClassList("archive-ui"); m_Scope.pickingMode = PickingMode.Ignore;
            m_Scope.style.position = Position.Absolute; m_Scope.style.left = m_Scope.style.top = 0;
            m_Scope.style.transformOrigin = new TransformOrigin(Length.Percent(0), Length.Percent(0), 0);
            host.Add(m_Scope); m_Overlay = m_Scope.Q("raidMapOverlay");
            BountyUIComposition.Window(m_Scope.Q("raidMapWindow"));
            m_Hint = m_Scope.Q<Label>("raidMapHint"); m_Status = m_Scope.Q<Label>("raidMapStatus");
            m_Zoom = m_Scope.Q<Label>("raidMapZoom"); m_Legend = m_Scope.Q<Label>("raidMapLegend");
            m_Close = m_Scope.Q<Button>("raidMapClose"); m_Close.clicked += Close;
            m_Center = m_Scope.Q<Button>("raidMapCenter"); m_Center.clicked += Center;
            m_Full = m_Scope.Q<Button>("raidMapFull"); m_Full.clicked += Full;
            m_In = m_Scope.Q<Button>("raidMapZoomIn"); m_In.clicked += ZoomIn;
            m_Out = m_Scope.Q<Button>("raidMapZoomOut"); m_Out.clicked += ZoomOut;
            map.name = "raidMapCanvas"; m_Scope.Q("raidMapCanvasHost").Add(map);
            m_Overlay.RegisterCallback<KeyDownEvent>(Key);
            m_Overlay.RegisterCallback<WheelEvent>(SwallowWheel);
            m_Overlay.RegisterCallback<PointerDownEvent>(SwallowDown);
            m_Overlay.RegisterCallback<PointerUpEvent>(SwallowUp);
            MoonkovLocalization.Bind(m_Scope); map.Relocalize(); Present(false, false, false, default);
        }
        public void Present(bool available, bool open, bool clash, ClashSnapshotRpc snapshot)
        {
            RefreshViewport();
            m_Hint.style.display = available && !open ? DisplayStyle.Flex : DisplayStyle.None;
            m_Overlay.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open) { m_Map.CancelDrag(); return; }
            MoonkovLocalization.Set(m_Status, !clash ? "EXPEDITION / SCHEMATIC SUPPLY AND EXIT MAP" :
                snapshot.Bounty == ClashBountyState.Extracted ? "BOUNTY HAS LEFT THE ARENA" :
                snapshot.Bounty == ClashBountyState.Carried && !snapshot.IsCarrier && !snapshot.TeamHasBounty ? "RIVAL BOUNTY / APPROXIMATE AREA ONLY" :
                snapshot.Bounty == ClashBountyState.Ground || snapshot.Bounty == ClashBountyState.Carried ? "BOUNTY / EXACT LOCATION" : "TARGET / BOUNTY NOT YET AVAILABLE");
            MoonkovLocalization.Set(m_Legend, clash ? "YOU · BOUNTY · TEAM · TARGET · A/B EXITS" : "YOU · SUPPLIES · EXIT");
            MoonkovLocalization.Set(m_Zoom, "ZOOM / {0:0.0}×", m_Map.Zoom);
            m_In.tooltip = MoonkovLocalization.Text("ZOOM IN"); m_Out.tooltip = MoonkovLocalization.Text("ZOOM OUT");
            m_In.SetEnabled(m_Map.Zoom < BountyClashMapView.MaximumZoom); m_Out.SetEnabled(m_Map.Zoom > BountyClashMapView.MinimumZoom);
        }
        public void Open() { m_Scope.BringToFront(); m_Overlay.style.display = DisplayStyle.Flex; m_Map.Focus(); }
        public void Hide() { m_Map.CancelDrag(); m_Overlay.style.display = DisplayStyle.None; m_Hint.style.display = DisplayStyle.None; }
        private void RefreshViewport()
        {
            var parent = m_Scope.parent;
            if (parent == null || parent.contentRect.width < 1 || Screen.width < 1 || Screen.height < 1) return;
            float width = Mathf.Min(Screen.width, parent.contentRect.width), scale = parent.contentRect.width / width;
            m_Scope.style.width = width; m_Scope.style.height = Screen.height * width / Screen.width;
            m_Scope.style.scale = new Scale(new Vector3(scale, scale, 1));
        }
        private void Key(KeyDownEvent evt)
        { if (evt.keyCode == KeyCode.Escape) { m_OnClose(); evt.StopImmediatePropagation(); } }
        private static void SwallowWheel(WheelEvent evt) { evt.StopPropagation(); }
        private static void SwallowDown(PointerDownEvent evt) => evt.StopPropagation();
        private static void SwallowUp(PointerUpEvent evt) => evt.StopPropagation();
        private void Close() => m_OnClose();
        private void Center() => m_Map.CenterOnPlayer();
        private void Full() => m_Map.FullMap();
        private void ZoomIn() => m_Map.ZoomBy(1.25f);
        private void ZoomOut() => m_Map.ZoomBy(.8f);
        public void Dispose()
        {
            m_Map.CancelDrag(); m_Close.clicked -= Close; m_Center.clicked -= Center; m_Full.clicked -= Full;
            m_In.clicked -= ZoomIn; m_Out.clicked -= ZoomOut;
            m_Overlay.UnregisterCallback<KeyDownEvent>(Key); m_Overlay.UnregisterCallback<WheelEvent>(SwallowWheel);
            m_Overlay.UnregisterCallback<PointerDownEvent>(SwallowDown); m_Overlay.UnregisterCallback<PointerUpEvent>(SwallowUp);
            m_Scope.RemoveFromHierarchy();
        }
    }
}
