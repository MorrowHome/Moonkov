using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    /// <summary>Read-only client equipment appearance. Never changes health, inventory, oxygen or hitboxes.</summary>
    [DisallowMultipleComponent,DefaultExecutionOrder(180)]
    public sealed partial class EnergyShieldPresentation : MonoBehaviour
    {
        private struct CameraState {public Camera Camera;public bool Hidden;}
        private readonly Stack<CameraState> m_Cameras=new Stack<CameraState>(4);
        private readonly List<Renderer> m_Hardware=new List<Renderer>(12);
        private MaterialPropertyBlock m_ShieldProperties;
        private Transform m_Head,m_Neck,m_Hips,m_Chest,m_LeftFoot,m_RightFoot;
        private Transform m_Shell,m_Collar,m_Device,m_Root;
        private MeshRenderer m_ShellRenderer;
        private Material m_FieldMaterial;
        private bool m_Ready,m_GeometryRetained,m_HasSequence,m_Owned,m_FirstPerson,m_HeadEquipped,m_ShieldEquipped,m_Regenerating,m_Hidden;
        private bool m_StandardCollar;
        private Camera m_OwnedCamera;
        private uint m_HitSequence,m_BreakSequence,m_RestoreSequence;
        private float m_HitAt=-100,m_BreakAt=-100,m_RestoreAt=-100,m_Strength,m_LastSound=-100;
        private Vector3 m_Impact;
        private AudioSource m_Audio;
        private AudioClip m_HitClip,m_BreakClip,m_RestoreClip;
        internal Material FieldMaterial => m_FieldMaterial;
        internal bool HasSurfaces => m_Ready && isActiveAndEnabled &&
            ((m_ShellRenderer && m_ShellRenderer.enabled && !m_ShellRenderer.forceRenderingOff) ||
            (m_VisorRenderer && m_VisorRenderer.enabled && !m_VisorRenderer.forceRenderingOff));

        public void Present(in PredictedPlayerGhost snapshot,bool owned,bool firstPerson,Camera ownedCamera)
        {
            if(!EnsureReady())return;
            m_Owned=owned;m_FirstPerson=owned&&firstPerson;m_OwnedCamera=ownedCamera;
            bool head=snapshot.HeadDomeEquipped;
            if(head&&!m_HeadEquipped)m_VisorEquippedAt=Time.unscaledTime;
            m_HeadEquipped=head;m_ShieldEquipped=snapshot.ShieldEquipped;
            m_StandardCollar=snapshot.HeadDomeStandard;
            m_Regenerating=snapshot.ShieldRegenerating && snapshot.CurrentHealth>0 && (!snapshot.ClashEnabled || snapshot.ClashLife==ClashLife.Alive);
            if(!m_HasSequence)
            {m_HitSequence=snapshot.ShieldHitSequence;m_BreakSequence=snapshot.ShieldBreakSequence;m_RestoreSequence=snapshot.ShieldRestoreSequence;m_HasSequence=true;}
            else
            {
                bool hit=NewSequence(snapshot.ShieldHitSequence,ref m_HitSequence),broken=NewSequence(snapshot.ShieldBreakSequence,ref m_BreakSequence),restored=NewSequence(snapshot.ShieldRestoreSequence,ref m_RestoreSequence);
                float now=Time.unscaledTime;
                if(hit){m_HitAt=now;m_Impact=(Vector3)snapshot.ShieldHitPoint;m_Strength=EnergyShieldRules.Resource(snapshot.ShieldHitStrength*100)/100;}
                if(broken)m_BreakAt=now;if(restored)m_RestoreAt=now;
                if(broken)PlayCue(1);else if(hit)PlayCue(0);else if(restored)PlayCue(2);
            }
        }
        // Optional inventory-portrait bridge; uses real item values, not a second loadout model.
        public void PresentInventory(InventoryGraph graph)
        {
            var shield=graph?.Equipped("Shield");var head=graph?.Equipped("Helmet");
            var snapshot=new PredictedPlayerGhost{CurrentHealth=1,ShieldEquipped=shield?.Code==EnergyShieldEquipment.ShieldCode,HeadDomeEquipped=EnergyShieldEquipment.IsHeadDome(head?.Code),HeadDomeStandard=head?.Code==EnergyShieldEquipment.StandardHeadDomeCode};
            Present(snapshot,false,false,null);
        }
        private static bool NewSequence(uint current,ref uint previous)
        {if(current==0){previous=0;return false;}if(unchecked((int)(current-previous))<=0)return false;previous=current;return true;}
        private bool EnsureReady()
        {
            // Unity-owned property blocks are initialized on the presentation path,
            // including after Editor reload/creation where nonserialized fields can be empty.
            m_ShieldProperties ??= new MaterialPropertyBlock();
            if(m_Ready){EnergyShieldRendering.Register(this);return true;}
            var animator=GetComponentInChildren<Animator>();if(!animator || !animator.isHuman)return false;
            var shader=Resources.Load<Shader>("Moonkov/EnergyShield");if(!shader || !shader.isSupported)return false;
            m_Head=animator.GetBoneTransform(HumanBodyBones.Head);if(!m_Head)return false;
            m_Neck=animator.GetBoneTransform(HumanBodyBones.Neck);m_Hips=animator.GetBoneTransform(HumanBodyBones.Hips);m_Chest=animator.GetBoneTransform(HumanBodyBones.Chest);
            m_LeftFoot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);m_RightFoot=animator.GetBoneTransform(HumanBodyBones.RightFoot);
            FitCollar(animator);
            EnergyShieldGeometry.Retain();m_GeometryRetained=true;
            m_FieldMaterial=new Material(shader){name="Energy protection / private surface",hideFlags=HideFlags.HideAndDontSave};
            m_Root=new GameObject("Energy protection / original runtime geometry"){hideFlags=HideFlags.DontSave}.transform;m_Root.SetParent(transform,false);
            m_ShellRenderer=Surface("Transient combat field",out m_Shell);
            m_Collar=new GameObject("Neko life-support collar").transform;m_Collar.SetParent(m_Root,false);
            m_Device=new GameObject("Independent shield generator").transform;m_Device.SetParent(m_Root,false);
            var metal=Resources.Load<Material>("Moonkov/TacticalMetal");var indicator=Resources.Load<Material>("Moonkov/TacticalIndicator");
            if(!metal || !indicator){DisposePresentation();return false;}
            Color dark=new Color(.075f,.105f,.13f),silver=new Color(.38f,.44f,.49f),cyan=new Color(.08f,.6f,.8f);
            BuildCollar(metal,indicator);
            BuildVisor(metal,indicator);
            Hardware(m_Device,EnergyShieldGeometry.Box,Vector3.zero,new Vector3(.20f,.15f,.065f),metal,dark);
            Hardware(m_Device,EnergyShieldGeometry.Box,new Vector3(0,0,.038f),new Vector3(.16f,.11f,.018f),metal,silver);
            Hardware(m_Device,EnergyShieldGeometry.Collar,new Vector3(0,0,.054f),new Vector3(.045f,.018f,.045f),indicator,cyan).transform.localRotation=Quaternion.Euler(90,0,0);
            for(int i=0;i<3;i++)Hardware(m_Device,EnergyShieldGeometry.Box,new Vector3(-.046f+i*.046f,-.041f,.055f),new Vector3(.027f,.009f,.005f),indicator,cyan);
            Unity.MP_FPS.Moon.MoonSilverPrintSubject.Protect(m_Collar.gameObject);Unity.MP_FPS.Moon.MoonSilverPrintSubject.Protect(m_Device.gameObject);
            m_Ready=true;EnergyShieldRendering.Register(this);return true;
        }
        private MeshRenderer Surface(string name,out Transform node)
        {
            node=new GameObject(name){layer=gameObject.layer}.transform;node.SetParent(m_Root,false);
            node.gameObject.AddComponent<MeshFilter>().sharedMesh=EnergyShieldGeometry.Sphere;
            var renderer=node.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=m_FieldMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.enabled=false;return renderer;
        }
        private MeshRenderer Hardware(Transform parent,Mesh mesh,Vector3 position,Vector3 scale,Material material,Color color)
        {
            var node=new GameObject("Original protection hardware"){layer=gameObject.layer};node.transform.SetParent(parent,false);node.transform.localPosition=position;node.transform.localScale=scale;
            node.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=node.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);renderer.SetPropertyBlock(block);m_Hardware.Add(renderer);return renderer;
        }
        private static void WorldPose(Transform node,Vector3 position,Quaternion rotation,Vector3 size)
        {
            node.SetPositionAndRotation(position,rotation);var scale=node.parent?node.parent.lossyScale:Vector3.one;
            node.localScale=new Vector3(size.x/Mathf.Max(.0001f,Mathf.Abs(scale.x)),size.y/Mathf.Max(.0001f,Mathf.Abs(scale.y)),size.z/Mathf.Max(.0001f,Mathf.Abs(scale.z)));
        }
        private void LateUpdate()
        {
            if(!m_Ready || !m_Head)return;
            UpdateCollarPose();
            Vector3 hip=m_Hips?m_Hips.position:transform.position+transform.up*.85f;
            WorldPose(m_Device,hip+transform.right*.18f+transform.forward*.12f,Quaternion.LookRotation(transform.forward,transform.up),Vector3.one);
            m_Collar.gameObject.SetActive(m_HeadEquipped);m_Device.gameObject.SetActive(m_ShieldEquipped);
            m_CollarNekoDetails.gameObject.SetActive(!m_StandardCollar);
            m_CollarStandardDetails.gameObject.SetActive(m_StandardCollar);
            UpdateVisor();
            var feet=m_LeftFoot&&m_RightFoot?(m_LeftFoot.position+m_RightFoot.position)*.5f:transform.position;
            Vector3 axis=m_Head.position-feet;float length=Mathf.Max(.65f,axis.magnitude);axis=axis.sqrMagnitude>.001f?axis.normalized:transform.up;
            Vector3 forward=Vector3.Cross(transform.right,axis);if(forward.sqrMagnitude<.01f)forward=transform.forward;
            WorldPose(m_Shell,(feet+m_Head.position)*.5f,Quaternion.LookRotation(forward,axis),new Vector3(.43f,length*.57f,.34f));
            float now=Time.unscaledTime,hitAge=now-m_HitAt,breakAge=now-m_BreakAt,restoreAge=now-m_RestoreAt;
            m_ShellRenderer.enabled=m_ShieldEquipped && (hitAge<.5f || breakAge<.65f || restoreAge<.6f || m_Regenerating);
            m_ShieldProperties.SetVector("_FieldState",new Vector4(1,1.12f,hitAge,breakAge));m_ShieldProperties.SetVector("_FieldFlow",new Vector4(m_Regenerating?1:0,restoreAge,m_Strength,0));m_ShieldProperties.SetVector("_EnergyHitPoint",m_Impact);m_ShellRenderer.SetPropertyBlock(m_ShieldProperties);
        }
        internal bool OwnerVisor(Camera camera) => m_Ready && m_HeadEquipped && m_Owned && m_FirstPerson && camera==m_OwnedCamera;
        internal void SimulatePresentation() => LateUpdate();
        internal float OwnerPulse(Camera camera)
        {
            if(!m_Ready || !m_Owned || !m_FirstPerson || camera!=m_OwnedCamera || !m_ShieldEquipped)return 0;
            float hit=Mathf.Clamp01(1-(Time.unscaledTime-m_HitAt)/.32f)*m_Strength;
            float broken=Mathf.Clamp01(1-(Time.unscaledTime-m_BreakAt)/.5f);
            return Mathf.Max(hit*.12f,broken*.17f);
        }
        internal void RestoreCameraVisibility(){m_Cameras.Clear();SetHidden(false);}
        internal void BeginCamera(Camera camera)
        {
            m_Cameras.Push(new CameraState{Camera=camera,Hidden=m_Hidden});SetHidden(m_Owned&&m_FirstPerson&&camera==m_OwnedCamera);
        }
        internal void EndCamera(Camera camera)
        {if(m_Cameras.Count==0)return;var state=m_Cameras.Pop();SetHidden(state.Hidden);}
        private void SetHidden(bool hidden)
        {m_Hidden=hidden;if(m_ShellRenderer)m_ShellRenderer.forceRenderingOff=hidden;if(m_VisorRenderer)m_VisorRenderer.forceRenderingOff=hidden;foreach(var renderer in m_Hardware)if(renderer)renderer.forceRenderingOff=hidden;}
        internal bool InView(Camera camera,Plane[] planes)
        {
            if(!HasSurfaces)return false;
            return Visible(m_ShellRenderer)||Visible(m_VisorRenderer);
            bool Visible(Renderer renderer)=>renderer && renderer.enabled && !renderer.forceRenderingOff && (camera.cullingMask&(1<<renderer.gameObject.layer))!=0 && GeometryUtility.TestPlanesAABB(planes,renderer.bounds);
        }
        private void PlayCue(int cue)
        {
            if(!Application.isPlaying || Time.unscaledTime-m_LastSound<.09f)return;
            var game=GameManager.Instance;if(!game || !game.AudioMixer)return;
            if(!m_Audio)
            {
                var groups=game.AudioMixer.FindMatchingGroups(nameof(SoundMixer.SoundMixerGroup.SFX));if(groups.Length==0)return;
                m_Audio=m_Root.gameObject.AddComponent<AudioSource>();m_Audio.playOnAwake=false;m_Audio.outputAudioMixerGroup=groups[0];m_Audio.minDistance=1;m_Audio.maxDistance=24;
                m_HitClip=Tone("Original shield impact",.15f,310,180);m_BreakClip=Tone("Original shield collapse",.35f,220,65);m_RestoreClip=Tone("Original shield return",.28f,130,390);
            }
            m_LastSound=Time.unscaledTime;m_Audio.spatialBlend=m_Owned?0:1;m_Audio.Stop();m_Audio.PlayOneShot(cue==1?m_BreakClip:cue==2?m_RestoreClip:m_HitClip,.45f);
        }
        private static AudioClip Tone(string name,float duration,float from,float to)
        {
            const int rate=16000;var samples=new float[(int)(rate*duration)];float phase=0;
            for(int i=0;i<samples.Length;i++){float u=i/(float)samples.Length;phase+=Mathf.Lerp(from,to,u)*Mathf.PI*2/rate;samples[i]=(Mathf.Sin(phase)+Mathf.Sin(phase*2.03f)*.18f)*Mathf.Sin(Mathf.PI*u)*.12f;}
            var clip=AudioClip.Create(name,samples.Length,1,rate,false);clip.SetData(samples,0);return clip;
        }
        public void ResetPresentation()
        {
            m_HeadEquipped=m_ShieldEquipped=m_Regenerating=m_HasSequence=false;m_HitAt=m_BreakAt=m_RestoreAt=-100;
            if(m_Audio)m_Audio.Stop();if(m_ShellRenderer)m_ShellRenderer.enabled=false;
            if(m_VisorRenderer)m_VisorRenderer.enabled=false;
            if(m_VisorRails!=null)foreach(var rail in m_VisorRails)if(rail)rail.enabled=false;
            if(m_EnergyEars!=null)foreach(var ear in m_EnergyEars)if(ear)ear.enabled=false;
            if(m_Collar)m_Collar.gameObject.SetActive(false);if(m_Device)m_Device.gameObject.SetActive(false);
        }
        public void DisposePresentation()
        {
            EnergyShieldRendering.Unregister(this);SetHidden(false);m_Cameras.Clear();if(m_Audio)m_Audio.Stop();
            DestroyOwned(m_HitClip);DestroyOwned(m_BreakClip);DestroyOwned(m_RestoreClip);DestroyOwned(m_FieldMaterial);if(m_Root)DestroyOwned(m_Root.gameObject);
            m_Hardware.Clear();m_Root=m_Shell=m_Collar=m_Device=null;m_ShellRenderer=null;m_FieldMaterial=null;m_Audio=null;
            m_Visor=null;m_VisorRenderer=null;m_VisorRails=null;
            m_EnergyEars=m_EmitterLenses=null;m_SealHalo=null;m_SealHaloStroke=null;
            m_CollarNekoDetails=m_CollarStandardDetails=null;
            if(m_GeometryRetained){EnergyShieldGeometry.Release();m_GeometryRetained=false;}m_Ready=false;ResetPresentation();
        }
        private void OnDisable()=>DisposePresentation();
        private void OnDestroy()=>DisposePresentation();
        private static void DestroyOwned(Object obj)
        {if(!obj)return;if(Application.isPlaying)Object.Destroy(obj);else Object.DestroyImmediate(obj);}
    }
}
