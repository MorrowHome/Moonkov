using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class EnergyShieldBudgetChecks
{
    [MenuItem("Tools/Moonkov/Performance/Check remote shield budget")]
    public static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit mode.");
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var type=typeof(EnergyShieldPresentation);
        var tick=type.GetMethod("SimulatePresentation",flags);
        int checks=0;
        void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
        foreach(var path in new[]{"Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab",
            "Assets/HatsuneMiku/Prefabs/HatsuneMikuNetworkPlayer.prefab","Assets/Shian/Prefabs/ShianNetworkPlayer.prefab"})
        {
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),scene);
                var model=root.GetComponentInChildren<DollSingerMovement>(true);
                var body=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var meshes=body.Select(r=>r.sharedMesh).ToArray();
                var colliders=root.GetComponentsInChildren<Collider>(true);
                var enabled=colliders.Select(c=>c.enabled).ToArray();
                var view=model.GetComponent<EnergyShieldPresentation>()??model.gameObject.AddComponent<EnergyShieldPresentation>();
                view.PresentationAudioEnabled=false;
                for(int repeat=0;repeat<2;repeat++)
                {
                    view.ConfigureHardwareBudget(true);view.ResetPresentation();
                    var state=new PredictedPlayerGhost{CurrentHealth=440,HeadDomeEquipped=true,HaloEquipped=true,
                        HaloEnergyInitialized=true,HaloEnergy=100,HaloCapacity=100,SubspaceBackpack=2};
                    view.Present(state,false,false,null);tick.Invoke(view,null);
                    var sphere=model.GetComponentInChildren<HaloDefenseSphere>(true);
                    var hardware=(List<Renderer>)type.GetField("m_Hardware",flags).GetValue(view);
                    Check(sphere && !sphere.Surface && hardware.Count==0,"Idle defense allocated geometry or decorative hardware");
                    state.HaloDefending=true;
                    view.Present(state,false,false,null);
                    for(int frame=0;frame<16;frame++)tick.Invoke(view,null);
                    Check(sphere.Surface && sphere.Surface.enabled && sphere.Deployment==1 && sphere.Surface.sharedMaterials.Length==1,
                        "Active remote defense must open with one shared material batch");
                    state.ShieldHitSequence=1;state.ShieldHitStrength=.7f;
                    view.Present(state,false,false,null);tick.Invoke(view,null);
                    Check(sphere.Surface.enabled,"Active defense disappeared on impact");
                    view.ResetPresentation();state.ShieldHitSequence=0;state.HaloDefending=false;state.HaloRegenerating=true;
                    view.Present(state,false,false,null);tick.Invoke(view,null);
                    Check(!sphere.Surface.enabled,"Regeneration must not activate passive defense");
                    view.ConfigureHardwareBudget(false);view.Present(state,true,true,null);tick.Invoke(view,null);
                    hardware=(List<Renderer>)type.GetField("m_Hardware",flags).GetValue(view);
                    var collar=(Transform)type.GetField("m_Collar",flags).GetValue(view);
                    Check(hardware.Count>10 && collar && collar.gameObject.activeSelf,"Owner/full hardware was not restored");
                    Check(body.Select(r=>r.sharedMesh).SequenceEqual(meshes),"Body skin geometry changed");
                    Check(root.GetComponentsInChildren<Collider>(true).SequenceEqual(colliders) && colliders.Select(c=>c.enabled).SequenceEqual(enabled),"Gameplay colliders changed");
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        return $"Halo defense budget: PASS ({checks} checks, 3 avatars, lazy geometry, active defense and full hardware restoration).";
    }
}
