using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS
{
    // Cosmetic client copy. Inventory and access remain owned by the server's corpse ID.
    public sealed class CorpseVisual : MonoBehaviour
    {
        private PlayableGraph m_Graph;
        private AnimationClipPlayable m_Clip;
        private Animator m_Animator;
        private SecondaryBoneSpring m_HairSpring;
        private bool m_BodySettled;
        private float m_LastAge;
        private Transform m_Model;
        private Transform[] m_Contacts;
        private Bounds m_LootBounds;
        private float m_GroundLift;
        private PhysicsScene m_PhysicsScene;
        private static readonly int s_GroundMask=LayerMask.GetMask("Default","Ground");
        private double m_Start;
        private float m_InitialAge,m_End;
        private const int MaxSpringStepsPerSample = 4;
        private static readonly Unity.Profiling.ProfilerMarker s_SpawnProfile = new("Moonkov.Corpse.Spawn");
        private static readonly Unity.Profiling.ProfilerMarker s_SampleProfile = new("Moonkov.Corpse.SampleAge");
        private static readonly Unity.Profiling.ProfilerMarker s_GroundProfile = new("Moonkov.Corpse.GroundFit");
        public bool Settled {get;private set;}
        public float FinishAge => m_End+(m_HairSpring!=null ? 3f : 0f);
        public Vector3 LootAimPoint(Camera camera)
        {
            if (!camera || m_Contacts == null || m_Contacts.Length == 0)
                return transform.position + Vector3.up * .25f;
            var ray = new Ray(camera.transform.position, camera.transform.forward);
            if (m_LootBounds.IntersectRay(ray, out float distance)) return ray.GetPoint(distance);
            float depth = Mathf.Max(0, Vector3.Dot(m_LootBounds.center - ray.origin, ray.direction));
            return m_LootBounds.ClosestPoint(ray.GetPoint(depth));
        }
        public static CorpseVisual Spawn(Transform parent,CorpsePresentationSettings settings,RaidCorpseRpc corpse)
        {
            using var profile=s_SpawnProfile.Auto();
            if(settings==null || settings.Death==null || settings.CharacterVisuals==null || corpse.CharacterIndex<0 ||
                corpse.CharacterIndex>=settings.CharacterVisuals.Length || settings.CharacterVisuals[corpse.CharacterIndex]==null)
                throw new InvalidOperationException("Corpse presentation assets are missing. Run Tools/Moonkov/Build Corpse Presentation.");
            var root=new GameObject("Corpse / "+corpse.LootId);root.SetActive(false);
            root.transform.SetParent(parent,false);root.transform.SetPositionAndRotation(corpse.Position,corpse.Rotation);
            var view=root.AddComponent<CorpseVisual>();
            view.m_PhysicsScene=root.scene.GetPhysicsScene();
            try
            {
                var model=Instantiate(settings.CharacterVisuals[corpse.CharacterIndex],root.transform,false);
                view.m_Model=model.transform;
                view.m_Animator=model.GetComponentInChildren<Animator>(true);
                if(view.m_Animator==null || !view.m_Animator.isHuman)throw new InvalidOperationException("Corpse needs a Humanoid animator.");
                view.m_Animator.runtimeAnimatorController=null;view.m_Animator.applyRootMotion=false;
                view.m_Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.forceRenderingOff=false;
                    var bounds=renderer.localBounds;bounds.Expand(bounds.size.magnitude*2);renderer.localBounds=bounds;
                }
                view.m_End=Mathf.Max(0,settings.Death.length-.0001f);
                view.m_InitialAge=corpse.Age;view.m_Start=Time.timeAsDouble;
                root.SetActive(true);view.m_Animator.Rebind();
                view.m_HairSpring=model.GetComponentInChildren<SecondaryBoneSpring>(true);
                if(view.m_HairSpring!=null)view.m_HairSpring.InitializeCorpsePresentation();
                var contacts=new System.Collections.Generic.List<Transform>();
                foreach(var bone in new[] {HumanBodyBones.Hips,HumanBodyBones.Chest,HumanBodyBones.Head,
                    HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,
                    HumanBodyBones.LeftHand,HumanBodyBones.RightHand})
                {
                    var contact=view.m_Animator.GetBoneTransform(bone);if(contact!=null)contacts.Add(contact);
                }
                view.m_Contacts=contacts.ToArray();
                view.m_Graph=PlayableGraph.Create("Moonkov corpse "+corpse.LootId);
                view.m_Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                view.m_Clip=AnimationClipPlayable.Create(view.m_Graph,settings.Death);
                view.m_Clip.SetSpeed(0);view.m_Clip.SetApplyFootIK(false);view.m_Clip.SetApplyPlayableIK(false);
                AnimationPlayableOutput.Create(view.m_Graph,"Death",view.m_Animator).SetSourcePlayable(view.m_Clip);
                view.m_Graph.Play();view.SampleAge(corpse.Age);
                if (corpse.Age < .35f) MoonkovAudio.Play(MoonkovAudio.Library?.Death, corpse.Position);
                return view;
            }
            catch {if(Application.isPlaying)Destroy(root);else DestroyImmediate(root);throw;}
        }
        private void Update() => SampleAge(m_InitialAge+(float)(Time.timeAsDouble-m_Start));
        public void SampleAge(float age)
        {
            if(Settled)return;
            using var profile=s_SampleProfile.Auto();
            if(!m_BodySettled)
            {
                m_Model.position-=Vector3.up*m_GroundLift;m_GroundLift=0;
                m_Clip.SetTime(Mathf.Clamp(age,0,m_End));m_Graph.Evaluate(0);
                foreach(var contact in m_Contacts)IncludeGroundContact(contact.position-Vector3.up*.08f);
                if(age>=m_End)FitFinalMeshToGround();
                m_Model.position+=Vector3.up*m_GroundLift;
                UpdateLootBounds();
                if(age>=m_End){m_Animator.enabled=false;m_Graph.Destroy();m_BodySettled=true;}
            }
            float targetAge=Mathf.Clamp(age,0,FinishAge);
            if(m_HairSpring!=null)
            {
                // A late corpse can have several seconds of settling to reconstruct.
                // Spread that cosmetic work across frames instead of blocking a game frame.
                float remaining=Mathf.Max(0,targetAge-m_LastAge);
                int steps=0;
                while(remaining>.00001f && steps++<MaxSpringStepsPerSample)
                {
                    float step=Mathf.Min(1f/60f,remaining);m_HairSpring.SimulatePresentation(step);remaining-=step;
                }
                m_LastAge=targetAge-remaining;
            }
            else m_LastAge=targetAge;
            if(age<FinishAge || m_LastAge+.00001f<targetAge)return;
            if(m_HairSpring!=null)
            {
                m_Model.position-=Vector3.up*m_GroundLift;m_GroundLift=0;
                FitFinalMeshToGround();m_Model.position+=Vector3.up*m_GroundLift;
                UpdateLootBounds();
            }
            // Body stops at the death clip's end; hair gets three more seconds, then freezes too.
            Settled=true;enabled=false;
        }
        private void UpdateLootBounds()
        {
            if (m_Contacts == null || m_Contacts.Length == 0) return;
            // Culling bounds are deliberately enlarged; only the posed body defines its loot target.
            m_LootBounds = new Bounds(m_Contacts[0].position, Vector3.zero);
            for (int i = 1; i < m_Contacts.Length; i++) m_LootBounds.Encapsulate(m_Contacts[i].position);
            m_LootBounds.Expand(.2f);
        }
        private void IncludeGroundContact(Vector3 point)
        {
            if(m_PhysicsScene.Raycast(point+Vector3.up*2,Vector3.down,out var hit,5,s_GroundMask,QueryTriggerInteraction.Ignore))
                m_GroundLift=Mathf.Max(m_GroundLift,hit.point.y+.012f-point.y);
        }
        private void FitFinalMeshToGround()
        {
            using var profile=s_GroundProfile.Auto();
            // Bake once at the final pose. Renderer bounds include unused animation space,
            // while bone centres do not account for the thickness of the visible body.
            var mesh=new Mesh();var vertices=new System.Collections.Generic.List<Vector3>();
            try
            {
                foreach(var renderer in m_Model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if(!renderer.enabled || !renderer.gameObject.activeInHierarchy)continue;
                    renderer.BakeMesh(mesh,true);mesh.GetVertices(vertices);
                    var bounds=mesh.bounds;var bottom=new Vector3[64];var occupied=new bool[64];
                    var localToWorld=renderer.transform.localToWorldMatrix;
                    foreach(var vertex in vertices)
                    {
                        int x=Mathf.Clamp((int)((vertex.x-bounds.min.x)/Mathf.Max(.0001f,bounds.size.x)*8),0,7);
                        int z=Mathf.Clamp((int)((vertex.z-bounds.min.z)/Mathf.Max(.0001f,bounds.size.z)*8),0,7);
                        int cell=z*8+x;
                        float worldY=localToWorld.m10*vertex.x+localToWorld.m11*vertex.y+localToWorld.m12*vertex.z+localToWorld.m13;
                        if(!occupied[cell] || worldY<bottom[cell].y)
                        {occupied[cell]=true;bottom[cell]=localToWorld.MultiplyPoint3x4(vertex);}
                    }
                    for(int i=0;i<64;i++)if(occupied[i])IncludeGroundContact(bottom[i]);
                }
            }
            finally {if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}
        }
        private void OnDestroy(){if(m_Graph.IsValid())m_Graph.Destroy();}
    }
}
