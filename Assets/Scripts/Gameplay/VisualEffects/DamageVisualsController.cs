using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS
{
    // Owning-camera presentation; independent of animation update and prediction replay.
    public class DamageVisualsController : MonoBehaviour
    {
        [Header("References"),Tooltip("Legacy damage material, retained as a fallback.")]
        [SerializeField] private Material screenDamageMaterial;
        [Header("Intensity Range")]
        [SerializeField] private float minIntensity=.4f;
        [SerializeField] private float maxIntensity=.8f;
        [SerializeField] private float fadeSpeed=2f;
        [Range(0,1),SerializeField] private float effectStrength=1;

        private readonly InjuryFeedbackState m_Injury=new InjuryFeedbackState();
        private PlayerGhost m_Player;
        private Material m_InjuryMaterial,m_MucusMaterial;
        private FullScreenPassWrapper m_InjuryPass,m_MucusPass;
        private bool m_VeinsRetained;
        private float m_MucusRemaining,m_MucusElapsed;
        private Material m_GlareMaterial;
        private FullScreenPassWrapper m_GlarePass;
        private float m_GlareStrength;
        private Vector2 m_GlareViewport=new Vector2(.5f,.5f);
        private static readonly int Glare=Shader.PropertyToID("_Glare");
        private static readonly int Vitals=Shader.PropertyToID("_VitalState"),Wounds=Shader.PropertyToID("_WoundState"),
            FeedbackTime=Shader.PropertyToID("_FeedbackTime"),Veins=Shader.PropertyToID("_VeinMask"),Intensity=Shader.PropertyToID("_Intensity");

        private void Awake()=>m_Player=GetComponentInParent<PlayerGhost>();
        private void OnEnable()
        {
            m_Player=GetComponentInParent<PlayerGhost>();m_Injury.Reset();
            RenderPipelineManager.beginCameraRendering-=InjectRenderPass;
            RenderPipelineManager.beginCameraRendering+=InjectRenderPass;
        }
        private void Update()
        {
            var ghost=m_Player?m_Player.GhostGameObject:null;
            if(!ghost || ghost.Role!=MultiplayerRole.ClientOwned || !ghost.GhostEntityExists() || !ghost.GhostHasComponent<PredictedPlayerGhost>())
            {m_Injury.Reset();m_MucusRemaining=m_GlareStrength=0;return;}
            var state=ghost.ReadGhostComponentData<PredictedPlayerGhost>();
            m_Injury.Present(state,Time.unscaledDeltaTime,fadeSpeed);
            if(state.CurrentHealth>0 || m_Injury.Active)EnsureInjuryMaterial();
            UpdateInjuryMaterial();UpdateMucus(state);UpdateGlare(state);
        }
        private void EnsureInjuryMaterial()
        {
            if(m_InjuryMaterial)return;
            var shader=Resources.Load<Shader>("Moonkov/InjurySurvivalScreen");
            if(shader)
            {
                m_InjuryMaterial=new Material(shader){name="Local camera / injury and survival",hideFlags=HideFlags.HideAndDontSave};
                m_InjuryMaterial.SetTexture(Veins,InjuryVeinMask.Retain());m_VeinsRetained=true;
            }
            else if(screenDamageMaterial)
                m_InjuryMaterial=new Material(screenDamageMaterial){hideFlags=HideFlags.HideAndDontSave};
            if(m_InjuryMaterial)m_InjuryPass=new FullScreenPassWrapper("_LocalInjurySurvival",m_InjuryMaterial,0,shader!=null,false,(RenderPassEvent)602);
        }
        private void UpdateInjuryMaterial()
        {
            if(!m_InjuryMaterial)return;
            m_InjuryMaterial.SetVector(Vitals,new Vector4(m_Injury.Critical,m_Injury.Hunger,m_Injury.Thirst,m_Injury.Impact));
            m_InjuryMaterial.SetVector(Wounds,new Vector4(m_Injury.Blood,m_Injury.Variation,m_Injury.Visibility,effectStrength));
            m_InjuryMaterial.SetFloat(FeedbackTime,m_Injury.Time);
            m_InjuryMaterial.SetFloat(Intensity,m_Injury.Impact*effectStrength);
        }
        private void UpdateMucus(in PredictedPlayerGhost state)
        {
            float remaining=state.CurrentHealth>0?state.MucusBlindSeconds:0;
            if(remaining>m_MucusRemaining+.15f)m_MucusElapsed=0;
            m_MucusRemaining=remaining;if(remaining<=0)return;
            if(!m_MucusMaterial)
            {
                var shader=Resources.Load<Shader>("Moonkov/AlienMucusScreen");if(!shader)return;
                m_MucusMaterial=new Material(shader){name="Local camera / alien mucus",hideFlags=HideFlags.HideAndDontSave};
                m_MucusPass=new FullScreenPassWrapper("_AlienMucusPass",m_MucusMaterial,0,false,false,(RenderPassEvent)602);
            }
            m_MucusElapsed+=Time.unscaledDeltaTime;
            m_MucusMaterial.SetFloat("_Remaining",remaining);m_MucusMaterial.SetFloat("_Elapsed",m_MucusElapsed);
        }
        private void InjectRenderPass(ScriptableRenderContext context,Camera camera)
        {
            if(!isActiveAndEnabled || !m_Player)return;
            var ghost=m_Player.GhostGameObject;
            if(!ghost || ghost.Role!=MultiplayerRole.ClientOwned || camera!=m_Player.GetPlayerCamera())return;
            if(camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) && data.renderType==CameraRenderType.Overlay)return;
            if(effectStrength>0 && m_Injury.Active)m_InjuryPass?.EnqueuePass(camera);
            if(m_MucusRemaining>0)m_MucusPass?.EnqueuePass(camera);
            if(m_GlareStrength>.001f)m_GlarePass?.EnqueuePass(camera);
        }

        private void UpdateGlare(in PredictedPlayerGhost state)
        {
            var camera=m_Player.GetPlayerCamera();
            if(!camera){m_GlareStrength=0;return;}
            Vector3 viewport=camera.WorldToViewportPoint(state.HaloGlareSource);
            float target=state.CurrentHealth>0 && viewport.z>0 ? Mathf.Clamp01(state.HaloLightGlare)*effectStrength : 0;
            if(target>0)m_GlareViewport=new Vector2(Mathf.Clamp01(viewport.x),Mathf.Clamp01(viewport.y));
            m_GlareStrength=Mathf.MoveTowards(m_GlareStrength,target,Time.unscaledDeltaTime*(target>m_GlareStrength?4:2));
            if(m_GlareStrength<=.001f)return;
            if(!m_GlareMaterial)
            {
                var shader=Resources.Load<Shader>("Moonkov/HaloLightGlareScreen");if(!shader)return;
                m_GlareMaterial=new Material(shader){name="Local camera / aimed light glare",hideFlags=HideFlags.HideAndDontSave};
                m_GlarePass=new FullScreenPassWrapper("_HaloLightGlare",m_GlareMaterial,0,false,false,(RenderPassEvent)603);
            }
            m_GlareMaterial.SetVector(Glare,new Vector4(m_GlareViewport.x,m_GlareViewport.y,camera.aspect,m_GlareStrength));
        }

        // Preserve the existing trigger API for explicit presentation callers.
        public void TriggerDamageEffect()=>TriggerDamageEffect(minIntensity);
        public void TriggerDamageEffect(float intensity)
        {
            if(intensity<=0)return;
            m_Injury.Trigger(Mathf.Lerp(minIntensity,maxIntensity,Mathf.Clamp01(intensity)));UpdateInjuryMaterial();
        }
        public void TriggerDamageEffect(float damageAmount,float maxHealth)
        {if(damageAmount>0 && maxHealth>0)TriggerDamageEffect(damageAmount/maxHealth);}

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering-=InjectRenderPass;
            m_Injury.Reset();m_MucusRemaining=m_MucusElapsed=0;
            m_GlareStrength=0;DestroyPrivate(m_GlareMaterial);m_GlareMaterial=null;m_GlarePass=null;
            DestroyPrivate(m_InjuryMaterial);DestroyPrivate(m_MucusMaterial);
            m_InjuryMaterial=m_MucusMaterial=null;m_InjuryPass=m_MucusPass=null;
            if(m_VeinsRetained){InjuryVeinMask.Release();m_VeinsRetained=false;}
        }
        private void OnDestroy()=>OnDisable();
        private static void DestroyPrivate(Object item)
        {if(!item)return;if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);}
    }
}
