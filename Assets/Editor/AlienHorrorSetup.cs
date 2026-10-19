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
        Tune("Spider", "伏顶蛛", "留意头顶刮擦声；起扑前侧移，落地后反击。", AlienThreatTier.Common, AlienApproach.Direct, 180, 30);
        var snake = Tune("Snake", "裂颚游蛇", "沿侧翼贴地逼近；避开直线咬击，转向时反击。", AlienThreatTier.Common, AlienApproach.Flank, 180, 30);
        snake.Chain.lateralAmplitude = .14f; snake.Chain.waveFrequency = 1.1f;
        Tune("Caterpillar", "腐囊喷吐者", "保持中距喷吐；横移躲过锁定落点，借掩体接近。", AlienThreatTier.Common, AlienApproach.KeepDistance, 220, 32).PreferredDistance = 7;
        Tune("Leech", "噬血蛭", "从侧后方逼近并吸血自愈；及时拉开距离切断吸取。", AlienThreatTier.Common, AlienApproach.Flank, 110, 28);
        Tune("FaceHugger", "覆面寄生虫", "贴地绕侧后跃向面部；起跳前击杀，落空后优先清除。", AlienThreatTier.Common, AlienApproach.Flank, 85, 22);
        var centipede = Tune("Centipede", "裂甲冲锋者", "精英：长躯侧切后直线冲撞；横向避让，利用收招窗口。", AlienThreatTier.Elite, AlienApproach.Flank, 620, 44);
        centipede.Scale = 1.05f; centipede.Speed = 5.6f / centipede.Scale; centipede.TurnSpeed = 135;
        centipede.Chain.lateralAmplitude = .12f; centipede.Chain.waveFrequency = .9f;
        var slime = Tune("Slime", "黑潮囊母", "精英：近身三连脉冲，间歇喷脸；离开液体躯体后横移。", AlienThreatTier.Elite, AlienApproach.Direct, 520, 36);
        slime.Scale = 1.05f; slime.Speed = 2.7f / slime.Scale; slime.Branch.frequency = .65f;
        var walker = Tune("StiltWalker", "长脚窥行者", "精英：被注视时短暂停步，随后绕侧伸臂抓击；不要只顾凝视。", AlienThreatTier.Elite, AlienApproach.Stalk, 440, 42);
        walker.FlankDistance = 3.5f; walker.Speed = 4; walker.TurnSpeed = 100;
        var watcher = Tune("StiltWatcher", "长颈监视者", "精英：4.2 米长颈轮廓，缓慢逼近后长距抓击；借低矮通道脱离。", AlienThreatTier.Elite, AlienApproach.Stalk, 680, 48);
        watcher.FlankDistance = 4.5f; watcher.AttackReach = 4.2f; watcher.Windup = 1.05f; watcher.Speed = 3.2f; watcher.TurnSpeed = 75;
        var tentacle = Tune("Tentacle", "深渊肢王", "巨型首领：横扫前方与锁点重砸交替；半血转阶段，绕到背侧或借厚掩体避让。", AlienThreatTier.Boss, AlienApproach.Direct, 3400, 48);
        tentacle.Scale = 4; tentacle.Branch.bodyRadii = new Vector3(.55f, .85f, .7f); tentacle.Branch.reach = 3;
        tentacle.Branch.rootRadius = .19f; tentacle.Branch.tipRadius = .055f;
        tentacle.Branch.frequency = .42f; tentacle.Branch.lift = .22f; tentacle.Branch.curl = .16f;
        tentacle.Branch.bodyStretch = .12f; tentacle.Branch.ripple = .012f;
        tentacle.BossPursuitSpeed = 4.2f; tentacle.BossRepositionSpeed = 6; tentacle.BossRepositionSeconds = .8f;
        tentacle.BossPreferredDistance = 10; tentacle.BossRepositionCooldown = 5; tentacle.TurnSpeed = 65;
        var roots = Tune("Roots", "葬根主宰", "巨型首领：扎根封锁远处落点，攻击间拔根换位；根刺间留有通路，半血连发。", AlienThreatTier.Boss, AlienApproach.Direct, 4400, 42);
        roots.Scale = 4; roots.Branch.bodyRadii = new Vector3(.65f, 1.05f, .65f); roots.Branch.reach = 3.6f;
        roots.Branch.rootRadius = .16f; roots.Branch.tipRadius = .045f;
        roots.Branch.frequency = .3f; roots.Branch.lift = .16f; roots.Branch.curl = .25f;
        roots.Branch.ripple = .01f; roots.Branch.growthRate = .6f;
        roots.BossPursuitSpeed = 2.2f; roots.BossRepositionSpeed = 3.8f; roots.BossRepositionSeconds = 1.1f;
        roots.BossPreferredDistance = 18; roots.BossRepositionCooldown = 6; roots.TurnSpeed = 45;
        foreach (var boss in new[] { tentacle, roots })
        {
            boss.Speed = boss.BossPursuitSpeed / boss.Scale; boss.SightRange = 65; boss.HearingRange = 28;
            boss.MemorySeconds = 14; boss.PatrolRadius = 3; boss.BossDisplaySeconds = 1.8f;
            boss.LiquidDistance = 22; boss.MaximumLiquidEnemies = 1;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Horror roster applied to 11 profiles already referenced by the formal Theophilus map.");
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
