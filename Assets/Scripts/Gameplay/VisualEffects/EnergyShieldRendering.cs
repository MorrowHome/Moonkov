using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS
{
    // One client rendering hook, no additional camera/World and no renderer-asset edits.
    internal static class EnergyShieldRendering
    {
        private static readonly List<EnergyShieldPresentation> s_Views=new List<EnergyShieldPresentation>(16);
        private static readonly Plane[] s_Planes=new Plane[6];
        private static ProtectionPass s_Pass;
        private static bool s_Subscribed;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            foreach(var view in s_Views)if(view)view.RestoreCameraVisibility();
            s_Views.Clear();Unsubscribe();s_Pass=null;
        }
        internal static void Register(EnergyShieldPresentation view)
        {
            if(!view || !view.isActiveAndEnabled || s_Views.Contains(view))return;
            s_Views.Add(view);
            if(s_Subscribed)return;
            s_Pass=new ProtectionPass();s_Subscribed=true;
            RenderPipelineManager.beginCameraRendering+=BeginCamera;RenderPipelineManager.endCameraRendering+=EndCamera;
        }
        internal static void Unregister(EnergyShieldPresentation view)
        {s_Views.Remove(view);if(s_Views.Count==0){Unsubscribe();s_Pass=null;}}
        private static void Unsubscribe()
        {
            if(!s_Subscribed)return;s_Subscribed=false;
            RenderPipelineManager.beginCameraRendering-=BeginCamera;RenderPipelineManager.endCameraRendering-=EndCamera;
        }
        private static void BeginCamera(ScriptableRenderContext context,Camera camera)
        {
            for(int i=s_Views.Count-1;i>=0;i--)if(!s_Views[i] || !s_Views[i].isActiveAndEnabled)s_Views.RemoveAt(i);
            if(s_Views.Count==0){Unsubscribe();s_Pass=null;return;}
            // Visibility is paired for every camera, including a first-person overlay.
            foreach(var view in s_Views)view.BeginCamera(camera);
            if(!camera || SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null ||
                camera.cameraType!=CameraType.Game && camera.cameraType!=CameraType.SceneView)return;
            if(!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline))return;
            camera.TryGetComponent<UniversalAdditionalCameraData>(out var additional);
            if(additional && additional.renderType==CameraRenderType.Overlay)return;
            GeometryUtility.CalculateFrustumPlanes(camera,s_Planes);
            bool surfaces=false,visor=false;float pulse=0;EnergyShieldPresentation owner=null;
            foreach(var view in s_Views)
            {
                surfaces|=view.InView(camera,s_Planes);
                float value=view.OwnerPulse(camera);
                if(view.OwnerVisor(camera)){visor=true;owner=view;}
                if(value>pulse){pulse=value;owner=view;}
            }
            if(!surfaces && pulse<=0 && !visor)return;
            s_Pass.HasSurfaces=surfaces;s_Pass.Visor=visor;s_Pass.Pulse=pulse;s_Pass.PulseOwner=owner;s_Pass.Camera=camera;
            var renderer=additional?additional.scriptableRenderer:pipeline.scriptableRenderer;
            renderer?.EnqueuePass(s_Pass);
        }
        private static void EndCamera(ScriptableRenderContext context,Camera camera)
        {foreach(var view in s_Views)if(view)view.EndCamera(camera);}

        private sealed class ProtectionPass : ScriptableRenderPass
        {
            private static readonly List<ShaderTagId> Tags=new List<ShaderTagId>{new ShaderTagId("MoonkovEnergyShield")};
            private static readonly int SceneColor=Shader.PropertyToID("_EnergySceneColor"),SceneDepth=Shader.PropertyToID("_EnergySceneDepth");
            private static readonly int ColorScale=Shader.PropertyToID("_EnergyColorScaleBias"),DepthScale=Shader.PropertyToID("_EnergyDepthScaleBias");
            private readonly MaterialPropertyBlock pulseProperties=new MaterialPropertyBlock();
            internal bool HasSurfaces,Visor;internal float Pulse;internal Camera Camera;internal EnergyShieldPresentation PulseOwner;
            internal ProtectionPass()
            {renderPassEvent=(RenderPassEvent)601;requiresIntermediateTexture=true;ConfigureInput(ScriptableRenderPassInput.Depth);}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
            {
                var resources=frameData.Get<UniversalResourceData>();var camera=frameData.Get<UniversalCameraData>();
                if(camera.camera!=Camera || resources.isActiveTargetBackBuffer)return;
                bool surfaces=HasSurfaces && resources.cameraDepthTexture.IsValid();
                bool pulse=Pulse>0 && PulseOwner && PulseOwner.FieldMaterial;
                bool visor=Visor && PulseOwner && PulseOwner.OwnerVisor(camera.camera) && PulseOwner.FieldMaterial;
                if(!surfaces && !pulse && !visor)return;
                var target=resources.activeColorTexture;var snapshot=TextureHandle.nullHandle;
                if(surfaces || visor)
                {
                    // Refraction samples a separate graph-owned copy, never its write attachment.
                    var descriptor=graph.GetTextureDesc(target);descriptor.name="Energy seal / refraction scene";descriptor.clearBuffer=false;
                    snapshot=graph.CreateTexture(descriptor);
                    graph.AddBlitPass(target,snapshot,Vector2.one,Vector2.zero,passName:"Energy seal / scene copy");
                }
                using var builder=graph.AddRasterRenderPass<PassData>("Energy seal and transient shield",out var data);
                data.target=target;data.snapshot=snapshot;data.depth=resources.cameraDepthTexture;data.surfaces=surfaces;
                data.pulseOwner=pulse || visor?PulseOwner:null;data.visor=visor;data.pulse=Pulse;data.pulseProperties=pulseProperties;
                data.renderers=default;
                if(surfaces)
                {
                    var drawing=RenderingUtils.CreateDrawingSettings(Tags,frameData.Get<UniversalRenderingData>(),camera,frameData.Get<UniversalLightData>(),SortingCriteria.CommonTransparent);
                    data.renderers=graph.CreateRendererList(new RendererListParams(frameData.Get<UniversalRenderingData>().cullResults,drawing,new FilteringSettings(RenderQueueRange.transparent)));
                    builder.UseRendererList(data.renderers);builder.UseTexture(data.depth,AccessFlags.Read);
                }
                if(snapshot.IsValid())builder.UseTexture(snapshot,AccessFlags.Read);
                builder.SetRenderAttachment(target,0,AccessFlags.ReadWrite);
                // Renderer-list surface materials consume these dedicated, declared draw-local inputs.
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static(PassData d,RasterGraphContext context)=>
                {
                    if(d.snapshot.IsValid())
                    {
                        context.cmd.SetGlobalTexture(SceneColor,d.snapshot);
                        context.cmd.SetGlobalVector(ColorScale,ScaleBias(context,d.snapshot,d.target));
                    }
                    if(d.surfaces)
                    {
                        context.cmd.SetGlobalTexture(SceneDepth,d.depth);
                        context.cmd.SetGlobalVector(DepthScale,ScaleBias(context,d.depth,d.target));
                        context.cmd.DrawRendererList(d.renderers);
                    }
                    if(d.pulseOwner && d.pulseOwner.isActiveAndEnabled && d.pulseOwner.FieldMaterial)
                    {
                        var block=d.pulseProperties;block.Clear();block.SetVector("_OwnerPulse",new Vector4(d.pulse,0,0,0));
                        // Blit.Vert applies this uniform even when no source texture is sampled.
                        block.SetVector("_BlitScaleBias",new Vector4(1,1,0,0));
                        if(d.visor)context.cmd.DrawProcedural(Matrix4x4.identity,d.pulseOwner.FieldMaterial,2,MeshTopology.Triangles,3,1,block);
                        if(d.pulse>0)context.cmd.DrawProcedural(Matrix4x4.identity,d.pulseOwner.FieldMaterial,1,MeshTopology.Triangles,3,1,block);
                    }
                });
            }
            private static Vector4 ScaleBias(RasterGraphContext context,TextureHandle source,TextureHandle target)
            {RTHandle handle=source;Vector2 scale=handle.useScaling?(Vector2)handle.rtHandleProperties.rtHandleScale:Vector2.one;bool flip=context.GetTextureUVOrigin(in source)!=context.GetTextureUVOrigin(in target);return flip?new Vector4(scale.x,-scale.y,0,scale.y):new Vector4(scale.x,scale.y,0,0);}
            private sealed class PassData
            {
                internal TextureHandle target,snapshot,depth;internal bool surfaces,visor;internal RendererListHandle renderers;
                internal EnergyShieldPresentation pulseOwner;internal float pulse;internal MaterialPropertyBlock pulseProperties;
            }
        }
    }
}
