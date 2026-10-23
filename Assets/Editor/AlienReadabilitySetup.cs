using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class AlienReadabilitySetup
{
    [MenuItem("Tools/Moonkov/Aliens/Apply larger readable creature bodies")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        Tune("Spider", 1, 4.2f);
        var snake = Tune("Snake", 1.25f, 3.7f);
        snake.Chain.radius = .25f; snake.Chain.verticalScale = 1.15f;
        snake.Chain.headScale = new Vector3(.38f, .42f, .44f);
        snake.Chain.headLift = .32f; snake.Chain.sprintHeadLift = .24f;
        Save(snake);
        var caterpillar = Tune("Caterpillar", 1.25f, 2.4f);
        caterpillar.Chain.radius = .32f; caterpillar.Chain.verticalScale = 1.05f;
        caterpillar.Chain.headScale = new Vector3(.45f, .48f, .42f);
        Save(caterpillar);
        var leech = Tune("Leech", .8f, 2.1f);
        leech.Chain.radius = .34f; leech.Chain.verticalScale = 1;
        leech.Chain.headScale = new Vector3(.48f, .45f, .48f);
        leech.Chain.headLift = .18f;
        Save(leech);
        Tune("FaceHugger", .75f, 4);
        var centipede = Tune("Centipede", 1.35f, 4.4f);
        centipede.Chain.radius = .25f; centipede.Chain.verticalScale = 1.1f;
        Save(centipede);
        var slime = Tune("Slime", 1.25f, 2.4f);
        slime.Branch.bodyRadii = new Vector3(.58f, .48f, .67f);
        Save(slime);
        Debug.Log("Seven creature bodies enlarged with explicit world speeds; upright elites and bosses retain their roster sizes.");
    }
    private static AlienCreatureProfile Tune(string name, float scale, float worldSpeed)
    {
        var profile = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset");
        if (!profile) throw new InvalidOperationException("Missing profile " + name);
        Undo.RecordObject(profile, "Improve alien body readability");
        profile.Scale = scale; profile.Speed = worldSpeed / scale;
        Save(profile); return profile;
    }
    private static void Save(AlienCreatureProfile profile)
    {
        EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
    }
}
