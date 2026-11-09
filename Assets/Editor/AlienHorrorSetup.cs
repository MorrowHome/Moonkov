using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class AlienHorrorSetup
{
    [MenuItem("Tools/Moonkov/Aliens/Apply horror roster tuning")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        Tune("Spider", "伏顶蛛", "留意头顶刮擦声；起扑前侧移，落地后反击。", AlienThreatTier.Common, AlienApproach.Direct, 180, 39);
        var snake = Tune("Snake", "裂颚游蛇", "沿侧翼贴地逼近；避开直线咬击，转向时反击。", AlienThreatTier.Common, AlienApproach.Flank, 180, 40);
        snake.Chain.lateralAmplitude = .14f; snake.Chain.waveFrequency = 1.1f;
        Tune("Caterpillar", "腐囊喷吐者", "保持中距喷吐；横移躲过锁定落点，借掩体接近。", AlienThreatTier.Common, AlienApproach.KeepDistance, 220, 38).PreferredDistance = 7;
        Tune("Leech", "噬血蛭", "从侧后方逼近并吸血自愈；及时拉开距离切断吸取。", AlienThreatTier.Common, AlienApproach.Flank, 110, 37);
        Tune("FaceHugger", "覆面寄生虫", "贴地绕侧后跃向面部；起跳前击杀，落空后优先清除。", AlienThreatTier.Common, AlienApproach.Flank, 85, 28);
        var centipede = Tune("Centipede", "裂甲冲锋者", "精英：长躯侧切后直线冲撞；横向避让，利用收招窗口。", AlienThreatTier.Elite, AlienApproach.Flank, 620, 54);
        centipede.Scale = 1.05f; centipede.Speed = 7.4f / centipede.Scale; centipede.TurnSpeed = 135;
        centipede.Chain.lateralAmplitude = .12f; centipede.Chain.waveFrequency = .9f;
        var slime = Tune("Slime", "黑潮囊母", "精英：近身三连脉冲，间歇喷脸；离开液体躯体后横移。", AlienThreatTier.Elite, AlienApproach.Direct, 520, 44);
        slime.Scale = 1.05f; slime.Speed = 4.7f / slime.Scale; slime.Branch.frequency = .65f;
        var walker = Tune("StiltWalker", "长脚窥行者", "精英：被注视时短暂停步，随后绕侧伸臂抓击；不要只顾凝视。", AlienThreatTier.Elite, AlienApproach.Stalk, 440, 52);
        walker.FlankDistance = 3.5f; walker.Speed = 6.2f; walker.TurnSpeed = 100;
        var watcher = Tune("StiltWatcher", "长颈监视者", "精英：4.2 米长颈轮廓，缓慢逼近后长距抓击；借低矮通道脱离。", AlienThreatTier.Elite, AlienApproach.Stalk, 680, 56);
        watcher.FlankDistance = 4.5f; watcher.AttackReach = 4.2f; watcher.Windup = 1.05f; watcher.Speed = 5.5f; watcher.TurnSpeed = 75;
        SpiderBossSetup.ApplyProfiles();
        AssetDatabase.SaveAssets();
        Debug.Log("Horror roster applied; giant spider is the boss, tentacle and roots remain ordinary creatures.");
    }

    private static AlienCreatureProfile Tune(string asset, string title, string hint, AlienThreatTier tier,
        AlienApproach approach, float health, float damage)
    {
        var p = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + asset + ".asset");
        if (!p) throw new InvalidOperationException("Missing alien profile: " + asset);
        Undo.RecordObject(p, "Tune alien horror roster");
        p.DisplayName = title; p.EncounterHint = hint; p.ThreatTier = tier; p.Boss = tier == AlienThreatTier.Boss;
        p.Approach = approach; p.Health = health; p.Damage = damage;
        p.MemorySeconds = tier == AlienThreatTier.Common ? 8 : 11;
        EditorUtility.SetDirty(p); return p;
    }
}
