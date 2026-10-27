using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS
{
    // Original procedural Moonkov props. No Hunt textures, models or audio are used.
    // This component owns visuals only; server PhysicsScene checks own all gameplay.
    [DefaultExecutionOrder(150)]
    public sealed class TacticalItemsFeedback : MonoBehaviour
    {
        private sealed class Visual
        {
            public TacticalWorldStateRpc State; public GameObject Root; public Transform Core;
            public Renderer Smoke; public MaterialPropertyBlock SmokeProperties;
            public Vector3 Position,Velocity; public float Received,Expire; public uint Sequence;
        }
        private static TacticalItemsFeedback s_Active;
        private TacticalItemsTransparentRenderer m_TransparentRenderer;
        private bool m_Destroyed;
        private TacticalItemDefinition m_Selected;
        private bool m_PresentationBlocked;
        private string m_SelectedCode;private GameObject m_Held;
        private DollSingerNetworkPresentation m_Owner;private Transform m_RightHand;private float m_NextOwnerSearch;
        private GameObject m_FirePrefab,m_ChokePrefab,m_PoisonPrefab;
        private GameObject m_Preview;
        private LineRenderer m_Trajectory,m_PreviewRing;
        public static TacticalItemOwnerRpc LocalOwner {get;private set;}
        private readonly Dictionary<uint,Visual> m_Visuals=new();
        private readonly Dictionary<uint,uint> m_Sequences=new();
        private readonly List<uint> m_Expired=new();
        private readonly List<Material> m_Materials=new();
        private Material m_Metal,m_Amber,m_Red,m_Green,m_Cyan,m_White,m_Dark,m_Flame,m_Smoke,m_Poison;
        private Material m_SmokeVolume;
        private static readonly int SmokeDensityId=Shader.PropertyToID("_Density");
        private uint m_Epoch,m_HighestSequence;
        private sealed class CoughEmitter {public GameObject Root;public float Next,Expires;}
        private readonly Dictionary<int,CoughEmitter> m_CoughEmitters=new();
        private readonly List<int> m_ExpiredCoughs=new();


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics(){s_Active=null;LocalOwner=default;}
        public static void SetOwner(TacticalItemOwnerRpc owner)
        {
            var previous = LocalOwner;
            LocalOwner = owner;
            if (owner.RequestId == 0 || previous.RaidId != owner.RaidId ||
                owner.RequestId == previous.RequestId && owner.Feedback == previous.Feedback) return;
            var library = MoonkovAudio.Library;
            if (!library) return;
            if (owner.Feedback == TacticalItemFeedback.Started) MoonkovAudio.Play(library.Equipment, Vector3.zero, .6f);
            else if (owner.Feedback == TacticalItemFeedback.Completed) MoonkovAudio.Play(library.Pickup, Vector3.zero, .7f);
            else if (owner.Feedback != TacticalItemFeedback.None) MoonkovAudio.Error();
        }
        public static TacticalItemsFeedback GetOrCreate()
        {if(!s_Active)s_Active=new GameObject("Network tactical item feedback").AddComponent<TacticalItemsFeedback>();return s_Active;}
        public static void SetPresentationBlocked(bool blocked)
        {if(s_Active)s_Active.m_PresentationBlocked=blocked;}
        public static void SetSelectedItem(string code)
        {
            var definition=TacticalItemRules.Find(code);
            bool medical=Unity.MP_FPS.Inventory.ConsumableCatalog.MedicalRestore(code)>0;
            if(definition==null && code!="energy_grenade" && !medical)
            {
                if(s_Active)
                {
                    s_Active.m_Selected=null;s_Active.m_SelectedCode=null;
                    if(s_Active.m_Held){s_Active.m_Held.SetActive(false);Release(s_Active.m_Held);s_Active.m_Held=null;}
                    if(s_Active.m_Preview)s_Active.m_Preview.SetActive(false);
                }
                return;
            }
            var feedback=GetOrCreate();
            if(feedback.m_SelectedCode==code)return;
            feedback.m_SelectedCode=code;feedback.m_Selected=definition;
            if(feedback.m_Held)Release(feedback.m_Held);feedback.m_Held=null;
        }
        private void Awake()
        {
            s_Active=this;
            m_TransparentRenderer?.Dispose();m_TransparentRenderer=new TacticalItemsTransparentRenderer(this,HasDeferredParticles);
            m_Metal=Material(new Color(.3f,.4f,.44f),true);m_Amber=Material(new Color(1,.42f,.035f));
            m_Red=Material(new Color(.8f,.12f,.08f));m_Green=Material(new Color(.32f,.8f,.1f));
            m_Cyan=Material(new Color(.06f,.8f,.9f));m_White=Material(new Color(.7f,.85f,.9f));m_Dark=Material(new Color(.065f,.085f,.1f),true);
            m_FirePrefab=Resources.Load<GameObject>("Moonkov/TacticalFire");m_PoisonPrefab=Resources.Load<GameObject>("Moonkov/TacticalPoison");m_ChokePrefab=Resources.Load<GameObject>("Moonkov/TacticalChoke");
            m_Flame=VolumeMaterial(new Color(1,.45f,.08f,.9f),true);
            m_Smoke=VolumeMaterial(new Color(.75f,.82f,.88f,.18f),false);
            m_SmokeVolume=new Material(Resources.Load<Shader>("Moonkov/SmokeVolume"));m_Materials.Add(m_SmokeVolume);
            m_Poison=VolumeMaterial(new Color(.42f,.65f,.12f,.2f),false);
            m_Preview=new GameObject("Tactical aiming preview");m_Preview.transform.SetParent(transform,false);
            m_Trajectory=m_Preview.AddComponent<LineRenderer>();m_Trajectory.useWorldSpace=true;m_Trajectory.widthMultiplier=.018f;m_Trajectory.sharedMaterial=m_Cyan;
            var ring=new GameObject("Deployment footprint");ring.transform.SetParent(m_Preview.transform,false);m_PreviewRing=ring.AddComponent<LineRenderer>();
            m_PreviewRing.positionCount=48;m_PreviewRing.loop=true;m_PreviewRing.useWorldSpace=true;m_PreviewRing.widthMultiplier=.022f;m_PreviewRing.sharedMaterial=m_Green;
            m_Preview.SetActive(false);Unity.MP_FPS.Moon.MoonSilverPrintSubject.Protect(m_Preview);
        }
        private bool HasDeferredParticles()
        {
            if(!this || !isActiveAndEnabled)return false;
            foreach(var visual in m_Visuals.Values)
                if(visual.Root && visual.Root.activeInHierarchy &&
                    (visual.State.Phase==TacticalWorldPhase.Smoke || visual.State.Phase==TacticalWorldPhase.Fire || visual.State.Phase==TacticalWorldPhase.Choke || visual.State.Phase==TacticalWorldPhase.Poison))return true;
            // Cough emitters are audio only; they do not need a transparent render pass.
            return false;
        }
        private Material Material(Color color,bool lit=false)
        {
            var template=Resources.Load<Material>(lit ? "Moonkov/TacticalMetal" : "Moonkov/TacticalIndicator");
            var shader=template ? template.shader : Shader.Find(lit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material=template ? new Material(template) : new Material(shader);material.name="Tactical item shared material";
            material.SetColor("_BaseColor",color);material.SetColor("_Color",color);if(lit){material.SetFloat("_Metallic",.6f);material.SetFloat("_Smoothness",.35f);}m_Materials.Add(material);return material;
        }
        private Material VolumeMaterial(Color color,bool flame)
        {
            var shader=Resources.Load<Shader>("Moonkov/TacticalVolume") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var material=new Material(shader){name=flame ? "Original tactical flame" : "Original tactical mist"};
            material.SetColor("_BaseColor",color);material.SetFloat("_Flame",flame ? 1 : 0);m_Materials.Add(material);return material;
        }
        public void Present(in TacticalWorldStateRpc state)
        {
            if(state.Epoch<m_Epoch || state.Id==0 && state.Phase!=TacticalWorldPhase.Removed)return;
            if(state.Epoch>m_Epoch){Clear();m_Epoch=state.Epoch;}
            if(state.Id==0){Clear();return;}
            if(state.Sequence+512<m_HighestSequence || m_Sequences.TryGetValue(state.Id,out var sequence) && state.Sequence<=sequence)return;
            if(state.Sequence>m_HighestSequence)m_HighestSequence=state.Sequence;
            m_Sequences[state.Id]=state.Sequence;
            if(state.Phase==TacticalWorldPhase.Removed){Remove(state.Id);return;}
            if(!m_Visuals.TryGetValue(state.Id,out var visual))
            {
                if(m_Visuals.Count>=TacticalItemRules.MaxWorldObjects)return;
                visual=new Visual();m_Visuals[state.Id]=visual;
            }
            bool change=visual.Root==null || state.Phase!=visual.State.Phase;
            if(change)
            {
                if(visual.Root)Release(visual.Root);
                visual.Root=new GameObject("Tactical "+state.Kind+" / "+state.Phase);
                visual.Root.transform.SetParent(transform,false);Build(visual,state);
                Unity.MP_FPS.Moon.MoonSilverPrintSubject.Protect(visual.Root);
                var library = MoonkovAudio.Library;
                if (library)
                {
                    var cue = state.Phase == TacticalWorldPhase.Alarm ? library.TacticalAlarm :
                        state.Phase == TacticalWorldPhase.Explosion ? library.TacticalExplosion :
                        state.Phase == TacticalWorldPhase.Choke || state.Phase == TacticalWorldPhase.Smoke ||
                        state.Phase == TacticalWorldPhase.Poison || state.Phase == TacticalWorldPhase.Fire ? library.TacticalGas :
                        state.Phase == TacticalWorldPhase.Armed || state.Phase == TacticalWorldPhase.Supply ||
                        state.Phase == TacticalWorldPhase.Wire || state.Phase == TacticalWorldPhase.Flying ? library.TacticalDeploy : null;
                    // Use the received world position, before the visual root is positioned.
                    MoonkovAudio.Play(cue, state.Position);
                }
            }
            visual.State=state;visual.Position=state.Position;visual.Velocity=state.Velocity;
            visual.Received=Time.time;visual.Expire=Time.time+Mathf.Max(.3f,state.Remaining+.5f);
            visual.Root.transform.position=state.Position;
            // Sequence records are bounded by the world's monotonic id range. No stale resurrection.
            if(m_Sequences.Count>512)
            {m_Expired.Clear();foreach(var pair in m_Sequences)if(pair.Key+256<state.Id)m_Expired.Add(pair.Key);foreach(uint id in m_Expired)m_Sequences.Remove(id);}
        }
        public static bool TryFindInteraction(Vector3 eye,Vector3 direction,out uint targetId,out TacticalItemAction action)
        {
            targetId=0;action=default;if(!s_Active)return false;float best=TacticalItemRules.InteractionRange;
            foreach(var pair in s_Active.m_Visuals)
            {
                var v=pair.Value;bool supply=v.State.Phase==TacticalWorldPhase.Supply;
                if(!supply && v.State.Phase!=TacticalWorldPhase.Armed || supply && v.State.Energy<=0)continue;
                Vector3 pos=v.Position+Vector3.up*.15f,delta=pos-eye;float distance=delta.magnitude;
                if(distance>best || Vector3.Dot(delta.normalized,direction.normalized)<.6f ||
                    UnityEngine.Physics.Linecast(eye,pos,LayerMask.GetMask("Default","Ground"),QueryTriggerInteraction.Ignore))continue;
                best=distance;targetId=pair.Key;action=supply ? TacticalItemAction.Resupply : TacticalItemAction.Disarm;
            }
            return targetId!=0;
        }
        private void Update()
        {
            DrawAimPreview();
            m_Expired.Clear();
            foreach(var pair in m_Visuals)
            {
                var v=pair.Value;
                if(Time.time>=v.Expire){m_Expired.Add(pair.Key);continue;}
                float age=Mathf.Clamp(Time.time-v.Received,0,.3f);
                v.Root.transform.position=v.State.Phase==TacticalWorldPhase.Flying && v.Velocity.sqrMagnitude>.001f ?
                    Ballistics.Position(v.Position,v.Velocity,EnergyGrenadeRules.Gravity,age) : v.Position;
                if(v.Smoke && v.State.Phase==TacticalWorldPhase.Smoke)
                {
                    float remaining=Mathf.Max(0,v.State.Remaining-(Time.time-v.Received));
                    v.Core.localScale=SmokeVisibility.Radii(v.State.Radius,remaining)*2;
                    v.SmokeProperties.SetFloat(SmokeDensityId,SmokeVisibility.Density(remaining));
                    v.Smoke.SetPropertyBlock(v.SmokeProperties);
                }
                if(v.Core && v.State.Phase==TacticalWorldPhase.Alarm)
                    v.Core.localScale=Vector3.one*(1+.1f*Mathf.Sin(Time.time*17));
            }
            foreach(uint id in m_Expired)Remove(id);
            m_ExpiredCoughs.Clear();foreach(var pair in m_CoughEmitters)if(Time.time>pair.Value.Expires)m_ExpiredCoughs.Add(pair.Key);
            foreach(int id in m_ExpiredCoughs){Release(m_CoughEmitters[id].Root);m_CoughEmitters.Remove(id);}
        }
        public void PresentCough(int id,Vector3 position)
        {
            if(!Camera.main || Vector3.Distance(position,Camera.main.transform.position)>RaidCoughSound.Range)return;
            if(!m_CoughEmitters.TryGetValue(id,out var emitter))
            {if(m_CoughEmitters.Count>=32)return;emitter=new CoughEmitter {Root=new GameObject("Replicated choke breathing")};emitter.Root.transform.SetParent(transform,false);m_CoughEmitters[id]=emitter;}
            emitter.Expires=Time.time+3;
            if(Time.time<emitter.Next)return;emitter.Next=Time.time+(float)RaidCoughSound.Interval;emitter.Root.transform.position=position;MoonkovAudio.Play(MoonkovAudio.Library?.TacticalCough,position,.65f);
        }
        private void DrawAimPreview()
        {
            bool visible=!m_PresentationBlocked && m_Selected!=null && Camera.main && Cursor.lockState==CursorLockMode.Locked && LocalOwner.Remaining<=0;
            m_Preview.SetActive(visible);if(!visible)return;
            var camera=Camera.main;var eye=camera.transform.position;var direction=camera.transform.forward;
            if(m_Owner)
            {
                var controller=m_Owner.GetComponent<FirstPersonController>()?.CharacterController;
                eye=m_Owner.transform.position+Vector3.up*Mathf.Max(.3f,(controller ? controller.height : 1.8f)-.2f);
            }
            m_Trajectory.enabled=m_Selected.Throwable;m_PreviewRing.enabled=m_Selected.Deployable;
            int mask=LayerMask.GetMask("Default","Ground");
            if(m_Selected.Deployable)
            {
                bool valid=UnityEngine.Physics.Raycast(eye,direction,out var hit,3,mask,QueryTriggerInteraction.Ignore) && hit.normal.y>.7f;
                var point=valid ? hit.point+Vector3.up*.04f : eye+direction*2;
                // A preview is advisory. The server rechecks its capsule eye, level, support and occupancy.
                m_PreviewRing.sharedMaterial=valid ? m_Green : m_Red;
                float radius=m_Selected.Trap ? .65f : .35f;
                for(int i=0;i<48;i++){float a=i*Mathf.PI*2/48;m_PreviewRing.SetPosition(i,point+new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));}
            }
            if(m_Selected.Throwable)
            {
                // Aim line uses camera direction, while actual release always comes from the server capsule.
                Vector3 position=eye+direction*.55f,velocity=direction*14+Vector3.up*2;
                int count=1;m_Trajectory.positionCount=49;m_Trajectory.SetPosition(0,position);
                for(int i=1;i<49;i++)
                {
                    var next=Ballistics.Position(position,velocity,EnergyGrenadeRules.Gravity,.075f);
                    var delta=next-position;
                    if(UnityEngine.Physics.Raycast(position,delta.normalized,out var impact,delta.magnitude,mask,QueryTriggerInteraction.Ignore))
                    {m_Trajectory.SetPosition(i,impact.point);count=i+1;break;}
                    m_Trajectory.SetPosition(i,next);position=next;velocity+=Vector3.down*(EnergyGrenadeRules.Gravity*.075f);count=i+1;
                }
                m_Trajectory.positionCount=count;
            }
        }
        private void Build(Visual visual,in TacticalWorldStateRpc state)
        {
            var root=visual.Root.transform;var phase=state.Phase;
            var accent=state.Kind==TacticalItemKind.Fire ? m_Amber : state.Kind==TacticalItemKind.PoisonTrap ? m_Green :
                state.Kind==TacticalItemKind.Choke || state.Kind==TacticalItemKind.AmmoBox ? m_Cyan : m_Red;
            if(phase==TacticalWorldPhase.Flying)
            {
                BuildProp(root,state.Kind,accent);root.localScale=Vector3.one;return;
            }
            if(phase==TacticalWorldPhase.Armed)
            {
                BuildTrap(root,accent,true);return;
            }
            if(phase==TacticalWorldPhase.Supply)
            {
                BuildSupply(root);return;
            }
            visual.Core=new GameObject("Active effect").transform;visual.Core.SetParent(root,false);var core=visual.Core;
            if(phase==TacticalWorldPhase.Smoke)
            {
                core.localPosition=SmokeVisibility.Center(Vector3.zero);
                core.localScale=SmokeVisibility.Radii(state.Radius,state.Remaining)*2;
                Primitive(core,PrimitiveType.Cube,Vector3.one,Vector3.zero,m_SmokeVolume);
                visual.Smoke=core.GetComponentInChildren<Renderer>();
                visual.SmokeProperties=new MaterialPropertyBlock();
                visual.SmokeProperties.SetFloat(SmokeDensityId,SmokeVisibility.Density(state.Remaining));
                visual.Smoke.SetPropertyBlock(visual.SmokeProperties);return;
            }
            if(phase==TacticalWorldPhase.Wire)
            {
                var points=new Vector3[160];
                for(int i=0;i<points.Length;i++){float t=i/(float)(points.Length-1),angle=t*Mathf.PI*20;points[i]=new Vector3(t*1.8f-.9f,.32f+Mathf.Sin(angle)*.28f,Mathf.Cos(angle)*.28f);}
                Line(core,points,.018f,m_Metal,false);
                var barbs=new List<Vector3>();for(int i=3;i<points.Length-3;i+=5){var point=points[i];barbs.Add(point);barbs.Add(point+new Vector3(.07f,.07f,.04f));barbs.Add(point);barbs.Add(point+new Vector3(-.07f,-.07f,-.04f));barbs.Add(point);}
                Line(core,barbs.ToArray(),.013f,m_Metal,false);
                for(int i=0;i<2;i++)Primitive(core,PrimitiveType.Cylinder,new Vector3(.045f,.3f,.045f),new Vector3(i==0?-.9f:.9f,.27f,0),m_Metal);
                return;
            }
            if(phase==TacticalWorldPhase.Fire)
            {
                ResourceVolume(core,state.Radius,m_FirePrefab,m_Flame,true);return;
            }
            if(phase==TacticalWorldPhase.Choke || phase==TacticalWorldPhase.Poison)
            {
                var material=phase==TacticalWorldPhase.Choke ? m_Smoke : m_Poison;
                ResourceVolume(core,state.Radius,phase==TacticalWorldPhase.Choke ? m_ChokePrefab : m_PoisonPrefab,material,false);return;
            }
            Ring(core,phase==TacticalWorldPhase.Alarm ? 1.3f : state.Radius,.045f,m_Amber);
            Primitive(core,PrimitiveType.Sphere,Vector3.one*.3f,Vector3.up*.25f,m_Amber);
        }
        private static void ResourceVolume(Transform root,float radius,GameObject prefab,Material fallback,bool flame)
        {
            if(!prefab){Volume(root,radius,fallback,flame);return;}
            var effect=Instantiate(prefab,root,false);effect.transform.localPosition=Vector3.zero;
            int layer=0;
            foreach(var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=particles.main;main.maxParticles=Mathf.Min(64,main.maxParticles);main.loop=true;
                if(particles.gameObject!=effect)particles.transform.localPosition=Vector3.up*(layer++*.12f);
                var shape=particles.shape;shape.radius=Mathf.Max(.1f,radius*.75f);
                var collision=particles.collision;collision.enabled=false;var triggers=particles.trigger;triggers.enabled=false;
                particles.Play();
            }
        }
        private static void Volume(Transform root,float radius,Material material,bool flame)
        {
            var particles=new GameObject(flame ? "Rising original flames" : "Low atmospheric mist").AddComponent<ParticleSystem>();particles.transform.SetParent(root,false);
            particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=particles.main;main.maxParticles=flame ? 96 : 64;main.startLifetime=new ParticleSystem.MinMaxCurve(flame ? .55f : 1.8f,flame ? 1.1f : 2.8f);
            main.startSpeed=0;main.startSize=new ParticleSystem.MinMaxCurve(flame ? .45f : .9f,flame ? .85f : 1.7f);
            main.startColor=Color.white;main.simulationSpace=ParticleSystemSimulationSpace.Local;
            var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Circle;shape.rotation=new Vector3(90,0,0);shape.radius=radius*.85f;
            var velocity=particles.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.Local;velocity.x=new ParticleSystem.MinMaxCurve(0);velocity.y=new ParticleSystem.MinMaxCurve(flame ? .85f : .16f);velocity.z=new ParticleSystem.MinMaxCurve(0);
            var emission=particles.emission;emission.rateOverTime=flame ? 70 : 24;
            var fade=particles.colorOverLifetime;fade.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.15f),new GradientAlphaKey(.75f,.6f),new GradientAlphaKey(0,1)});fade.color=gradient;
            var renderer=particles.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=flame ? ParticleSystemRenderMode.VerticalBillboard : ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;particles.Play();
        }
        private void LateUpdate() => DrawHeld();
        private void DrawHeld()
        {
            bool visible=!m_PresentationBlocked && (m_Selected!=null || m_SelectedCode=="energy_grenade" || Unity.MP_FPS.Inventory.ConsumableCatalog.MedicalRestore(m_SelectedCode)>0) && Camera.main && Cursor.lockState==CursorLockMode.Locked;
            if(!visible){if(m_Held)m_Held.SetActive(false);return;}
            if(!m_Held)
            {
                m_Held=new GameObject("Held "+m_SelectedCode);m_Held.transform.SetParent(transform,false);
                // Medical supplies use the same authored models as loot and inventory.
                // Selecting only draws the model; LMB still sends the authoritative use request.
                if(!PhysicalItemVisuals.Create(m_SelectedCode,m_Held.transform,true))
                {
                    if(m_Selected==null && m_SelectedCode!="energy_grenade")
                    { Release(m_Held);m_Held=null;return; }
                    var kind=m_Selected!=null ? m_Selected.Kind : TacticalItemKind.Frag;
                    BuildProp(m_Held.transform,kind,kind==TacticalItemKind.Stamina ? m_Amber : kind==TacticalItemKind.Antidote ? m_Green : kind==TacticalItemKind.Choke ? m_Cyan : m_Red);
                }
                Unity.MP_FPS.Moon.MoonSilverPrintSubject.Protect(m_Held);
            }
            m_Held.SetActive(true);
            if((!m_Owner || !m_RightHand) && Time.time>=m_NextOwnerSearch)
            {
                m_NextOwnerSearch=Time.time+1;
                foreach(var candidate in UnityEngine.Object.FindObjectsByType<DollSingerNetworkPresentation>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
                    if(candidate.OwnedInput){m_Owner=candidate;var animator=candidate.GetComponentInChildren<Animator>();if(animator && animator.isHuman)m_RightHand=animator.GetBoneTransform(HumanBodyBones.RightHand);break;}
            }
            bool third=m_Owner && m_Owner.IsThirdPerson && m_RightHand;
            if(third)
            {m_Held.transform.position=m_RightHand.TransformPoint(new Vector3(.025f,.015f,.035f));m_Held.transform.rotation=m_RightHand.rotation*Quaternion.Euler(0,0,-90);m_Held.transform.localScale=Vector3.one*.65f;}
            else
            {
                float progress=LocalOwner.Remaining>0 ? 1-Mathf.Clamp01(LocalOwner.Remaining/Mathf.Max(1,m_Selected?.UseSeconds ?? 3)) : 0;
                float action=Mathf.Sin(progress*Mathf.PI);var camera=Camera.main;
                Vector3 offset=new Vector3(.18f,-.18f,.44f)+new Vector3(-.08f,.1f,-.035f)*action;
                m_Held.transform.position=camera.transform.TransformPoint(offset);m_Held.transform.rotation=camera.transform.rotation*Quaternion.Euler(8+action*25,-12,-18+action*15);
                m_Held.transform.localScale=Vector3.one*(m_Selected?.Kind==TacticalItemKind.AmmoBox ? .48f : .8f);
            }
        }
        private void BuildProp(Transform root,TacticalItemKind kind,Material accent)
        {
            foreach(var definition in TacticalItemRules.Definitions)
                if(definition.Kind==kind && PhysicalItemVisuals.Create(definition.Code,root,true))return;
            if(kind==TacticalItemKind.AmmoBox){BuildSupply(root);return;}
            if(kind>=TacticalItemKind.AlertTrap && kind<=TacticalItemKind.PoisonTrap){BuildTrap(root,accent,false);return;}
            if(kind<=TacticalItemKind.Antidote)
            {
                Primitive(root,PrimitiveType.Cylinder,new Vector3(.03f,.065f,.03f),Vector3.zero,m_White);
                Primitive(root,PrimitiveType.Cylinder,new Vector3(.032f,.018f,.032f),Vector3.up*.025f,accent);
                Primitive(root,PrimitiveType.Cylinder,new Vector3(.012f,.025f,.012f),Vector3.up*.09f,m_Metal);
                Primitive(root,PrimitiveType.Cube,new Vector3(.06f,.009f,.02f),Vector3.up*.115f,m_Dark);
                Primitive(root,PrimitiveType.Cylinder,new Vector3(.003f,.028f,.003f),Vector3.down*.093f,m_Metal);return;
            }
            if(kind==TacticalItemKind.Dynamite)
            {
                for(int i=0;i<3;i++){float angle=i*Mathf.PI*2/3;Primitive(root,PrimitiveType.Cylinder,new Vector3(.042f,.095f,.042f),new Vector3(Mathf.Cos(angle)*.027f,0,Mathf.Sin(angle)*.027f),m_Red);}
                Primitive(root,PrimitiveType.Cube,new Vector3(.105f,.018f,.095f),Vector3.up*.05f,m_Dark);
                Primitive(root,PrimitiveType.Cube,new Vector3(.105f,.018f,.095f),Vector3.down*.05f,m_Dark);
                Line(root,new[]{new Vector3(0,.1f,0),new Vector3(.015f,.15f,.015f),new Vector3(.045f,.17f,.02f)},.006f,m_Amber,false);return;
            }
            if(kind==TacticalItemKind.Smoke)
            {
                Primitive(root,PrimitiveType.Cylinder,new Vector3(.11f,.09f,.11f),Vector3.zero,m_Metal);
                Primitive(root,PrimitiveType.Cylinder,new Vector3(.114f,.025f,.114f),Vector3.zero,m_White);
                Primitive(root,PrimitiveType.Cylinder,new Vector3(.065f,.012f,.065f),Vector3.up*.1f,m_Dark);
                Line(root,new[]{new Vector3(0,.12f,0),new Vector3(.065f,.12f,0),new Vector3(.065f,-.03f,0)},.012f,m_Metal,false);return;
            }
            if(kind==TacticalItemKind.Fire)
            {
                Primitive(root,PrimitiveType.Cylinder,new Vector3(.07f,.075f,.07f),Vector3.zero,m_Dark);
                Primitive(root,PrimitiveType.Sphere,new Vector3(.07f,.055f,.07f),Vector3.up*.075f,m_Metal);
                Primitive(root,PrimitiveType.Cylinder,new Vector3(.028f,.034f,.028f),Vector3.up*.11f,m_Metal);
                Primitive(root,PrimitiveType.Cube,new Vector3(.02f,.055f,.014f),Vector3.up*.16f,m_White);
                Primitive(root,PrimitiveType.Cylinder,new Vector3(.073f,.018f,.073f),Vector3.zero,m_Amber);return;
            }
            Primitive(root,PrimitiveType.Sphere,new Vector3(.11f,.14f,.11f),Vector3.zero,m_Dark);
            Primitive(root,PrimitiveType.Cylinder,new Vector3(.045f,.025f,.045f),Vector3.up*.08f,m_Metal);
            for(int i=0;i<8;i++){float angle=i*Mathf.PI/4;Primitive(root,PrimitiveType.Cube,new Vector3(.018f,.105f,.018f),new Vector3(Mathf.Cos(angle)*.055f,0,Mathf.Sin(angle)*.055f),kind==TacticalItemKind.Choke ? m_Cyan : m_Metal);}
            Line(root,new[]{new Vector3(0,.11f,0),new Vector3(.04f,.11f,0),new Vector3(.045f,-.02f,0)},.014f,m_Metal,false);
        }
        private void BuildTrap(Transform root,Material accent,bool wire)
        {
            Primitive(root,PrimitiveType.Cube,new Vector3(.29f,.08f,.19f),Vector3.up*.04f,m_Dark);
            Primitive(root,PrimitiveType.Cube,new Vector3(.24f,.015f,.145f),Vector3.up*.09f,m_Metal);
            Primitive(root,PrimitiveType.Cylinder,new Vector3(.048f,.012f,.048f),new Vector3(-.07f,.105f,0),m_Dark);
            Primitive(root,PrimitiveType.Cylinder,new Vector3(.031f,.004f,.031f),new Vector3(-.07f,.12f,0),accent);
            for(int i=0;i<2;i++)Primitive(root,PrimitiveType.Cube,new Vector3(.025f,.025f,.04f),new Vector3(.05f+i*.05f,.11f,0),m_Metal);
            if(!wire)return;
            for(int i=0;i<2;i++)Primitive(root,PrimitiveType.Cylinder,new Vector3(.035f,.12f,.035f),new Vector3(i==0?-.6f:.6f,.1f,0),m_Metal);
            Line(root,new[]{new Vector3(-.6f,.14f,0),new Vector3(.6f,.14f,0)},.008f,m_Metal,false);
        }
        private void BuildSupply(Transform root)
        {
            if(PhysicalItemVisuals.Create("ammo_box",root))return;
            Primitive(root,PrimitiveType.Cube,new Vector3(.58f,.31f,.36f),Vector3.up*.155f,m_Dark);
            Primitive(root,PrimitiveType.Cube,new Vector3(.6f,.045f,.38f),Vector3.up*.335f,m_Metal);
            Primitive(root,PrimitiveType.Cube,new Vector3(.36f,.018f,.3f),new Vector3(0,.365f,.01f),m_Dark);
            for(int i=0;i<4;i++)
            {var point=new Vector3((i-1.5f)*.1f,.255f,-.191f);Primitive(root,PrimitiveType.Cylinder,new Vector3(.058f,.085f,.058f),point,m_Cyan);Primitive(root,PrimitiveType.Cylinder,new Vector3(.062f,.012f,.062f),point+Vector3.up*.085f,m_Metal);}
            for(int i=0;i<2;i++){Primitive(root,PrimitiveType.Cube,new Vector3(.035f,.075f,.04f),new Vector3(i==0?-.11f:.11f,.4f,0),m_Metal);Primitive(root,PrimitiveType.Cube,new Vector3(.04f,.07f,.025f),new Vector3(i==0?-.21f:.21f,.23f,-.2f),m_Metal);}
            Primitive(root,PrimitiveType.Cube,new Vector3(.25f,.025f,.04f),Vector3.up*.445f,m_Metal);
            Line(root,new[]{new Vector3(-.03f,.24f,-.22f),new Vector3(.02f,.24f,-.22f),new Vector3(-.02f,.18f,-.22f),new Vector3(.03f,.18f,-.22f)},.012f,m_Cyan,false);
        }
        private void Primitive(Transform parent,PrimitiveType type,Vector3 scale,Vector3 position,Material material)
        {
            var part=GameObject.CreatePrimitive(type);part.transform.SetParent(parent,false);part.transform.localPosition=position;part.transform.localScale=scale;
            var collider=part.GetComponent<Collider>();collider.enabled=false;Release(collider);
            var renderer=part.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
        }
        private static void Line(Transform root,Vector3[] points,float width,Material material,bool loop)
        {
            var line=new GameObject("Tactical line").AddComponent<LineRenderer>();line.transform.SetParent(root,false);line.useWorldSpace=false;
            line.positionCount=points.Length;line.SetPositions(points);line.loop=loop;line.widthMultiplier=width;line.sharedMaterial=material;line.shadowCastingMode=ShadowCastingMode.Off;
        }
        private static void Ring(Transform root,float radius,float width,Material material,float height=.04f)
        {var points=new Vector3[48];for(int i=0;i<48;i++){float a=i*Mathf.PI*2/48;points[i]=new Vector3(Mathf.Cos(a)*radius,height,Mathf.Sin(a)*radius);}Line(root,points,width,material,true);}
        private void Remove(uint id)
        {if(m_Visuals.TryGetValue(id,out var v)){if(v.Root)Release(v.Root);m_Visuals.Remove(id);}}
        private void Clear(){foreach(var v in m_Visuals.Values)if(v.Root)Release(v.Root);m_Visuals.Clear();m_Sequences.Clear();m_HighestSequence=0;foreach(var emitter in m_CoughEmitters.Values)if(emitter.Root)Release(emitter.Root);m_CoughEmitters.Clear();}
        private static void Release(UnityEngine.Object value)
        {if(!value)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
        private void OnDestroy()
        {if(m_Destroyed)return;m_Destroyed=true;m_TransparentRenderer?.Dispose();m_TransparentRenderer=null;Clear();foreach(var material in m_Materials)if(material)Release(material);if(s_Active==this)s_Active=null;}
    }
}
