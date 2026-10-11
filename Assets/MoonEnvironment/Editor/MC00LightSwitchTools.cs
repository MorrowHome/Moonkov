using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Moon.Preview;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Author v2 mapping adapter plus a separate, explicitly temporary local panel.</summary>
    public static class MC00LightSwitchTools
    {
        public const string Folder = MC00GameExportTools.Folder + "/LightingTrial";
        public const string ScenePath = MC00GameExportTools.Folder + "/Scenes/MC00LightSwitchPreview.unity";
        public const string A = "SRC_5328cb8663556e3b29dc", B = "SRC_e6365998da3f1684fc26";
        [Serializable] public sealed class Mapping
        {
            public int schema_version;
            public string asset_id, asset_revision;
            public bool example_only, physical_buttons_exist;
            public Lamp[] lights;
            public Switch[] switches;
        }
        [Serializable] public sealed class Lamp
        {
            public string target_light_id, anchor_object, anchor_output_id, emissive_resolution_status;
            public bool default_on;
            public Emission[] emissive_targets;
        }
        [Serializable] public sealed class Emission { public string renderer_object, material_name; }
        [Serializable] public sealed class Switch
        {
            public string switch_id, button_object, action, label;
            public string indicator_object, indicator_material_name, interaction_anchor;
            public string[] target_light_ids;
        }

        [MenuItem("Tools/Moon Environment/MC00 Game Export/4 - Create separate light switch trial")]
        public static void CreateTrial()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your current scene before creating a trial.");
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Trial already exists. Open it; do not overwrite authored work.");
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            if (!AssetDatabase.CopyAsset(MC00GameExportTools.PreviewPath, ScenePath)) throw new IOException("Could not copy preview.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var roots = scene.GetRootGameObjects();
            var model = roots.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="MC00_Render");
            var budget = model.GetComponentInParent<LocalLightBudget>();
            var input = roots.SelectMany(r=>r.GetComponentsInChildren<DollSingerInput>(true)).Single();
            var camera = roots.SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).Single(c=>c.enabled);
            var panel = new GameObject("LOCAL TRIAL - replace with authored MC00 panel");
            panel.transform.SetPositionAndRotation(input.transform.position + input.transform.forward * 2 + Vector3.up * 1.25f,
                Quaternion.LookRotation(input.transform.forward, Vector3.up));
            var housing = MakeMaterial("Panel",new Color(.035f,.055f,.065f),false);
            var key = MakeMaterial("Button",new Color(.14f,.19f,.21f),false);
            var on = MakeMaterial("IndicatorOn",new Color(.15f,.65f,.32f),true);
            var off = MakeMaterial("IndicatorOff",new Color(.12f,.15f,.17f),true);
            var mixed = MakeMaterial("IndicatorMixed",new Color(.9f,.52f,.08f),true);
            var unavailable = MakeMaterial("IndicatorUnavailable",new Color(.65f,.12f,.12f),true);
            Cube("Housing", panel.transform, Vector3.zero, new Vector3(1.4f,.88f,.08f), housing);
            var mapping = TrialMapping();
            for(int i=0;i<mapping.switches.Length;i++)
            {
                var item = mapping.switches[i];
                var button = Cube(item.button_object,panel.transform,new Vector3(i%2==0?-.34f:.34f,i<2?.18f:-.18f,-.09f),new Vector3(.59f,.29f,.08f),key);
                // Keep root at unit scale so authored labels and click bounds use metres.
                var label = new GameObject("Label"); label.transform.SetParent(button.transform,false);
                label.transform.localPosition = new Vector3(0,0,-.052f);
                var text=label.AddComponent<TextMesh>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize=48;text.characterSize=.028f;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.color=Color.white;
                label.GetComponent<MeshRenderer>().sharedMaterial=text.font.material;
                var indicator=Cube("Status",button.transform,new Vector3(.25f,.10f,-.055f),new Vector3(.05f,.025f,.02f),off);
                UnityEngine.Object.DestroyImmediate(indicator.GetComponent<Collider>());
                button.AddComponent<LightSwitchButton>();
            }
            var controls = Bind(mapping,model,panel.transform,budget);
            foreach(var item in mapping.switches)
            {
                var button = panel.GetComponentsInChildren<LightSwitchButton>().Single(b=>b.name==item.button_object);
                button.Configure(item.switch_id,item.label,controls,item.target_light_ids,(LightCommand)Enum.Parse(typeof(LightCommand),item.action),
                    button.GetComponentInChildren<TextMesh>(),button.transform.Find("Status").GetComponent<Renderer>(),on,off,mixed,unavailable);
            }
            panel.AddComponent<LightSwitchPreviewInteractor>().Configure(camera,input);
            File.WriteAllText(Folder+"/LocalTrialMapping.v2.json",JsonUtility.ToJson(mapping,true));
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Selection.activeGameObject=panel;
            Debug.Log("Light switch trial ready: "+ScenePath+". Stand at the original spawn and aim at the panel; press F.");
        }

        // Caller supplies the exact building and panel scope. Mapping names are authoring
        // keys only; the runtime uses direct references and stable light IDs.
        public static LightCircuitControls Bind(Mapping map, Transform model, Transform panel, LocalLightBudget budget,
            string sourceFolder = MC00GameExportTools.Source, string materialFolder = Folder)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Author bindings outside Play.");
            if(map==null||map.schema_version!=2||map.asset_id!="MC00"||map.example_only||!map.physical_buttons_exist||map.lights==null||map.switches==null||!budget||!model||!panel)
                throw new InvalidOperationException("Require a physical v2 mapping, model and budget; examples cannot be installed.");
            if(panel.GetComponent<LightCircuitControls>()) throw new InvalidOperationException("Panel already bound.");
            var manifest=JsonUtility.FromJson<MC00GameExportTools.Manifest>(File.ReadAllText(sourceFolder+"/unity-manifest.json"));
            var authorLights=manifest.lights.ToDictionary(l=>l.source_id);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var fixtureIds = new HashSet<Light>();
            var emissionSlots = new HashSet<(Renderer,int)>();
            var lamps = new List<(Lamp data,Light light,Emission dataEmission,Renderer renderer)>();
            var bindings = new List<LightCircuitControls.Binding>();
            var nodes = model.GetComponentsInChildren<Transform>(true);
            var buttonNodes = panel.GetComponentsInChildren<Transform>(true);
            var switchIds = new HashSet<string>(); var buttonNames = new HashSet<string>();
            // Resolve all external keys before modifying scene objects.
            foreach(var l in map.lights)
            {
                if(string.IsNullOrWhiteSpace(l.target_light_id)||!ids.Add(l.target_light_id)) throw new InvalidOperationException("Duplicate/empty light ID.");
                if(!authorLights.TryGetValue(l.target_light_id,out var author)||author.source_name!=l.anchor_object||author.anchor_output_id!=l.anchor_output_id)
                    throw new InvalidOperationException("Light identity does not match source manifest: "+l.target_light_id);
                var anchor = nodes.Single(t=>t.name==l.anchor_object);
                var light = anchor.GetComponentsInChildren<Light>(true).Single();
                if(!fixtureIds.Add(light)||!budget.TryGetPowered(light,out _))throw new InvalidOperationException("Duplicate light or outside budget: "+l.anchor_object);
                var binding = new LightCircuitControls.Binding {lightId=l.target_light_id,fixture=light};
                bindings.Add(binding);
                foreach(var e in l.emissive_targets??Array.Empty<Emission>())
                {
                    var renderer = nodes.Single(t=>t.name==e.renderer_object).GetComponent<Renderer>();
                    if(!renderer||renderer.sharedMaterials.Count(m=>m&&m.name==e.material_name)!=1)
                        throw new InvalidOperationException("Emission material must resolve exactly once: "+e.renderer_object+" / "+e.material_name);
                    if(!emissionSlots.Add((renderer,Array.FindIndex(renderer.sharedMaterials,m=>m&&m.name==e.material_name))))
                        throw new InvalidOperationException("Two lights cannot own the same emission material slot.");
                    lamps.Add((l,light,e,renderer));
                }
            }
            foreach(var s in map.switches)
            {
                if(string.IsNullOrWhiteSpace(s.switch_id)||!switchIds.Add(s.switch_id)||!buttonNames.Add(s.button_object)||
                    s.target_light_ids==null||s.target_light_ids.Length==0||s.target_light_ids.Any(id=>!ids.Contains(id))||
                    !Enum.TryParse(s.action,out LightCommand action)||!Enum.IsDefined(typeof(LightCommand),action))
                    throw new InvalidOperationException("Invalid switch mapping: "+s.switch_id);
                var node=buttonNodes.Single(t=>t.name==s.button_object);
                if(!node.GetComponentInChildren<Collider>())throw new InvalidOperationException("Button needs an authored click collider: "+s.button_object);
            }
            foreach(var binding in bindings)
            {
                var emissions = new List<LightCircuitControls.EmissionTarget>();
                foreach(var item in lamps.Where(x=>x.light==binding.fixture))
                {
                    var slots=item.renderer.sharedMaterials;
                    int slot=Array.FindIndex(slots,m=>m&&m.name==item.dataEmission.material_name);
                    var original=slots[slot];
                    var on=EmissionMaterial(original,true,materialFolder);var off=EmissionMaterial(original,false,materialFolder);
                    var clusters=item.renderer.GetComponentInParent<BuildingRenderClusters>();
                    if(clusters)clusters.ReleaseFor(item.renderer);
                    // URP's material validator requires an emissive GI flag for the
                    // visible emission keyword. These switchable surfaces must NOT
                    // contribute baked light that would survive switching them off.
                    GameObjectUtility.SetStaticEditorFlags(item.renderer.gameObject,
                        GameObjectUtility.GetStaticEditorFlags(item.renderer.gameObject)&~StaticEditorFlags.ContributeGI);
                    slots[slot]=on;item.renderer.sharedMaterials=slots;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(item.renderer);
                    emissions.Add(new LightCircuitControls.EmissionTarget {renderer=item.renderer,materialSlot=slot,onMaterial=on,offMaterial=off});
                }
                binding.emissiveTargets=emissions.ToArray();
                binding.fixture.enabled=map.lights.Single(l=>l.target_light_id==binding.lightId).default_on;
                budget.SetPowered(binding.fixture,binding.fixture.enabled);
                PrefabUtility.RecordPrefabInstancePropertyModifications(binding.fixture);
            }
            var controls=panel.gameObject.AddComponent<LightCircuitControls>();controls.Configure(budget,bindings.ToArray());
            if(!string.IsNullOrEmpty(controls.ConfigurationError))throw new InvalidOperationException(controls.ConfigurationError);
            foreach(var s in map.switches)
            {
                var node=buttonNodes.Single(t=>t.name==s.button_object);
                var button=node.GetComponent<LightSwitchButton>()??node.gameObject.AddComponent<LightSwitchButton>();
                button.Configure(s.switch_id,string.IsNullOrEmpty(s.label)?s.switch_id:s.label,controls,s.target_light_ids,(LightCommand)Enum.Parse(typeof(LightCommand),s.action));
            }
            return controls;
        }

        static Mapping TrialMapping()=>new Mapping {
            schema_version=2,asset_id="MC00",asset_revision="local-lighting-trial-1",physical_buttons_exist=true,
            lights=new[]{
                new Lamp{target_light_id=A,anchor_object="MC106_Reading_Bar_Actual_Light",anchor_output_id="NODE_5328cb8663556e3b29dc",default_on=true,emissive_resolution_status="resolved_local_trial",
                    emissive_targets=new[]{new Emission{renderer_object="MC106_Lamp_opal_diffuser",material_name="MC106_Reading_Diffuser"}}},
                new Lamp{target_light_id=B,anchor_object="MC108_Task_Bar_Actual_Light",anchor_output_id="NODE_e6365998da3f1684fc26",default_on=true,emissive_resolution_status="resolved_local_trial",
                    emissive_targets=new[]{new Emission{renderer_object="MC108_Task_Bar_Lens",material_name="MC108_Task_Diffuser"}}}},
            switches=new[]{
                new Switch{switch_id="LOCAL_A",button_object="LOCAL_A",label="READING",target_light_ids=new[]{A},action="Toggle"},
                new Switch{switch_id="LOCAL_BATCH",button_object="LOCAL_BATCH",label="BOTH / TOGGLE",target_light_ids=new[]{A,B},action="Toggle"},
                new Switch{switch_id="LOCAL_ON",button_object="LOCAL_ON",label="BOTH / ON",target_light_ids=new[]{A,B},action="SetOn"},
                new Switch{switch_id="LOCAL_OFF",button_object="LOCAL_OFF",label="BOTH / OFF",target_light_ids=new[]{A,B},action="SetOff"}}
        };
        static Material EmissionMaterial(Material original,bool on,string materialFolder)
        {
            string path=materialFolder+"/"+original.name.Replace('/','_')+(on?"_On":"_Off")+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(material)return material;
            material=new Material(original){name=original.name+(on?"_On":"_Off"),globalIlluminationFlags=on?MaterialGlobalIlluminationFlags.BakedEmissive:MaterialGlobalIlluminationFlags.None};
            if(on)material.EnableKeyword("_EMISSION");else{material.SetColor("_EmissionColor",Color.black);material.DisableKeyword("_EMISSION");}
            AssetDatabase.CreateAsset(material,path);return material;
        }
        static Material MakeMaterial(string name,Color color,bool emission)
        {
            string path=Folder+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(material)return material;
            material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,enableInstancing=true};
            material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",.25f);
            if(emission){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",color*.7f);material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;}
            AssetDatabase.CreateAsset(material,path);return material;
        }
        static GameObject Cube(string name,Transform parent,Vector3 position,Vector3 size,Material material)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;
            // Geometry child can scale; interactive root and label coordinates stay in metres.
            var visual=GameObject.CreatePrimitive(PrimitiveType.Cube);visual.name="Visual";visual.transform.SetParent(go.transform,false);visual.transform.localScale=size;
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
            var mesh=UnityEngine.Object.Instantiate(visual.GetComponent<MeshFilter>().sharedMesh);
            var vertices=mesh.vertices;for(int i=0;i<vertices.Length;i++)vertices[i]=Vector3.Scale(vertices[i],size);mesh.vertices=vertices;mesh.RecalculateBounds();
            string meshPath=Folder+"/"+name+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(existing){UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,meshPath);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<BoxCollider>().size=size;
            UnityEngine.Object.DestroyImmediate(visual);return go;
        }
    }
}
