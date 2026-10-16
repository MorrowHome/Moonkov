using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Native lilToon authoring. Source atlases, rig, shader and material GUIDs stay intact.</summary>
public static class MoonkovLilToonLookDev
{
    public const string Root = "Assets/DollSinger/LilToonLookDev";
    public const string ScenePath = Root + "/LilToonLookDev.unity";
    public const string Grade = Root + "/LookGrade.asset";
    public const string OriginalGrade = Root + "/Before/Grade.asset";
    public const string Source = "Assets/DollSinger/Art/Materials/";
    public static readonly string[] Names = { "Face", "Body", "EyeWhite", "Dress", "Jewel", "Brows", "Hair", "Eyes", "Lashes", "Shoes", "Glasses", "InnerDress", "DressDetail" };
    public static Material Get(string name, bool before = false) => AssetDatabase.LoadAssetAtPath<Material>($"{Root}/{(before ? "Before" : "Materials")}/{name}.mat");
    static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Maps/{name}.png");
    static void F(Material m, string p, float v) { if (!m.HasProperty(p)) throw new InvalidOperationException(p); m.SetFloat(p,v); }
    static void C(Material m, string p, Color v) => m.SetColor(p,v);
    static float G(float u,float v,float x,float y,float sx,float sy) => Mathf.Exp(-Mathf.Pow((u-x)/sx,2)-Mathf.Pow((v-y)/sy,2));

