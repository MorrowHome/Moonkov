using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.Rendering.Universal;

// Native editable mesh source for the field items. Uses the existing Editor only.
public static class FieldExpansionAssets
{
    [Serializable] public sealed class Entry { public string code,name,zh,description,descriptionZh; }
    [Serializable] public sealed class Manifest { public Entry[] items; }
    const string Root="Assets/Resources/Moonkov/Items/";
    public static string Build() => BuildItems(null);
    public static string BuildRegeneration() => BuildItems("regeneration_shot");
    static string BuildItems(string only)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit mode.");
        // Pipeline scripts live in an in-memory assembly; Unity serialization cannot instantiate their nested records.
        var json=Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json",true);
        var manifest=(Manifest)json.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)}).Invoke(null,
            new object[]{File.ReadAllText("Tools/Art/field_expansion.json"),typeof(Manifest)});
        var entries=manifest.items.Where(e=>only==null || e.code==only).ToArray();
        if(entries.Length==0)throw new InvalidOperationException("No matching item.");
        var scene=EditorSceneManager.NewPreviewScene();
        var temporary=new List<Mesh>();
        var iconMaterials=new List<Material>();
        var materials=new[]{"dark","steel","ceramic","blue","green","red","amber"}
            .Select(n=>AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/Item_"+n+".mat")).ToArray();
        if(materials.Any(m=>!m))throw new InvalidOperationException("Missing shared item material.");
        RenderTexture target=null;Texture2D sheet=null;
        var originalQualityPipeline=QualitySettings.renderPipeline;
        var previewPipeline=UnityEngine.Object.Instantiate((UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline);
        ScriptableRendererData previewRenderer=null;
        try
        {
            // Inventory icons use an isolated in-memory renderer, without gameplay depth/stencil passes.
            var settings=new SerializedObject(previewPipeline);var renderers=settings.FindProperty("m_RendererDataList");
            previewRenderer=UnityEngine.Object.Instantiate((ScriptableRendererData)renderers.GetArrayElementAtIndex(0).objectReferenceValue);
            previewRenderer.rendererFeatures.Clear();renderers.arraySize=1;renderers.GetArrayElementAtIndex(0).objectReferenceValue=previewRenderer;
            settings.FindProperty("m_DefaultRendererIndex").intValue=0;settings.ApplyModifiedPropertiesWithoutUndo();
            QualitySettings.renderPipeline=previewPipeline;
            var cameraNode=new GameObject("Field item icon camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraNode,scene);
            var camera=cameraNode.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.cullingMask=1<<5;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            camera.orthographic=true;camera.nearClipPlane=.001f;camera.farClipPlane=10;
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.volumeLayerMask=0;
            var lamp=new GameObject("Icon key");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lamp,scene);
            var light=lamp.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2.6f;light.cullingMask=1<<5;light.shadows=LightShadows.None;
            lamp.transform.rotation=Quaternion.Euler(35,-25,0);
            target=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
            int columns=Math.Min(5,entries.Length),rows=(entries.Length+columns-1)/columns;
            sheet=new Texture2D(columns*512,rows*512,TextureFormat.RGBA32,false);
            int index=0,triangles=0;
            foreach(var entry in entries)
            {
                var root=new GameObject(entry.code);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
                var pieces=new List<GameObject>();
                void Part(PrimitiveType type,Vector3 position,Vector3 scale,int mat,Vector3 rotation=default)
                {
                    var go=GameObject.CreatePrimitive(type);go.transform.SetParent(root.transform,false);
                    go.transform.localPosition=position;go.transform.localScale=scale;go.transform.localRotation=Quaternion.Euler(rotation);
                    go.GetComponent<Renderer>().sharedMaterial=materials[mat];UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());pieces.Add(go);
                }
                void Box(float x,float y,float z,float w,float h,float d,int m)=>Part(PrimitiveType.Cube,new(x,y,z),new(w,h,d),m);
                void Can(float x,float y,float z,float radius,float height,int m,Vector3 rotation=default)=>Part(PrimitiveType.Cylinder,new(x,y,z),new(radius*2,height/2,radius*2),m,rotation);
                void Cross(float y,float z,float size,int m)
                {Box(0,y,z,size,size*.27f,.002f,m);Box(0,y,z-.001f,size*.27f,size,.002f,m);}
                void Panel(float y,float w,float h,float z,int m)=>Box(0,y,z,w,h,.004f,m);
                switch(entry.code)
                {
                    case "regeneration_shot":
                        Can(0,0,0,.018f,.105f,2);Can(0,.057f,0,.02f,.01f,3);Can(0,-.057f,0,.02f,.01f,3);
                        Can(0,.075f,0,.01f,.026f,1);Box(0,.093f,0,.047f,.009f,.022f,3);
                        Can(0,-.081f,0,.01f,.036f,0);Panel(.003f,.021f,.062f,-.019f,3);
                        Cross(.006f,-.022f,.014f,2);Box(0,-.018f,-.022f,.012f,.003f,.002f,2);break;
                    case "oxygen_recycler":
                        Box(0,0,0,.13f,.14f,.045f,0);Panel(0,.112f,.125f,-.026f,2);
                        Can(-.032f,.005f,-.045f,.023f,.11f,3);Can(.032f,.005f,-.045f,.023f,.11f,1);
                        Box(0,.061f,-.041f,.08f,.012f,.018f,0);Box(0,-.052f,-.041f,.08f,.012f,.018f,0);
                        for(int n=0;n<4;n++)Box(-.032f,-.03f+n*.018f,-.069f,.028f,.004f,.003f,2);
                        Box(.045f,.083f,0,.025f,.026f,.023f,1);break;
                    case "glare_filter":
                        Box(0,0,0,.15f,.06f,.024f,0);Box(-.038f,0,-.014f,.063f,.044f,.008f,6);Box(.038f,0,-.014f,.063f,.044f,.008f,6);
                        Box(0,0,-.02f,.012f,.028f,.015f,1);Box(-.078f,0,.029f,.008f,.045f,.072f,0);Box(.078f,0,.029f,.008f,.045f,.072f,0);
                        Box(0,0,.066f,.15f,.018f,.008f,1);break;
                    case "survey_multitool":
                        Box(0,0,0,.11f,.14f,.04f,0);Panel(.012f,.088f,.083f,-.024f,1);Panel(.012f,.076f,.071f,-.028f,3);
                        Box(0,.012f,-.031f,.043f,.002f,.002f,2);Box(0,.012f,-.032f,.002f,.043f,.002f,2);
                        for(int n=0;n<3;n++)Box(-.03f+n*.03f,-.047f,-.025f,.018f,.014f,.008f,n==1?6:1);
                        Can(0,.083f,0,.03f,.028f,1);Can(0,.099f,0,.022f,.005f,3);break;
                    case "hemostatic_bandage":
                        Can(0,0,0,.045f,.075f,2,new(90,0,0));Can(0,0,-.039f,.017f,.003f,0,new(90,0,0));
                        Box(.039f,-.018f,0,.025f,.07f,.077f,2);Box(.039f,-.018f,-.04f,.024f,.025f,.003f,5);
                        Box(0,.039f,0,.075f,.014f,.08f,4);break;
                    case "rescue_kit":
                        Box(0,0,0,.21f,.14f,.075f,4);Box(0,.075f,0,.115f,.012f,.03f,0);
                        Box(-.065f,0,-.039f,.012f,.14f,.009f,0);Box(.065f,0,-.039f,.012f,.14f,.009f,0);
                        Panel(0,.075f,.075f,-.041f,2);Cross(0,-.045f,.048f,5);Box(0,-.053f,-.04f,.04f,.008f,.006f,1);break;
                    case "hydration_gel":
                        Box(0,0,0,.068f,.105f,.023f,3);Box(0,-.055f,0,.075f,.008f,.027f,1);
                        Box(0,.055f,0,.075f,.008f,.027f,1);Box(.02f,.065f,0,.017f,.018f,.02f,2);
                        Panel(-.005f,.041f,.055f,-.014f,2);Can(0,.003f,-.018f,.012f,.003f,3,new(90,0,0));
                        Box(0,-.021f,-.017f,.023f,.003f,.002f,3);break;
                    case "compressed_ration":
                        Box(0,0,0,.087f,.18f,.034f,6);Box(0,.09f,0,.093f,.008f,.038f,1);Box(0,-.09f,0,.093f,.008f,.038f,1);
                        Panel(.012f,.071f,.086f,-.02f,2);for(int n=0;n<3;n++)Box(0,.031f-n*.021f,-.023f,.048f,.008f,.003f,0);
                        Box(0,-.062f,-.02f,.05f,.013f,.006f,0);break;
                    case "emergency_oxygen":
                        Can(0,0,0,.031f,.105f,3);Can(0,.0585f,0,.033f,.012f,1);Can(0,-.0585f,0,.033f,.012f,1);
                        Can(0,.077f,0,.012f,.025f,0);Box(0,.095f,0,.045f,.012f,.016f,1);
                        Box(0,.005f,-.032f,.026f,.051f,.003f,2);Box(0,.005f,-.035f,.014f,.029f,.002f,3);break;
                    case "flash_grenade":
                        Can(0,0,0,.032f,.12f,1);Can(0,.066f,0,.027f,.014f,0);Can(0,-.059f,0,.035f,.008f,0);
                        for(int n=0;n<6;n++)
                        {float a=n*Mathf.PI/3;Part(PrimitiveType.Cube,new(Mathf.Sin(a)*.033f,0,Mathf.Cos(a)*.033f),new(.016f,.075f,.005f),0,new(0,n*60,0));}
                        Can(0,.041f,0,.034f,.01f,6);Can(0,-.041f,0,.034f,.01f,6);Box(.038f,.018f,0,.008f,.115f,.025f,1);break;
                    case "poison_bomb":
                        Can(0,0,0,.041f,.115f,4);Can(0,.064f,0,.043f,.013f,0);Can(0,-.064f,0,.043f,.013f,1);
                        Can(0,.0825f,0,.023f,.024f,1);Box(.045f,.015f,0,.008f,.133f,.024f,1);
                        Panel(0,.048f,.055f,-.043f,6);Can(0,.003f,-.047f,.013f,.003f,0,new(90,0,0));
                        Box(0,-.017f,-.047f,.025f,.003f,.002f,0);break;
                    default:throw new InvalidOperationException(entry.code);
                }
                var used=materials.Where(m=>pieces.Any(p=>p.GetComponent<Renderer>().sharedMaterial==m)).ToArray();
                var groups=new List<Mesh>();
                foreach(var material in used)
                {
                    var mesh=new Mesh();mesh.CombineMeshes(pieces.Where(p=>p.GetComponent<Renderer>().sharedMaterial==material)
                        .Select(p=>new CombineInstance{mesh=p.GetComponent<MeshFilter>().sharedMesh,transform=p.transform.localToWorldMatrix}).ToArray(),true,true);
                    groups.Add(mesh);temporary.Add(mesh);
                }
                var combined=new Mesh{name=entry.name};combined.CombineMeshes(groups.Select(m=>new CombineInstance{mesh=m,transform=Matrix4x4.identity}).ToArray(),false,false);
                string meshPath=Root+"Models/"+entry.code+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if(saved){EditorUtility.CopySerialized(combined,saved);UnityEngine.Object.DestroyImmediate(combined);}
                else{saved=combined;AssetDatabase.CreateAsset(saved,meshPath);}
                EditorUtility.SetDirty(saved);AssetDatabase.SaveAssetIfDirty(saved);
                foreach(var go in pieces)UnityEngine.Object.DestroyImmediate(go);
                root.AddComponent<MeshFilter>().sharedMesh=saved;root.AddComponent<MeshRenderer>().sharedMaterials=used;
                PrefabUtility.SaveAsPrefabAsset(root,Root+"Prefabs/"+entry.code+".prefab");root.layer=5;
                if(entry.code=="regeneration_shot")
                {
                    // Reuse the tested injection geometry, anchors and moving parts. No animation or source mesh rebake.
                    var source=Resources.Load<GameObject>("Moonkov/Actions/Props/vitality_shot");
                    if(!source)throw new InvalidOperationException("Missing authored injector action prop.");
                    var action=UnityEngine.Object.Instantiate(source);action.name=entry.code;
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(action,scene);
                    foreach(var part in action.GetComponentsInChildren<Renderer>(true))
                        part.sharedMaterials=part.sharedMaterials.Select(m=>m==materials[5]?materials[3]:m).ToArray();
                    PrefabUtility.SaveAsPrefabAsset(action,"Assets/Resources/Moonkov/Actions/Props/"+entry.code+".prefab");
                    UnityEngine.Object.DestroyImmediate(action);
                }
                // Tiny inventory previews must not sample the open level's shadow cascades.
                var renderer=root.GetComponent<MeshRenderer>();renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
                renderer.sharedMaterials=used.Select(material=>
                {
                    var iconMaterial=new Material(material);iconMaterial.SetFloat("_ReceiveShadows",0);iconMaterial.EnableKeyword("_RECEIVE_SHADOWS_OFF");
                    iconMaterials.Add(iconMaterial);return iconMaterial;
                }).ToArray();
                var bounds=saved.bounds;float extent=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
                camera.orthographicSize=extent*.72f;camera.transform.position=bounds.center+new Vector3(.55f,.38f,-1)*extent*3;
                camera.transform.LookAt(bounds.center);camera.Render();
                var previous=RenderTexture.active;var icon=new Texture2D(512,512,TextureFormat.RGBA32,false);
                string iconPath=Root+"Icons/"+entry.code+".png";
                try
                {
                    RenderTexture.active=target;icon.ReadPixels(new Rect(0,0,512,512),0,0);icon.Apply();
                    var pixels=icon.GetPixels();int visible=pixels.Count(p=>p.a>.1f);
                    if(visible<4000 || visible>240000)throw new InvalidOperationException("Bad icon framing: "+entry.code+" "+visible);
                    File.WriteAllBytes(iconPath,icon.EncodeToPNG());sheet.SetPixels((index%columns)*512,(rows-1-index/columns)*512,512,512,pixels);
                }
                finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(icon);}
                AssetDatabase.ImportAsset(iconPath,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(iconPath);importer.textureType=TextureImporterType.Default;
                importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.maxTextureSize=512;
                importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
                if(saved.vertexCount==0 || saved.bounds.size.magnitude>1 || root.GetComponentsInChildren<Collider>().Length!=0)
                    throw new InvalidOperationException("Invalid physical presentation: "+entry.code);
                triangles+=saved.triangles.Length/3;index++;UnityEngine.Object.DestroyImmediate(root);
            }
            string output=only==null?"items":"regeneration";
            sheet.Apply();Directory.CreateDirectory("LocalData/FieldExpansion");File.WriteAllBytes("LocalData/FieldExpansion/"+output+".png",sheet.EncodeToPNG());
            var collection=LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
            foreach(var entry in entries)
                foreach(var reference in collection.Tables)
                {
                    var table=(StringTable)reference.asset;bool zh=table.LocaleIdentifier.Code=="zh-Hans";
                    table.AddEntry(entry.name,zh?entry.zh:entry.name);table.AddEntry(entry.description,zh?entry.descriptionZh:entry.description);
                    if(entry.code=="regeneration_shot")
                    {
                        table.AddEntry("REGEN {0:0}s",zh?"再生 {0:0} 秒":"REGEN {0:0}s");
                        table.AddEntry("REGEN PAUSED {0:0}s",zh?"再生暂停 {0:0} 秒":"REGEN PAUSED {0:0}s");
                    }
                    EditorUtility.SetDirty(table);AssetDatabase.SaveAssetIfDirty(table);
                }
            EditorUtility.SetDirty(collection.SharedData);AssetDatabase.SaveAssetIfDirty(collection.SharedData);
            string result=$"{index} static models and transparent icons; {triangles} triangles total. Preview: LocalData/FieldExpansion/{output}.png";
            File.WriteAllText("LocalData/FieldExpansion/"+(only==null?"assets-review":"regeneration-assets-review")+".txt",result);return result;
        }
        finally
        {
            if(target){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(sheet)UnityEngine.Object.DestroyImmediate(sheet);
            foreach(var mesh in temporary)UnityEngine.Object.DestroyImmediate(mesh);
            foreach(var material in iconMaterials)UnityEngine.Object.DestroyImmediate(material);
            EditorSceneManager.ClosePreviewScene(scene);
            QualitySettings.renderPipeline=originalQualityPipeline;
            if(previewPipeline)UnityEngine.Object.DestroyImmediate(previewPipeline);
            if(previewRenderer)UnityEngine.Object.DestroyImmediate(previewRenderer);
        }
    }
}
