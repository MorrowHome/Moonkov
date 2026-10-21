using System;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>Mode-specific anchors on the existing, collidable MC00 compound.
    /// Runtime-only attachment leaves the expedition scene's authored values intact.</summary>
    [DisallowMultipleComponent]
    public sealed class BountyClashArenaSettings : MonoBehaviour
    {
        public const string SceneName = "TheophilusGameScene";
        public static BountyClashArenaSettings Active { get; private set; }
        [SerializeField] private Vector3 m_Center = new Vector3(1620, 211.6f, 2168);
        [SerializeField] private float m_Radius = 130;
        [SerializeField] private Vector3 m_Target = new Vector3(1613.5f, 212.0862f, 2157);
        [SerializeField] private Vector3[] m_Spawns = {
            new Vector3(1585,211.0841f,2125), new Vector3(1584.25f,210.9849f,2126.299f), new Vector3(1583.5f,210.9849f,2125),
            new Vector3(1670,206.1514f,2115), new Vector3(1671.5f,205.854f,2115), new Vector3(1670.75f,206.1811f,2116.299f),
            new Vector3(1660,214.1329f,2240), new Vector3(1661.5f,214.0833f,2240), new Vector3(1659.25f,214.3162f,2241.299f),
            new Vector3(1600.049f,214.1275f,2235.695f), new Vector3(1599.974f,214.2294f,2237.524f), new Vector3(1599.893f,214.3537f,2239.477f)
        };
        [SerializeField] private Vector3[] m_Exits = {
            new Vector3(1599.954f,204.4442f,2067.8f), new Vector3(1625,216.3493f,2280)
        };
        [SerializeField] private float m_ExitRadius = 8, m_BlockRadius = 15;
        private ClashArenaDefinition m_Frozen;
        private BountyClashMapPresentation m_Presentation;
        private bool m_ModeActive;
        private float m_BanishProgress;
        private bool m_BountyAvailable, m_HasBountyPosition;
        private Vector3 m_BountyPosition;
        public bool ModeActive => m_ModeActive;
        // Server consumers own their copy for the match; callers cannot move future spawns.
        public ClashArenaDefinition Definition
        {
            get
            {
                Freeze();
                return new ClashArenaDefinition {Center=m_Frozen.Center,Target=m_Frozen.Target,Radius=m_Frozen.Radius,
                    Spawns=(ClashPoint[])m_Frozen.Spawns.Clone(),Exits=(ClashPoint[])m_Frozen.Exits.Clone(),
                    ExitRadius=m_Frozen.ExitRadius,BlockRadius=m_Frozen.BlockRadius,BanishSeconds=m_Frozen.BanishSeconds,
                    ReadySeconds=m_Frozen.ReadySeconds,ReconnectSeconds=m_Frozen.ReconnectSeconds};
            }
        }
        public bool Contains(Vector3 position)
        {
            Freeze();return ClashPoint.HorizontalSquared(Point(position),m_Frozen.Center)<=m_Frozen.Radius*(double)m_Frozen.Radius;
        }
        public static BountyClashArenaSettings EnsureForMap(MoonRaidMap map)
        {
            if(!map || map.gameObject.scene.name!=SceneName)return null;
            var settings=map.GetComponent<BountyClashArenaSettings>();
            return settings ? settings : map.gameObject.AddComponent<BountyClashArenaSettings>();
        }
        private void Freeze()
        {
            if(m_Frozen!=null)return;
            var spawns=new ClashPoint[m_Spawns?.Length ?? 0];var exits=new ClashPoint[m_Exits?.Length ?? 0];
            for(int i=0;i<spawns.Length;i++)spawns[i]=Point(m_Spawns[i]);
            for(int i=0;i<exits.Length;i++)exits[i]=Point(m_Exits[i]);
            m_Frozen=new ClashArenaDefinition {Center=Point(m_Center),Target=Point(m_Target),Radius=m_Radius,
                Spawns=spawns,Exits=exits,ExitRadius=m_ExitRadius,BlockRadius=m_BlockRadius};
        }
        private static ClashPoint Point(Vector3 p) => new ClashPoint(p.x,p.y,p.z);
        public static Vector3 Position(ClashPoint p) => new Vector3(p.X,p.Y,p.Z);
        private void OnEnable() {Freeze();Active=this;}
        private void OnDisable()
        {
            if(Active==this)Active=null;
            m_Presentation?.Dispose();m_Presentation=null;m_ModeActive=false;
        }
        private void OnValidate() {if(!Application.isPlaying)m_Frozen=null;}
        private void Update()
        {
            bool active=false;
            foreach(var world in World.All)
                if(world.IsCreated && (world.IsServer() || world.IsClient()) && BountyClashRuntime.Enabled(world.EntityManager))
                {active=true;break;}
            m_ModeActive=active;
            if(!active)
            {
                if(m_Presentation!=null) {m_Presentation.Dispose();m_Presentation=null;}
                return;
            }
            if(m_Presentation!=null) {m_Presentation.FaceLabels(Camera.main);return;}
            if(!ClientServerBootstrap.HasClientWorlds)return;
            Freeze();
            string error=m_Frozen.Validate();
            if(error!=null) {Debug.LogError("Bounty Clash map configuration: "+error,this);enabled=false;return;}
            m_Presentation=new BountyClashMapPresentation(transform,m_Frozen);
            if(m_HasBountyPosition)m_Presentation.SetBountyPosition(m_BountyPosition);
            m_Presentation.PresentObjective(m_BanishProgress,m_BountyAvailable);
        }
        // Presentation only. The HUD passes approved snapshot values; this never advances gameplay.
        public void PresentObjective(float progress,bool bountyAvailable,Vector3 bountyPosition)
        {
            if(float.IsFinite(bountyPosition.x) && float.IsFinite(bountyPosition.y) && float.IsFinite(bountyPosition.z))
            {
                m_BountyPosition=bountyPosition;m_HasBountyPosition=true;
                m_Presentation?.SetBountyPosition(bountyPosition);
            }
            PresentObjective(progress,bountyAvailable);
        }
        public void PresentObjective(float progress,bool bountyAvailable)
        {
            m_BanishProgress=float.IsFinite(progress) ? Mathf.Clamp01(progress) : 0;
            m_BountyAvailable=bountyAvailable;
            m_Presentation?.PresentObjective(m_BanishProgress,m_BountyAvailable);
        }
    }
}