    [MenuItem("Tools/Moonkov/lilToon/Build LookDev Copies")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode only");
        foreach (var path in new[]{Root,Root+"/Before",Root+"/Materials",Root+"/Maps"})
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));
        foreach (string name in Names)
        {
            if (!Get(name,true)) AssetDatabase.CopyAsset(Source+name+".mat",$"{Root}/Before/{name}.mat");
            if (!Get(name)) AssetDatabase.CopyAsset($"{Root}/Before/{name}.mat",$"{Root}/Materials/{name}.mat");
        }
        WriteMaps();
        foreach (string name in Names)
        {
            var m=Get(name);m.CopyPropertiesFromMaterial(Get(name,true));Configure(m);EditorUtility.SetDirty(m);
        }
        if (!AssetDatabase.LoadAssetAtPath<VolumeProfile>(OriginalGrade)) AssetDatabase.CopyAsset(MoonkovAnimeLightingSetup.ProfilePath,OriginalGrade);
        if (!AssetDatabase.LoadAssetAtPath<VolumeProfile>(Grade)) AssetDatabase.CopyAsset(OriginalGrade,Grade);
        ConfigureGrade(AssetDatabase.LoadAssetAtPath<VolumeProfile>(Grade));
        foreach(string name in Names)AssetDatabase.SaveAssetIfDirty(Get(name));
        SaveGrade(AssetDatabase.LoadAssetAtPath<VolumeProfile>(Grade));
        if (!File.Exists(ScenePath)) BuildStudio();
    }

    [MenuItem("Tools/Moonkov/lilToon/Apply Native Look To Game")]
    public static void ApplyToGame()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit Mode only");
        // Validate the entire set before touching shared assets. Build never writes the game look.
        foreach(string name in Names)
            if(!Get(name)||!Get(name,true)||!AssetDatabase.LoadAssetAtPath<Material>(Source+name+".mat"))throw new InvalidOperationException("Build LookDev Copies first: "+name);
        foreach(string name in Names)
        {
            var target=AssetDatabase.LoadAssetAtPath<Material>(Source+name+".mat");
            if(target.shader!=Get(name).shader)throw new InvalidOperationException("Shader mismatch: "+name);
        }
        foreach(string name in Names)
        {
            var target=AssetDatabase.LoadAssetAtPath<Material>(Source+name+".mat");Undo.RecordObject(target,"Apply native lilToon look");
            target.CopyPropertiesFromMaterial(Get(name));EditorUtility.SetDirty(target);AssetDatabase.SaveAssetIfDirty(target);
        }
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(MoonkovAnimeLightingSetup.ProfilePath);
        Undo.RecordObjects(profile.components.ToArray(),"Apply native lilToon grade");ConfigureGrade(profile);SaveGrade(profile);
    }

    [MenuItem("Tools/Moonkov/lilToon/Restore Before Look")]
    public static void RestoreBefore()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit Mode only");
        foreach(string name in Names)if(!Get(name,true))throw new InvalidOperationException("No before snapshot: "+name);
        foreach(string name in Names)
        {
            var target=AssetDatabase.LoadAssetAtPath<Material>(Source+name+".mat");Undo.RecordObject(target,"Restore lilToon look");
            target.CopyPropertiesFromMaterial(Get(name,true));EditorUtility.SetDirty(target);AssetDatabase.SaveAssetIfDirty(target);
        }
        var before=AssetDatabase.LoadAssetAtPath<VolumeProfile>(OriginalGrade);var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(MoonkovAnimeLightingSetup.ProfilePath);
        foreach(var component in profile.components)
        {
            var source=before.components.FirstOrDefault(c=>c.GetType()==component.GetType());if(!source)continue;
            Undo.RecordObject(component,"Restore lilToon grade");EditorUtility.CopySerialized(source,component);EditorUtility.SetDirty(component);
        }
        SaveGrade(profile);
    }

    static void SaveGrade(VolumeProfile profile)
    {
        foreach(var component in profile.components)AssetDatabase.SaveAssetIfDirty(component);AssetDatabase.SaveAssetIfDirty(profile);
    }

    public static void Configure(Material m)
    {
        string name=m.name.Replace(" (Instance)","");
        bool skin=name=="Face"||name=="Body",hair=name=="Hair",eye=name=="Eyes",white=name=="EyeWhite",line=name=="Brows"||name=="Lashes";
        // Keep diffuse color below the former overbright ceiling. Native GGX can
        // still produce concentrated HDR highlights after diffuse lighting.
        F(m,"_LightMinLimit",0);F(m,"_LightMaxLimit",1);F(m,"_AsUnlit",0);F(m,"_UseEmission",0);F(m,"_UseEmission2nd",0);
        F(m,"_UseShadow",1);F(m,"_ShadowReceive",1);F(m,"_ShadowStrength",skin?.8f:eye||white||line?.55f:.9f);
        F(m,"_ShadowBorder",skin?.72f:hair?.78f:.65f);F(m,"_ShadowBlur",skin?.6f:hair?.5f:.4f);
        F(m,"_ShadowNormalStrength",skin?.55f:1);F(m,"_Shadow2ndBorder",.16f);F(m,"_Shadow2ndBlur",.25f);
        F(m,"_ShadowBorderRange",.035f);
        C(m,"_ShadowColor",skin?new Color(.94f,.73f,.70f):hair?new Color(.57f,.64f,.79f):new Color(.64f,.67f,.83f));
        C(m,"_Shadow2ndColor",skin?new Color(.78f,.49f,.52f,.18f):new Color(.43f,.47f,.64f,.28f));
        C(m,"_Color",Color.white);
        m.SetVector("_MainTexHSVG",new Vector4(0,eye?1.12f:hair?1.06f:skin?1:1.12f,1,skin?1:hair?1.12f:1.2f));
        // Native RimShade adds a restrained warm edge to skin and colored depth to silver hair.
        F(m,"_UseRimShade",skin||hair?1:0);F(m,"_RimShadeBorder",.5f);F(m,"_RimShadeBlur",.8f);F(m,"_RimShadeFresnelPower",2);
        C(m,"_RimShadeColor",skin?new Color(1,.78f,.77f,.22f):new Color(.57f,.66f,.83f,.4f));
        F(m,"_UseRim",skin||hair?1:0);F(m,"_RimEnableLighting",1);F(m,"_RimShadowMask",1);F(m,"_RimDirStrength",1);
        F(m,"_RimBorder",.6f);F(m,"_RimBlur",.4f);F(m,"_RimFresnelPower",4);
        C(m,"_RimColor",new Color(.82f,.91f,1,skin?.06f:.12f));
        C(m,"_RimIndirColor",new Color(1,.85f,.76f,0));
        C(m,"_OutlineColor",skin?new Color(.24f,.12f,.14f):hair?new Color(.09f,.13f,.2f):new Color(.07f,.09f,.15f));
        F(m,"_OutlineEnableLighting",1);
        F(m,"_UseReflection",line?0:1);F(m,"_ApplySpecular",1);F(m,"_ApplyReflection",0);
        F(m,"_SpecularToon",0);F(m,"_GSAAStrength",1);F(m,"_Metallic",0);F(m,"_Reflectance",.035f);
        F(m,"_UseAnisotropy",hair?1:0);F(m,"_UseMatCap",skin||eye||name=="Jewel"||name=="Glasses"?1:0);
        F(m,"_MatCapEnableLighting",1);F(m,"_MatCapShadowMask",1);F(m,"_MatCapMainStrength",skin?.65f:hair?.7f:.15f);
        C(m,"_MatCapColor",new Color(1,.98f,.96f,skin?.26f:hair?.28f:eye?.22f:.32f));
        F(m,"_UseBacklight",skin?1:0);C(m,"_BacklightColor",new Color(1,.58f,.47f,.12f));
        F(m,"_BacklightReceiveShadow",1);F(m,"_BacklightBorder",.15f);F(m,"_BacklightBlur",.6f);F(m,"_BacklightDirectivity",3);
        C(m,"_ReflectionColor",new Color(1,.97f,.94f,skin?.65f:eye?.55f:hair?.45f:.22f));
        F(m,"_Smoothness",skin?1:eye?.9f:white?.35f:hair?.72f:.38f);
        if (skin)
        {
            m.SetTexture("_SmoothnessTex",Tex(name+"Surface"));m.SetTexture("_ReflectionColorTex",Tex(name+"Surface"));
            if(name=="Face") m.SetTexture("_MatCapBlendMask",Tex("FaceSheenMask"));
        }
        if(hair)
        {
            m.SetTexture("_AnisotropyTangentMap",Tex("HairDirection"));F(m,"_Anisotropy2Reflection",1);F(m,"_AnisotropyScale",.6f);
            F(m,"_AnisotropyTangentWidth",1.1f);F(m,"_AnisotropyBitangentWidth",.45f);F(m,"_AnisotropySpecularStrength",.32f);
            F(m,"_Anisotropy2ndTangentWidth",1.8f);F(m,"_Anisotropy2ndBitangentWidth",.85f);F(m,"_Anisotropy2ndShift",.12f);F(m,"_Anisotropy2ndSpecularStrength",.08f);
        }
        if(eye||white||line) { C(m,"_ShadowColor",new Color(.77f,.81f,.88f));F(m,"_UseRimShade",0); }
        if(eye)
        {
            F(m,"_UseParallax",1);F(m,"_UsePOM",0);F(m,"_Parallax",.02f);F(m,"_ParallaxOffset",.5f);
            m.SetTexture("_ParallaxMap",Tex("IrisDepth"));
        }
        if(white) { F(m,"_UseReflection",0);F(m,"_UseMatCap",0); }
        if(eye||name=="Glasses"||name=="Jewel")
        {
            // Reuse lilToon's own reflection asset; no procedural iris or painted catchlight replacement.
            m.SetTexture("_MatCapTex",AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/jp.lilxyzw.liltoon/Texture/matcap_metal_realistic.png"));
            C(m,"_MatCapColor",new Color(1,.98f,.95f,eye?.24f:.28f));
        }
        if(name=="Jewel"||name=="Glasses") { F(m,"_Smoothness",.75f);F(m,"_Metallic",name=="Glasses"?.45f:0);C(m,"_ReflectionColor",new Color(1,.91f,.73f,.5f)); }
        if(name=="Dress"||name=="DressDetail"||name=="InnerDress"||name=="Shoes")
        {
            string atlas=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")));
            m.SetTexture("_SmoothnessTex",Tex(atlas+"Surface"));m.SetTexture("_ReflectionColorTex",Tex(atlas+"Surface"));
            m.SetTexture("_MetallicGlossMap",Tex(atlas+"Metal"));F(m,"_Metallic",.5f);F(m,"_Smoothness",.85f);
            C(m,"_ReflectionColor",new Color(1,.95f,.85f,.42f));
        }
    }

    public static void ConfigureGrade(VolumeProfile p)
    {
        p.TryGet<ColorAdjustments>(out var c);c.postExposure.Override(.4f);c.contrast.Override(6);c.saturation.Override(4);
        p.TryGet<Tonemapping>(out var tone);tone.mode.Override(TonemappingMode.Neutral);
        p.TryGet<SplitToning>(out var s);s.shadows.Override(Color.gray);s.highlights.Override(Color.gray);s.balance.Override(0);
        p.TryGet<Bloom>(out var b);b.intensity.Override(.1f);b.threshold.Override(1.5f);
        foreach(var component in p.components)EditorUtility.SetDirty(component);EditorUtility.SetDirty(p);
    }

    static void Write(string name,int size,Func<float,float,Color> sample,bool normal=false)
    {
        var texture=new Texture2D(size,size,TextureFormat.RGBA32,false,true);
        try
        {
            var colors=new Color[size*size];for(int y=0;y<size;y++)for(int x=0;x<size;x++)colors[y*size+x]=sample((x+.5f)/size,(y+.5f)/size);
            texture.SetPixels(colors);texture.Apply();byte[] bytes=texture.EncodeToPNG();string path=$"{Root}/Maps/{name}.png";
            bool changed=!File.Exists(path)||!File.ReadAllBytes(path).SequenceEqual(bytes);
            if(changed){File.WriteAllBytes(path,bytes);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);}
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            bool settingsChanged=importer.textureCompression!=TextureImporterCompression.CompressedHQ||importer.sRGBTexture||importer.isReadable||!importer.mipmapEnabled||importer.wrapMode!=TextureWrapMode.Clamp||importer.alphaSource!=TextureImporterAlphaSource.FromInput;
            importer.sRGBTexture=false;importer.mipmapEnabled=true;importer.isReadable=false;importer.wrapMode=TextureWrapMode.Clamp;importer.textureCompression=TextureImporterCompression.CompressedHQ;
            importer.alphaSource=TextureImporterAlphaSource.FromInput;if(changed||settingsChanged)importer.SaveAndReimport();
        }
        finally{UnityEngine.Object.DestroyImmediate(texture);}
    }

    static void WriteMaps()
    {
        // Data masks only: do not repaint the mouth, skin color or iris design.
        Func<float,float,float> sheen=(u,v)=>Mathf.Clamp01(.08f+.4f*(G(u,v,.39f,.535f,.07f,.045f)+G(u,v,.61f,.535f,.07f,.045f))+.6f*G(u,v,.5f,.565f,.022f,.03f)+.25f*G(u,v,.5f,.76f,.12f,.12f))*(1-G(u,v,.5f,.486f,.045f,.028f));
        Write("FaceSurface",512,(u,v)=>{float a=sheen(u,v);float smooth=.52f+.4f*a;float strength=(.1f+.75f*a)*(1-G(u,v,.5f,.486f,.045f,.028f));return new Color(smooth,smooth,smooth,strength);});
        Write("FaceSheenMask",512,(u,v)=>Color.white*sheen(u,v));
        Write("BodySurface",128,(u,v)=>new Color(.58f,.58f,.58f,.23f));
        // Height follows the original iris UV island. Parallax moves its existing
        // drawing slightly beneath the independent view-dependent MatCap reflection.
        Write("IrisDepth",512,(u,v)=>
        {
            float r=new Vector2((u-.5954f)/.080f,(v-.1468f)/.132f).magnitude;
            return Color.white*Mathf.Lerp(.35f,.5f,Mathf.SmoothStep(0,1,r));
        });
        foreach(string atlas in new[]{"yifu","yifu2","xiezi"})
        {
            var source=new Texture2D(2,2);source.LoadImage(File.ReadAllBytes($"Assets/DollSinger/Art/Textures/{atlas}.png"));
            Func<Color,float> gold=c=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(.07f,.24f,c.r-c.b))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,.4f,c.g));
            try
            {
                Write(atlas+"Surface",512,(u,v)=>{float g=gold(source.GetPixelBilinear(u,v));float s=Mathf.Lerp(.38f,.87f,g);return new Color(s,s,s,Mathf.Lerp(.16f,.85f,g));});
                Write(atlas+"Metal",512,(u,v)=>Color.white*gold(source.GetPixelBilinear(u,v)));
            }
            finally{UnityEngine.Object.DestroyImmediate(source);}
        }
        HairFlow();
    }

    static void HairFlow()
    {
        var r=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Moonkov/MenuCharacterVisual.prefab").GetComponentInChildren<SkinnedMeshRenderer>();
        var mesh=r.sharedMesh;int slot=Array.FindIndex(r.sharedMaterials,m=>m.name=="Hair");
        var p=mesh.vertices;var n=mesh.normals;var tangent=mesh.tangents;var uv=mesh.uv;var ids=mesh.GetTriangles(slot);
        const int size=512;var colors=Enumerable.Repeat(new Color(1,.5f,.5f,1),size*size).ToArray();var occupied=new bool[size*size];
        var root=new Vector3(0,0,ids.Max(i=>p[i].z));
        for(int t=0;t<ids.Length;t+=3)
        {
            int a=ids[t],b=ids[t+1],c=ids[t+2];var A=uv[a]*size;var B=uv[b]*size;var D=uv[c]*size;
            float den=(B.y-D.y)*(A.x-D.x)+(D.x-B.x)*(A.y-D.y);if(Mathf.Abs(den)<.001f)continue;
            for(int y=Mathf.Max(0,(int)Mathf.Floor(Mathf.Min(A.y,Mathf.Min(B.y,D.y))));y<=Mathf.Min(size-1,Mathf.CeilToInt(Mathf.Max(A.y,Mathf.Max(B.y,D.y))));y++)
            for(int x=Mathf.Max(0,(int)Mathf.Floor(Mathf.Min(A.x,Mathf.Min(B.x,D.x))));x<=Mathf.Min(size-1,Mathf.CeilToInt(Mathf.Max(A.x,Mathf.Max(B.x,D.x))));x++)
            {
                float w=((B.y-D.y)*(x+.5f-D.x)+(D.x-B.x)*(y+.5f-D.y))/den;
                float q=((D.y-A.y)*(x+.5f-D.x)+(A.x-D.x)*(y+.5f-D.y))/den;float z=1-w-q;if(w<0||q<0||z<0)continue;
                var normal=(n[a]*w+n[b]*q+n[c]*z).normalized;var tang=tangent[a]*w+tangent[b]*q+tangent[c]*z;
                var T=new Vector3(tang.x,tang.y,tang.z).normalized;var bi=Vector3.Cross(normal,T)*Mathf.Sign(tang.w);
                var flow=Vector3.ProjectOnPlane(p[a]*w+p[b]*q+p[c]*z-root,normal).normalized;if(flow.sqrMagnitude<.01f)flow=T;
                var dir=new Vector2(Vector3.Dot(flow,T),Vector3.Dot(flow,bi)).normalized*.98f;
                colors[y*size+x]=new Color(.5f+.5f*dir.x,.5f+.5f*dir.y,.6f,1);occupied[y*size+x]=true;
            }
        }
        for(int pass=0;pass<8;pass++)
        {
            var previous=(bool[])occupied.Clone();for(int y=1;y<size-1;y++)for(int x=1;x<size-1;x++)
            {int i=y*size+x;if(previous[i])continue;foreach(int j in new[]{i-1,i+1,i-size,i+size})if(previous[j]){colors[i]=colors[j];occupied[i]=true;break;}}
        }
        Write("HairDirection",size,(u,v)=>colors[Mathf.Min(size-1,(int)(v*size))*size+Mathf.Min(size-1,(int)(u*size))],true);
    }

    public static void Pose(GameObject actor)
    {
        var animator=actor.GetComponentInChildren<Animator>();animator.enabled=true;animator.runtimeAnimatorController=null;animator.applyRootMotion=false;animator.Rebind();
        var graph=PlayableGraph.Create("lilToon fixed pose");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        try
        {
            var clip=AnimationClipPlayable.Create(graph,AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/DollSinger/Animation/Locomotion/Unarmed_Idle.anim"));
            clip.SetApplyFootIK(false);clip.SetTime(0);AnimationPlayableOutput.Create(graph,"Character",animator).SetSourcePlayable(clip);graph.Play();graph.Evaluate(0);
        }
        finally{graph.Destroy();}
        actor.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);animator.enabled=false;
        // Keep the authored secondary-bone pose; gameplay spring simulation is not a pose baker.
    }

    public static void SetLook(GameObject actor,bool before)
    {
        foreach(var r in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            r.sharedMaterials=r.sharedMaterials.Select(m=>m&&Names.Contains(m.name.Replace(" (Instance)",""))?Get(m.name.Replace(" (Instance)",""),before):m).ToArray();
    }

    static void BuildStudio()
    {
        var previous=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.04f,.045f,.055f);RenderSettings.skybox=null;RenderSettings.reflectionIntensity=0;
            var actor=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Moonkov/MenuCharacterVisual.prefab"),scene);actor.name="lilToon Character";
            foreach(var b in actor.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
            foreach(var l in actor.GetComponentsInChildren<Light>(true))l.enabled=false;
            foreach(var line in actor.GetComponentsInChildren<LineRenderer>(true))line.enabled=false;
            Pose(actor);SetLook(actor,false);
            var key=new GameObject("lilToon Key").AddComponent<Light>();key.type=LightType.Directional;key.intensity=1.2f;key.color=new Color(1,.96f,.92f);key.shadows=LightShadows.Soft;
            key.transform.rotation=Quaternion.LookRotation(-new Vector3(.6f,.7f,1).normalized);RenderSettings.sun=key;
            var fill=new GameObject("lilToon Fill").AddComponent<Light>();fill.type=LightType.Point;fill.intensity=.25f;fill.range=5;fill.color=new Color(.73f,.85f,1);fill.transform.position=new Vector3(-1,1.6f,1);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="lilToon Floor";
            var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.SetColor("_BaseColor",new Color(.13f,.16f,.22f));mat.SetFloat("_Smoothness",.1f);
            AssetDatabase.CreateAsset(mat,Root+"/Floor.mat");floor.GetComponent<Renderer>().sharedMaterial=mat;
            var camera=new GameObject("lilToon Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.allowHDR=true;camera.nearClipPlane=.05f;camera.farClipPlane=40;
            camera.transform.position=new Vector3(.08f,1.33f,1.65f);camera.transform.LookAt(new Vector3(0,1.27f,0));camera.fieldOfView=28;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.105f,.15f);
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.renderShadows=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var volume=new GameObject("lilToon Volume").AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Grade);
            EditorSceneManager.SaveScene(scene,ScenePath);
        }
        finally{SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);}
    }
}
