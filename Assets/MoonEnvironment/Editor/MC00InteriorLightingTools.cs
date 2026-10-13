using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Scene-owned lighting for the replaceable MC00 model; never edits its source prefab.</summary>
    public static class MC00InteriorLightingTools
    {
        // Same formal map as Apollo12GameSetup, which lives in Assembly-CSharp-Editor.
        private const string ScenePath = "Assets/MoonEnvironment/Variants/Apollo12Base/Scenes/Apollo12GameScene.unity";

        [MenuItem("Tools/Moon Environment/Apollo 12/Apply interior lighting")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play before configuring interior lighting.");
            var previous = SceneManager.GetActiveScene();
            var map = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !map.IsValid() || !map.isLoaded;
            int undoGroup = -1;
            try
            {
                if (openedHere) map = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                if (map.isDirty) throw new InvalidOperationException("Save or discard gameplay map edits before configuring interior lighting.");
                SceneManager.SetActiveScene(map);
                var budgets = map.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LocalLightBudget>(true)).ToArray();
                if (budgets.Length != 1) throw new InvalidOperationException("Expected one environment light budget in the gameplay map.");
                var sceneLighting = map.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MoonSceneLighting>(true)).ToArray();
                if (sceneLighting.Length != 1) throw new InvalidOperationException("Expected one MoonSceneLighting in the gameplay map.");
                var budget = budgets[0];
                var environment = budget.transform.root;
                var lights = environment.GetComponentsInChildren<Light>(true);
                var rooms = new[]
                {
                    Room(environment, lights, "L22 control room", new Vector3(1137.5f, 45.55f, 1318), new Vector3(17, 5.6f, 9),
                        "MC_L22 PLANT CONTROL ceiling task actual light_05611", "MC_L22 PLANT CONTROL ceiling task actual light_05614",
                        "MC95_IO_task_emitter", "MC97_BAY_test_task_bar_emitter", "MC97_RAFT_OPERATIONS_task_emitter", "MC97_RAFT_REPLAY_task_emitter"),
                    Room(environment, lights, "G03 service room", new Vector3(1137.5f, 33.55f, 1309.5f), new Vector3(18, 5.6f, 8.6f),
                        "MC_G03 CLEAN SUPPLY / SERVICE ceiling task actual light_05064", "MC_G03 CLEAN SUPPLY / SERVICE ceiling task actual light_05067",
                        "MC108_Task_Bar_Actual_Light")
                };
                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Configure MC00 interior lighting");
                // Fixed values in fixture order make reapplying the menu idempotent.
                var fixtures = rooms.SelectMany(room => room.fixtures).ToArray();
                var intensities = new[] { 24.375f, 24.375f, 3f, 6f, 7.8f, 7.8f, 24.375f, 24.375f, 3f };
                if (fixtures.Length != intensities.Length) throw new InvalidOperationException("Unexpected room fixture layout.");
                for (int i = 0; i < fixtures.Length; i++)
                {
                    Undo.RegisterCompleteObjectUndo(fixtures[i], "Tune existing room fixture");
                    fixtures[i].intensity = intensities[i];
                    EditorUtility.SetDirty(fixtures[i]);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(fixtures[i]);
                }
                var rig = environment.GetComponent<InteriorLightingRig>();
                if (!rig) rig = Undo.AddComponent<InteriorLightingRig>(environment.gameObject);
                Undo.RegisterCompleteObjectUndo(rig, "Configure room lighting");
                rig.Configure(budget, rooms);
                EditorUtility.SetDirty(rig);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
                Undo.RegisterCompleteObjectUndo(sceneLighting[0], "Configure interior exposure");
                var lightingSettings = new SerializedObject(sceneLighting[0]);
                lightingSettings.FindProperty("adaptExposure").boolValue = true;
                lightingSettings.FindProperty("maximumExposureEV").floatValue = 1.5f;
                lightingSettings.FindProperty("adaptInteriorShadows").boolValue = true;
                lightingSettings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(sceneLighting[0]);
                PrefabUtility.RecordPrefabInstancePropertyModifications(sceneLighting[0]);
                EditorSceneManager.MarkSceneDirty(map);
                if (!EditorSceneManager.SaveScene(map)) throw new IOException("Could not save MC00 interior lighting in the gameplay map.");
                Undo.CollapseUndoOperations(undoGroup);
                Debug.Log("MC00 gameplay lighting saved: nine existing fixtures, two room reflections and enclosed-view shadow adaptation. Runtime appearance remains subject to Play acceptance.");
            }
            catch
            {
                if (undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (openedHere && map.IsValid() && map.isLoaded) EditorSceneManager.CloseScene(map, true);
            }
        }

        private static InteriorLightingRig.Room Room(Transform owner, Light[] lights, string name, Vector3 center,
            Vector3 size, params string[] sources)
        {
            // These positions use the unchanged Apollo12 coordinate contract. Convert once;
            // runtime rooms then remain attached to the environment that owns their fixtures.
            if (Quaternion.Angle(owner.rotation, Quaternion.identity) > .01f || (owner.lossyScale - Vector3.one).sqrMagnitude > .0001f)
                throw new InvalidOperationException("MC00 room bounds require the authored environment orientation and scale.");
            var fixtures = sources.Select(source =>
            {
                var matches = lights.Where(l => l.transform.parent && l.transform.parent.name == source).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("Expected one runtime fixture under " + source + "; found " + matches.Length);
                return matches[0];
            }).ToArray();
            return new InteriorLightingRig.Room
            {
                name = name, localCenter = owner.InverseTransformPoint(center), size = size,
                fixtures = fixtures,
                reflectionIntensity = 1
            };
        }
    }
}
