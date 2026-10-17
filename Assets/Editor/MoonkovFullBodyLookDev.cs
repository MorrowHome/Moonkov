using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Material-specific full-body look, with immutable pre-task copies.</summary>
public static class MoonkovFullBodyLookDev
{
    public const string Root=MoonkovLilToonLookDev.Root+"/FullBody";
    public static readonly string[] Names=MoonkovLilToonLookDev.Names;
    public static Material Get(string name,bool before=false)=>AssetDatabase.LoadAssetAtPath<Material>($"{Root}/{(before?"Before":"Materials")}/{name}.mat");
    static Texture2D Map(string name)=>AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Maps/{name}.png");
    static void F(Material m,string p,float v){if(!m.HasProperty(p))throw new InvalidOperationException(p);m.SetFloat(p,v);}
    static void C(Material m,string p,Color v)=>m.SetColor(p,v);
    static float G(float u,float v,float x,float y,float sx,float sy)=>Mathf.Exp(-Mathf.Pow((u-x)/sx,2)-Mathf.Pow((v-y)/sy,2));

    [MenuItem("Tools/Moonkov/lilToon/Full Body/Build Copies")]
    public static void Build()
    {
        RequireEdit();
        foreach(var path in new[]{Root,Root+"/Before",Root+"/Materials",Root+"/Maps"})
            if(!AssetDatabase.IsValidFolder(path))AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));
        foreach(string n in Names)
        {
            if(!Get(n,true))AssetDatabase.CopyAsset(MoonkovLilToonLookDev.Source+n+".mat",$"{Root}/Before/{n}.mat");
            if(!Get(n))AssetDatabase.CopyAsset($"{Root}/Before/{n}.mat",$"{Root}/Materials/{n}.mat");
        }
        if(!File.Exists(Root+"/BeforeGrade.asset"))AssetDatabase.CopyAsset(MoonkovAnimeLightingSetup.ProfilePath,Root+"/BeforeGrade.asset");
        WriteMaps();
        foreach(string n in Names)
        {
            var m=Get(n);m.CopyPropertiesFromMaterial(Get(n,true));Configure(m,n);EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);
        }
    }

    static void Configure(Material m,string n)
    {
        if(n=="Brows"||n=="Lashes")return;
        // No unlit floor or environmental reflection can expose a hidden player.
        F(m,"_AsUnlit",0);F(m,"_LightMinLimit",0);F(m,"_LightMaxLimit",1);
        F(m,"_UseEmission",0);F(m,"_UseEmission2nd",0);F(m,"_ApplyReflection",0);
        F(m,"_RimEnableLighting",1);F(m,"_RimBackfaceMask",1);F(m,"_RimShadowMask",0);
        F(m,"_RimDirStrength",1);F(m,"_RimDirRange",0);C(m,"_RimIndirColor",new Color(1,1,1,0));
        F(m,"_MatCapEnableLighting",1);F(m,"_MatCapShadowMask",1);F(m,"_BacklightReceiveShadow",1);
        F(m,"_UseReflection",1);F(m,"_ApplySpecular",1);F(m,"_SpecularToon",0);F(m,"_GSAAStrength",1);
        if(n=="Face"||n=="Body")
        {
            // Smoothness is spatially authored in RGB; alpha separately controls specular energy.
            m.SetTexture("_SmoothnessTex",Map(n+"Response"));m.SetTexture("_ReflectionColorTex",Map(n+"Response"));
            F(m,"_Smoothness",1);F(m,"_Reflectance",.04f);C(m,"_ReflectionColor",new Color(1,.97f,.94f,n=="Face"?.8f:.82f));
            F(m,"_UseRim",1);F(m,"_RimFresnelPower",n=="Face"?3.5f:2.8f);F(m,"_RimBorder",.5f);F(m,"_RimBlur",.75f);F(m,"_RimMainStrength",.2f);
            m.SetTexture("_RimColorTex",Map(n+"Rim"));C(m,"_RimColor",n=="Face"?new Color(2.5f,2,1.95f,.3f):new Color(4.4f,4.4f,4.4f,.65f));
            F(m,"_UseBacklight",1);C(m,"_BacklightColor",new Color(1,.65f,.55f,.18f));F(m,"_BacklightBlur",.8f);
            if(n=="Face"){m.SetTexture("_MatCapBlendMask",Map("FaceSheen"));C(m,"_MatCapColor",new Color(1,.98f,.96f,.22f));}
            else {F(m,"_UseMatCap",0);F(m,"_Metallic",.5f);m.SetTexture("_MetallicGlossMap",Map("BodyMetal"));}
        }
        else if(n=="Hair")
        {
            F(m,"_UseMatCap",1);F(m,"_UseAnisotropy",1);F(m,"_Anisotropy2Reflection",1);F(m,"_AnisotropyScale",.5f);
            m.SetTexture("_MatCapTex",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/DollSinger/Art/Textures/SilkHairMatCap.png"));
            F(m,"_Anisotropy2MatCap",1);F(m,"_MatCapMainStrength",.85f);C(m,"_MatCapColor",new Color(1,.98f,.95f,.3f));
            F(m,"_AnisotropyTangentWidth",1.5f);F(m,"_AnisotropyBitangentWidth",.7f);F(m,"_AnisotropySpecularStrength",.48f);
            F(m,"_Anisotropy2ndTangentWidth",2);F(m,"_Anisotropy2ndBitangentWidth",.9f);F(m,"_Anisotropy2ndSpecularStrength",.2f);
            F(m,"_Smoothness",.68f);F(m,"_Reflectance",.04f);C(m,"_ReflectionColor",new Color(.94f,.97f,1,.75f));
            F(m,"_UseRim",1);m.SetTexture("_RimColorTex",null);C(m,"_RimColor",new Color(2.1f,2.4f,2.8f,.34f));
            F(m,"_RimFresnelPower",3.3f);F(m,"_RimBorder",.55f);F(m,"_RimBlur",.65f);F(m,"_RimMainStrength",.75f);
        }
        else if(n=="Eyes")
        {
            F(m,"_Smoothness",.96f);F(m,"_Reflectance",.065f);C(m,"_ReflectionColor",new Color(1,1,1,.95f));
            C(m,"_MatCapColor",new Color(1,.98f,.96f,.42f));F(m,"_Parallax",.026f);F(m,"_UseRim",0);
            // Convex cornea normal, confined to the original iris UV island.
            // Native normal/MatCap paths; no extra transparent geometry or pass.
            F(m,"_UseBumpMap",1);F(m,"_BumpScale",.55f);m.SetTexture("_BumpMap",Map("EyeCornea"));
            F(m,"_MatCapCustomNormal",1);F(m,"_MatCapBumpScale",.75f);m.SetTexture("_MatCapBumpMap",Map("EyeCornea"));
        }
        else if(n=="EyeWhite")
        {
            F(m,"_Smoothness",.35f);F(m,"_Reflectance",.035f);C(m,"_ReflectionColor",new Color(1,1,1,.12f));F(m,"_UseRim",0);F(m,"_UseMatCap",0);
        }
        else if(n=="Glasses"||n=="Jewel")
        {
            F(m,"_Smoothness",n=="Jewel"?.94f:.87f);F(m,"_Metallic",n=="Glasses"?.65f:0);F(m,"_Reflectance",.055f);
            C(m,"_ReflectionColor",n=="Jewel"?new Color(.85f,.95f,1,.85f):new Color(1,.92f,.75f,.8f));
            C(m,"_MatCapColor",new Color(1,.98f,.95f,.38f));F(m,"_UseRim",1);C(m,"_RimColor",new Color(2,2.2f,2.5f,.25f));
            F(m,"_RimFresnelPower",4);F(m,"_RimBorder",.6f);F(m,"_RimBlur",.5f);F(m,"_RimMainStrength",.7f);
        }
        else
        {
            string atlas=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")));
            if(!Map(atlas+"Response"))throw new InvalidOperationException("Unknown clothing atlas: "+atlas);
            m.SetTexture("_SmoothnessTex",Map(atlas+"Response"));m.SetTexture("_ReflectionColorTex",Map(atlas+"Response"));
            F(m,"_Smoothness",1);F(m,"_Metallic",.58f);F(m,"_Reflectance",.04f);C(m,"_ReflectionColor",new Color(1,.97f,.91f,.72f));
            F(m,"_UseMatCap",0);F(m,"_UseRim",1);m.SetTexture("_RimColorTex",Map(atlas+"Rim"));
            C(m,"_RimColor",new Color(2.2f,2.6f,3,n=="Shoes"?.22f:.4f));F(m,"_RimMainStrength",.8f);
            F(m,"_RimFresnelPower",3.5f);F(m,"_RimBorder",.5f);F(m,"_RimBlur",.75f);
        }
    }

    [MenuItem("Tools/Moonkov/lilToon/Full Body/Apply To Game")]
    public static void Apply()=>Copy(false);
    [MenuItem("Tools/Moonkov/lilToon/Full Body/Restore Previous Look")]
    public static void Restore()=>Copy(true);
    static void RequireEdit(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");}
    static void Copy(bool before)
    {
        RequireEdit();
        foreach(string n in Names)
        foreach(var target in new[]{AssetDatabase.LoadAssetAtPath<Material>(MoonkovLilToonLookDev.Source+n+".mat"),MoonkovLilToonLookDev.Get(n)})
            if(!target||!Get(n,before)||target.shader!=Get(n,before).shader)throw new InvalidOperationException("Missing/mismatched material: "+n);
        foreach(string n in Names)
        foreach(var target in new[]{AssetDatabase.LoadAssetAtPath<Material>(MoonkovLilToonLookDev.Source+n+".mat"),MoonkovLilToonLookDev.Get(n)})
        {Undo.RecordObject(target,"Full body material look");target.CopyPropertiesFromMaterial(Get(n,before));EditorUtility.SetDirty(target);AssetDatabase.SaveAssetIfDirty(target);}
    }
    public static void SetLook(GameObject actor,bool before)
    {
        foreach(var r in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))r.sharedMaterials=r.sharedMaterials.Select(m=>m&&Names.Contains(m.name.Replace(" (Instance)",""))?Get(m.name.Replace(" (Instance)",""),before):m).ToArray();
    }
    static void Write(string name,Func<float,float,Color> sample,int size=512,bool normal=false)
    {
        var t=new Texture2D(size,size,TextureFormat.RGBA32,false,true);
        try
        {
            var pixels=new Color[size*size];for(int y=0;y<size;y++)for(int x=0;x<size;x++)pixels[y*size+x]=sample((x+.5f)/size,(y+.5f)/size);
            t.SetPixels(pixels);t.Apply();string path=$"{Root}/Maps/{name}.png";File.WriteAllBytes(path,t.EncodeToPNG());AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var i=(TextureImporter)AssetImporter.GetAtPath(path);i.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;i.sRGBTexture=false;i.mipmapEnabled=true;i.isReadable=false;i.wrapMode=TextureWrapMode.Clamp;i.alphaSource=TextureImporterAlphaSource.FromInput;i.alphaIsTransparency=false;i.textureCompression=TextureImporterCompression.CompressedHQ;i.SaveAndReimport();
        }
        finally{UnityEngine.Object.DestroyImmediate(t);}
    }
    static void WriteMaps()
    {
        Func<float,float,float> cheek=(u,v)=>Mathf.Clamp01(G(u,v,.39f,.535f,.075f,.05f)+G(u,v,.61f,.535f,.075f,.05f));
        Func<float,float,float> mouth=(u,v)=>G(u,v,.5f,.486f,.045f,.028f);
        Write("FaceResponse",(u,v)=>{float c=cheek(u,v),nose=G(u,v,.5f,.565f,.022f,.03f),forehead=G(u,v,.5f,.76f,.12f,.12f);float s=.55f+.1f*c+.13f*nose+.06f*forehead;return new Color(s,s,s,(.12f+.32f*c+.25f*nose+.12f*forehead)*(1-mouth(u,v)));});
        Write("FaceSheen",(u,v)=>Color.white*(.1f+.32f*cheek(u,v))*(1-mouth(u,v)));
        Write("FaceRim",(u,v)=>new Color(1,1,1,(.18f+.6f*cheek(u,v))*(1-mouth(u,v))));
        Write("EyeCornea",(u,v)=>
        {
            var xy=new Vector2((u-.5954f)/.08f,(v-.1468f)/.132f);float r=xy.magnitude;
            float falloff=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.75f,1.1f,r));xy*=.7f*falloff;
            float z=Mathf.Sqrt(1-Mathf.Clamp01(xy.sqrMagnitude));return new Color(xy.x*.5f+.5f,xy.y*.5f+.5f,z*.5f+.5f,1);
        },512,true);
        // Body response is deliberately broad; the old shoulder map only adds a local accent.
        string shoulderPath=AssetDatabase.GetAssetPath(MoonkovShoulderRimLookDev.Get("Body").GetTexture("_RimColorTex"));
        var shoulder=new Texture2D(2,2);shoulder.LoadImage(File.ReadAllBytes(shoulderPath));
        var body=new Texture2D(2,2);body.LoadImage(File.ReadAllBytes("Assets/DollSinger/Art/Textures/body.png"));
        Func<Color,float> skin=c=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(.015f,.055f,c.r-c.b))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.75f,.9f,c.r))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.7f,.85f,c.g));
        Func<Color,float> metal=c=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,.25f,c.r-c.b))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.3f,.5f,c.g));
        try
        {
            Write("BodyResponse",(u,v)=>{var c=body.GetPixelBilinear(u,v);float k=skin(c),g=metal(c),accent=shoulder.GetPixelBilinear(u,v).a,s=Mathf.Lerp(Mathf.Lerp(.42f,.6f+.07f*accent,k),.85f,g);return new Color(s,s,s,Mathf.Lerp(Mathf.Lerp(.2f,.42f+.08f*accent,k),.8f,g));});
            Write("BodyRim",(u,v)=>{var c=body.GetPixelBilinear(u,v);float k=skin(c),blue=Mathf.Clamp01((c.b-c.r)*3);var tint=Color.Lerp(Color.Lerp(new Color(.85f,.9f,1),new Color(.2f,.4f,1),blue),new Color(1,.78f,.76f),k);tint.a=Mathf.Lerp(.1f,.36f,k)+.64f*shoulder.GetPixelBilinear(u,v).a*k;return tint;});
            Write("BodyMetal",(u,v)=>Color.white*metal(body.GetPixelBilinear(u,v)));
        }
        finally{UnityEngine.Object.DestroyImmediate(shoulder);UnityEngine.Object.DestroyImmediate(body);}
        foreach(string atlas in new[]{"yifu","yifu2","xiezi"})
        {
            var source=new Texture2D(2,2);source.LoadImage(File.ReadAllBytes($"Assets/DollSinger/Art/Textures/{atlas}.png"));
            Func<Color,float> gold=c=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(.07f,.24f,c.r-c.b))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,.4f,c.g));
            try
            {
                Write(atlas+"Response",(u,v)=>{var c=source.GetPixelBilinear(u,v);float g=gold(c),blue=Mathf.Clamp01((c.b-c.r)*3);float s=Mathf.Lerp(atlas=="xiezi"?.34f:Mathf.Lerp(.4f,.53f,blue),.88f,g);return new Color(s,s,s,Mathf.Lerp(Mathf.Lerp(.22f,.38f,blue),.88f,g));});
                Write(atlas+"Rim",(u,v)=>{var c=source.GetPixelBilinear(u,v);float g=gold(c);return new Color(1,1,1,Mathf.Lerp(.28f+.35f*Mathf.Clamp01((c.b-c.r)*3),.15f,g));});
            }
            finally{UnityEngine.Object.DestroyImmediate(source);}
        }
    }
}
