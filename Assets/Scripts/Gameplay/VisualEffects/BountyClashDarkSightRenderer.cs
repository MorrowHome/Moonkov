using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS
{
    /// <summary>Owner-camera presentation from the authorized client cache only. Never queries ghosts.</summary>
    public static class BountyClashDarkSightRenderer
    {
        private static Driver s_Driver;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { s_Driver?.Dispose(); s_Driver=null; }

        // Call after SetSenseInput each HUD frame, using the actual OwnedCamera.
        public static void Publish(Camera ownedCamera,bool view,bool boost,bool presence,BountyClashClientState authorizedCache,double now)
        {
            if(!ownedCamera || ownedCamera.cameraType!=CameraType.Game || authorizedCache==null) { ClearImmediate(); return; }
            if(ownedCamera.TryGetComponent<UniversalAdditionalCameraData>(out var cameraData) && cameraData.renderType==CameraRenderType.Overlay) {ClearImmediate();return;}
            var snapshot=authorizedCache.Snapshot;
            view &= authorizedCache.Approved && authorizedCache.HasSnapshot && snapshot.Connected &&
                snapshot.Phase==ClashPhase.Active && snapshot.Life==ClashLife.Alive;
            if(s_Driver!=null && s_Driver.Camera!=ownedCamera) {s_Driver.Dispose();s_Driver=null;}
            if(s_Driver==null && view)s_Driver=new Driver(ownedCamera);
            s_Driver?.Publish(view,boost,presence,authorizedCache,now);
        }
        public static void Clear()
        {
            var driver=s_Driver;if(driver==null)return;driver.Stop();
            // An inactive owner will not render its exit fade; do not retain a dead subscription.
            if(!driver.Camera || !driver.Camera.isActiveAndEnabled){driver.Dispose();s_Driver=null;}
        }
        public static void ClearImmediate() {s_Driver?.Dispose();s_Driver=null;}

        private sealed class Driver : IDisposable
        {
            internal readonly Camera Camera;
            private readonly Material material;
            private readonly DarkPass pass;
            private readonly Vector4[] contacts=new Vector4[BountyClashRules.MaxPlayers-1];
            private BountyClashClientState cache;
            private bool wanted,boost,presence,disposed;
            private double publishedAt,lastUpdate;
            private float blend;
            private AudioSource audio;
            private GameObject audioRoot;
            private AudioClip enter,exit;

            internal Driver(Camera camera)
            {
                Camera=camera;
                var shader=Resources.Load<Shader>("Moonkov/BountyClashDarkSight");
                if(shader && shader.isSupported)
                {
                    material=new Material(shader){name="Owner dark sight",hideFlags=HideFlags.HideAndDontSave};
                    pass=new DarkPass(material);
                }
                lastUpdate=Time.realtimeSinceStartupAsDouble;
                RenderPipelineManager.beginCameraRendering+=BeginCamera;
            }
            internal void Publish(bool view,bool boosted,bool sensed,BountyClashClientState data,double now)
            {
                if(disposed)return;
                cache=data;boost=view&&boosted;presence=view&&sensed;publishedAt=now;
                SetWanted(view);
                if(!boost){Array.Clear(contacts,0,contacts.Length);if(pass!=null)pass.Count=0;}
                if(!presence && pass!=null)pass.Presence=false;
                if(!view){boost=presence=false;cache=null;Array.Clear(contacts,0,contacts.Length);if(pass!=null){pass.Count=0;pass.Presence=false;}}
            }
            private void SetWanted(bool view)
            {
                if(wanted==view)return;
                wanted=view;
                if(Application.isPlaying && Camera)PlayTransition(view);
            }
            internal void Stop()
            {
                if(disposed)return;
                SetWanted(false);boost=presence=false;cache=null;Array.Clear(contacts,0,contacts.Length);
                if(pass!=null){pass.Count=0;pass.Presence=false;}
            }
            private void BeginCamera(ScriptableRenderContext context,Camera camera)
            {
                if(disposed)return;
                if(!Camera){Dispose();if(ReferenceEquals(s_Driver,this))s_Driver=null;return;}
                if(camera!=Camera || camera.cameraType!=CameraType.Game)return;
                camera.TryGetComponent<UniversalAdditionalCameraData>(out var additional);
                if(additional && additional.renderType==CameraRenderType.Overlay)return;
                double now=Time.realtimeSinceStartupAsDouble;
                if(wanted && now-publishedAt>.2)Stop(); // Missing HUD publication fails closed.
                if(wanted && (cache==null || !cache.Approved || !cache.HasSnapshot || !cache.Snapshot.Connected ||
                    cache.Snapshot.Life!=ClashLife.Alive || cache.Snapshot.Phase!=ClashPhase.Active))Stop();
                float delta=Mathf.Clamp((float)(now-lastUpdate),0,.1f);lastUpdate=now;
                blend=Mathf.MoveTowards(blend,wanted?1:0,delta/(wanted ? .12f : .18f));
                if(blend<=0 && !wanted){Dispose();if(ReferenceEquals(s_Driver,this))s_Driver=null;return;}
                if(material==null || pass==null || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline))return;
                int count=0;
                bool authorizedPresence=wanted && presence && cache!=null && cache.Approved && cache.HasSnapshot &&
                    cache.Snapshot.IsCarrier && cache.Snapshot.Connected && cache.Snapshot.Life==ClashLife.Alive &&
                    cache.Snapshot.Phase==ClashPhase.Active && cache.Sense.Presence && now-cache.SenseReceivedAt<.7;
                bool authorizedBoost=wanted && boost && cache!=null && cache.SenseVisible(now) &&
                    cache.Sense.Remaining-(float)Math.Max(0,now-cache.SenseReceivedAt)>0;
                if(authorizedBoost)
                    for(int i=0;i<cache.Contacts.Length && count<contacts.Length;i++)
                    {
                        if(!cache.ContactVisible(i,now))continue;
                        var contact=cache.Contacts[i];Vector3 world=contact.Position;
                        var viewport=camera.WorldToViewportPoint(world);
                        if(viewport.z<=camera.nearClipPlane || viewport.x<0 || viewport.x>1 || viewport.y<0 || viewport.y>1)continue;
                        // Never sharpen the server's uncertain point or interpolate a stale trail.
                        contacts[count++]=new Vector4(world.x,world.y+.6f,world.z,Mathf.Max(3,contact.UncertaintyRadius));
                    }
                pass.Blend=blend;pass.Presence=authorizedPresence;pass.Time=(float)now;
                pass.Contacts=contacts;pass.Count=count;pass.Owner=camera;
                var renderer=additional?additional.scriptableRenderer:pipeline.scriptableRenderer;
                renderer?.EnqueuePass(pass);
            }
            private void PlayTransition(bool entering)
            {
                // A single short helmet-local cue per input transition, through the existing SFX group.
                var game=GameManager.Instance;
                if(!game || !game.AudioMixer)return;
                if(!audio)
                {
                    audioRoot=new GameObject("Dark sight / local SFX"){hideFlags=HideFlags.HideAndDontSave};
                    audioRoot.transform.SetParent(Camera.transform,false);audio=audioRoot.AddComponent<AudioSource>();
                    audio.playOnAwake=false;audio.spatialBlend=0;
                    var groups=game.AudioMixer.FindMatchingGroups(nameof(SoundMixer.SoundMixerGroup.SFX));
                    if(groups.Length==0)return;
                    audio.outputAudioMixerGroup=groups[0];
                    enter=Tone(true);exit=Tone(false);
                }
                if(enter&&exit){audio.Stop();audio.PlayOneShot(entering?enter:exit,.6f);}
            }
            private static AudioClip Tone(bool entering)
            {
                const int rate=16000;float duration=entering ? .18f : .16f;var samples=new float[(int)(rate*duration)];
                for(int i=0;i<samples.Length;i++)
                {float t=i/(float)rate,u=t/duration;float f=entering?105:78;samples[i]=(Mathf.Sin(2*Mathf.PI*f*t)+.23f*Mathf.Sin(2*Mathf.PI*(f*1.9f)*t))*Mathf.Pow(Mathf.Sin(Mathf.PI*u),2)*.12f;}
                var clip=AudioClip.Create(entering?"Original dark sight entry":"Original dark sight exit",samples.Length,1,rate,false);clip.SetData(samples,0);return clip;
            }
            public void Dispose()
            {
                if(disposed)return;disposed=true;RenderPipelineManager.beginCameraRendering-=BeginCamera;
                cache=null;Array.Clear(contacts,0,contacts.Length);
                if(pass!=null){pass.Count=0;pass.Presence=false;pass.Blend=0;pass.Owner=null;}
                if(audio)audio.Stop();Destroy(audioRoot);Destroy(enter);Destroy(exit);CoreUtils.Destroy(material);
            }
            private static void Destroy(UnityEngine.Object obj)
            {if(!obj)return;if(Application.isPlaying)UnityEngine.Object.Destroy(obj);else UnityEngine.Object.DestroyImmediate(obj);}
        }

        private sealed class DarkPass : ScriptableRenderPass
        {
            private readonly Material material;
            private readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
            internal float Blend,Time;internal bool Presence;internal Camera Owner;internal Vector4[] Contacts;internal int Count;
            internal DarkPass(Material material)
            {this.material=material;renderPassEvent=RenderPassEvent.AfterRenderingPostProcessing;requiresIntermediateTexture=true;ConfigureInput(ScriptableRenderPassInput.Depth);}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
            {
                var resources=frameData.Get<UniversalResourceData>();var camera=frameData.Get<UniversalCameraData>();
                if(camera.camera!=Owner || resources.isActiveTargetBackBuffer || !material || Blend<=0)return;
                var source=resources.activeColorTexture;var descriptor=graph.GetTextureDesc(source);
                descriptor.name="Owner dark sight composite";descriptor.clearBuffer=false;
                var destination=graph.CreateTexture(descriptor);
                using var builder=graph.AddRasterRenderPass<PassData>("Owner dark sight / authorized soft contacts",out var data);
                data.source=source;data.destination=destination;data.depth=resources.cameraDepthTexture;
                data.material=material;data.properties=properties;data.blend=Blend;data.presence=Presence?1:0;data.time=Time;
                data.contacts=Contacts;data.count=Count;data.projection=camera.GetProjectionMatrix();data.view=camera.GetViewMatrix();
                data.right=Owner.transform.right;data.up=Owner.transform.up;
                data.texel=new Vector4(1f/descriptor.width,1f/descriptor.height,descriptor.width,descriptor.height);
                builder.UseTexture(source,AccessFlags.Read);if(data.depth.IsValid())builder.UseTexture(data.depth,AccessFlags.Read);
                builder.SetRenderAttachment(destination,0,AccessFlags.Write);
                builder.SetRenderFunc(static(PassData d,RasterGraphContext context)=>
                {
                    var block=d.properties;block.Clear();Texture sourceTexture=d.source;block.SetTexture("_BlitTexture",sourceTexture);block.SetVector("_BlitScaleBias",ScaleBias(context,d.source,d.destination));
                    block.SetVector("_DarkTexel",d.texel);block.SetVector("_DarkState",new Vector4(d.blend,d.presence,d.time,d.depth.IsValid()?1:0));
                    if(d.depth.IsValid()){Texture depthTexture=d.depth;block.SetTexture("_DarkDepth",depthTexture);block.SetVector("_DarkDepthScaleBias",ScaleBias(context,d.depth,d.destination));}
                    context.cmd.DrawProcedural(Matrix4x4.identity,d.material,0,MeshTopology.Triangles,3,1,block);
                    if(d.count>0)
                    {
                        bool flip=context.GetTextureUVOrigin(in d.destination)==TextureUVOrigin.BottomLeft;
                        block.SetMatrix("_DarkViewProjection",GL.GetGPUProjectionMatrix(d.projection,flip)*d.view);
                        block.SetVector("_DarkCameraRight",d.right);block.SetVector("_DarkCameraUp",d.up);
                        block.SetVectorArray("_DarkContacts",d.contacts);
                        context.cmd.DrawProcedural(Matrix4x4.identity,d.material,1,MeshTopology.Triangles,6,d.count,block);
                    }
                });
                resources.cameraColor=destination;
            }
            private static Vector4 ScaleBias(RasterGraphContext context,TextureHandle source,TextureHandle destination)
            {RTHandle h=source;var scale=h.useScaling?(Vector2)h.rtHandleProperties.rtHandleScale:Vector2.one;bool flip=context.GetTextureUVOrigin(in source)!=context.GetTextureUVOrigin(in destination);return flip?new Vector4(scale.x,-scale.y,0,scale.y):new Vector4(scale.x,scale.y,0,0);}
            private sealed class PassData
            {
                internal TextureHandle source,destination,depth;internal Material material;internal MaterialPropertyBlock properties;
                internal Vector4 texel;internal float blend,presence,time;internal Vector4[] contacts;internal int count;
                internal Matrix4x4 projection,view;internal Vector3 right,up;
            }
        }
    }
}
