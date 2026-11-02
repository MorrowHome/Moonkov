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
                    var state=new PredictedPlayerGhost{CurrentHealth=440,HeadDomeEquipped=true,ShieldEquipped=true,
                        ShieldCharge=100,ShieldEnergy=100,SubspaceBackpack=2};
                    view.Present(state,false,false,null);tick.Invoke(view,null);
                    var shell=(MeshRenderer)type.GetField("m_ShellRenderer",flags).GetValue(view);
                    var hardware=(List<Renderer>)type.GetField("m_Hardware",flags).GetValue(view);
                    Check(shell && !shell.enabled && hardware.Count==0,"Remote hardware was built or idle shield is visible");
                    state.ShieldHitSequence=1;state.ShieldHitStrength=.7f;
                    view.Present(state,false,false,null);tick.Invoke(view,null);
                    Check(shell.enabled,"Remote shield hit cue disappeared");
                    view.ResetPresentation();state.ShieldHitSequence=0;state.ShieldRegenerating=true;
                    view.Present(state,false,false,null);tick.Invoke(view,null);
                    Check(shell.enabled,"Remote shield regeneration cue disappeared");
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
        return $"Remote shield budget: PASS ({checks} checks, 3 avatars, hit/regeneration cues and full hardware restoration).";
    }
}
