using System;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.SpiderLab;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class AlienHitCoverageChecks
{
    [MenuItem("Tools/Moonkov/Aliens/Check worm tissue hit coverage")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var root = new GameObject("Temporary tissue coverage fixture");
        root.transform.position = new Vector3(-9200, 1000, -9200);
        var floor = new GameObject("Coverage floor"); floor.transform.position = root.transform.position - Vector3.up * .25f;
        floor.AddComponent<BoxCollider>().size = new Vector3(50, .5f, 50);
        int shots = 0;
        try
        {
            foreach (string name in new[] { "Snake", "Leech", "Caterpillar", "Centipede" })
            {
                var profile = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset");
                using (var body = new AlienCreatureBody(root.transform, profile, profile.Scale, false, LayerMask.NameToLayer("ServerPlayer")))
                {
                    body.Tick(Vector3.zero, 0, 0, 1f / 60, 0, false);
                    Physics.SyncTransforms();
                    var chain = (CreatureChain)typeof(AlienCreatureBody).GetField("m_Chain", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(body);
                    var colliders = root.GetComponentsInChildren<Collider>();
                    // Fire down onto the outer shoulder at many points along the visible tube.
                    // The old radius*verticalScale approximation misses this flattened surface.
                    for (int ring = 8; ring < 89; ring += 4)
                    {
                        var side = Vector3.Cross(chain.Ups[ring], chain.Tangents[ring]).normalized;
                        var shoulder = chain.Centres[ring] + side * chain.Radii[ring] * .9f;
                        var ray = new Ray(shoulder + chain.Ups[ring], -chain.Ups[ring]);
                        bool hit = false;
                        foreach (var collider in colliders) if (collider.Raycast(ray, out _, 2)) { hit = true; break; }
                        if (!hit) throw new InvalidOperationException(name + " missed visible shoulder at ring " + ring);
                        shots++;
                    }
                }
            }
            Debug.Log("Worm tissue hit coverage passed: " + shots + " shoulder shots across four species; no client renderer allocation.");
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(floor); }
    }
}
