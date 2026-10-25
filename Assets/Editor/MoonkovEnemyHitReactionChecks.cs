using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.SpiderLab;

public static class MoonkovEnemyHitReactionChecks
{
    [MenuItem("Tools/Moonkov/Check Enemy Hit Reactions")]
    public static void Run()
    {
        CheckPulse();
        foreach (string species in new[] { "Spider", "Snake", "Caterpillar", "Tentacle", "Centipede",
            "Leech", "Slime", "Roots", "FaceHugger", "StiltWalker" })
            CheckCreature(species);
        Debug.Log("Enemy hit reactions passed: late join, repeated health, healing, continuous hits, death/attachment, " +
            "frame rates, restore and ten solid/liquid species with unchanged hitboxes/motor.");
    }

    private static void CheckPulse()
    {
        var root = new GameObject("Temporary hit reaction");
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube); visual.transform.SetParent(root.transform, false);
        try
        {
            foreach (float dt in new[] { 1f / 30, 1f / 60, 1f / 144 })
            {
                var reaction = new AlienHitReaction(root.transform);
                reaction.Observe(80, 100, false);
                Require(reaction.Pulse == 0, "Joining an injured enemy replayed a hit.");
                reaction.Observe(60, 100, false);
                reaction.Present(dt, Vector3.zero, Vector3.up, Vector3.forward, 1, false);
                Require(reaction.Pulse > .1f && Quaternion.Angle(visual.transform.rotation, Quaternion.identity) > .1f,
                    "A confirmed hit did not move the visual.");
                reaction.Restore();
                Require(visual.transform.localPosition == Vector3.zero && visual.transform.localRotation == Quaternion.identity,
                    "Restore left visual drift.");
                float pulse = reaction.Pulse;
                reaction.Observe(60, 100, false); reaction.Observe(90, 100, false);
                Require(Mathf.Abs(pulse - reaction.Pulse) < .00001f, "Repeated snapshots/healing replayed a hit.");
                for (int i = 0; i < 90; i++)
                {
                    reaction.Observe(89 - i * .5f, 100, false);
                    reaction.Present(dt, Vector3.zero, Vector3.up, Vector3.forward, 1, false);
                    Require(reaction.Pulse <= 1.5f, "Continuous hits escaped their bound."); reaction.Restore();
                }
                reaction.Present(1, Vector3.zero, Vector3.up, Vector3.forward, 1, false);
                Require(reaction.Pulse < .001f, "Reaction did not settle.");
                reaction.Observe(0, 100, false); Require(reaction.Pulse == 0, "Death retained a flinch.");
                reaction.Observe(100, 100, false); reaction.Observe(50, 100, true);
                Require(reaction.Pulse == 0, "Attached/hidden enemy flinched.");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void CheckCreature(string species)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var owner = new GameObject("Hit reaction " + species);
        var controlOwner = new GameObject("No hit control " + species);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(owner, scene);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(controlOwner, scene);
        AlienCreatureBody body = null, control = null;
        try
        {
            var profile = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + species + ".asset");
            Require(profile, "Missing creature profile: " + species);
            body = new AlienCreatureBody(owner.transform, profile, profile.Scale, true, LayerMask.NameToLayer("ClientPlayer"));
            control = new AlienCreatureBody(controlOwner.transform, profile, profile.Scale, false, LayerMask.NameToLayer("ServerPlayer"));
            var pose = new SurfaceMotor.Pose { Rotation = Quaternion.identity };
            foreach (bool liquid in new[] { false, true })
            {
                body.SetCombatPresentation(AlienAction.Roam, 0, 0, profile.Health);
                control.SetCombatPresentation(AlienAction.Roam, 0, 0, profile.Health);
                body.Observe(pose, Vector3.zero, false, 0, .016f, 0, liquid);
                control.Observe(pose, Vector3.zero, false, 0, .016f, 0, liquid);
                var hitRoot = owner.transform.Find("Alien authoritative body hitboxes");
                var colliders = hitRoot.GetComponentsInChildren<Collider>();
                var controls = controlOwner.transform.Find("Alien authoritative body hitboxes").GetComponentsInChildren<Collider>();
                body.SetCombatPresentation(AlienAction.Roam, 0, 0, profile.Health * .7f);
                body.Observe(pose, Vector3.zero, false, 0, 0, 0, liquid);
                control.Observe(pose, Vector3.zero, false, 0, 0, 0, liquid);
                body.Observe(pose, Vector3.zero, false, 0, .016f, 0, liquid);
                control.Observe(pose, Vector3.zero, false, 0, .016f, 0, liquid);
                Require(owner.transform.position == Vector3.zero && body.Motor.Current.Position == Vector3.zero,
                    species + " flinch moved the gameplay root.");
                for (int i = 0; i < colliders.Length; i++)
                    Require(Vector3.Distance(controls[i].transform.position, colliders[i].transform.position) < .001f &&
                        Quaternion.Angle(controls[i].transform.rotation, colliders[i].transform.rotation) < .01f,
                        species + " flinch moved a hitbox.");
            }
        }
        finally { body?.Dispose(); control?.Dispose(); UnityEngine.Object.DestroyImmediate(owner);
            UnityEngine.Object.DestroyImmediate(controlOwner); EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Enemy hit reaction: " + message); }
}
