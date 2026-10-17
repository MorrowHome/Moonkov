using Unity.MP_FPS.SpiderLab;
using UnityEngine;

namespace Unity.MP_FPS
{
    public enum AlienSpecies : byte { Spider, Snake, Caterpillar, Tentacle, Centipede, Leech, Slime, Roots }

    /// <summary>Shared authoring data only. Scale is uniform and authoritative: body
    /// dimensions, motor contacts, hitboxes, strike reach and liquid bounds use it.
    /// Runtime contact/AI state belongs to each spawned enemy, never this asset.</summary>
    [CreateAssetMenu(menuName = "Moonkov/Alien Creature Profile")]
    public sealed class AlienCreatureProfile : ScriptableObject
    {
        public AlienSpecies Species;
        [Header("Scale / 统一尺寸倍率")]
        [Range(.1f, 4)] public float Scale = .65f;
        [Header("服务端感知、运动与近战")]
        [Min(1)] public float Health = 140;
        [Min(.1f)] public float Speed = 3.5f;
        [Range(60, 720)] public float TurnSpeed = 300;
        [Min(1)] public float SightRange = 35;
        [Min(0)] public float HearingRange = 12;
        [Min(.1f)] public float MemorySeconds = 5;
        [Min(0)] public float PatrolRadius = 8;
        [Min(1)] public float Damage = 24;
        [Min(.1f)] public float AttackReach = 1.6f;
        [Range(.15f, 1.5f)] public float Windup = .42f;
        [Range(.15f, 2)] public float Recovery = .55f;
        [Min(.3f)] public float AttackCooldown = 1.6f;
        [Range(0, 8)] public float LungeSpeed = 3;
        [Header("Combat role")]
        public bool Boss;
        [Range(.15f, .8f)] public float EnrageHealthFraction = .5f;
        [Min(.5f)] public float FaceAttachSeconds = 2.4f;
        [Header("Spider ambush encounters")]
        [Min(1)] public float SpiderAmbushRange = 6;
        [Min(.3f)] public float SpiderWindup = .75f;
        [Min(.2f)] public float SpiderStrikeSeconds = .45f;
        [Min(.3f)] public float SpiderRecovery = 1.1f;
        [Header("客户端表现")]
        public bool Liquid = true;
        [Min(2)] public float LiquidDistance = 24;
        [Range(0, 8)] public int MaximumLiquidEnemies = 2;
        public CreatureChainSettings Chain = new CreatureChainSettings();
        public BranchCreatureSettings Branch = new BranchCreatureSettings();
        public PredictiveFeet.GaitSettings Gait = new PredictiveFeet.GaitSettings();
        public SpiderFluidSettings Fluid = new SpiderFluidSettings();
        public bool IsChain => Species == AlienSpecies.Snake || Species == AlienSpecies.Caterpillar || Species == AlienSpecies.Centipede || Species == AlienSpecies.Leech;
        public bool IsLarva => Species != AlienSpecies.Snake;
        public ProceduralMenagerieExperiment.Species BranchSpecies => Species == AlienSpecies.Roots ?
            ProceduralMenagerieExperiment.Species.Roots : Species == AlienSpecies.Slime ? ProceduralMenagerieExperiment.Species.Slime : ProceduralMenagerieExperiment.Species.Tentacle;

        public void ApplyDefaults(AlienSpecies species)
        {
            Species = species;
            if (species == AlienSpecies.Caterpillar) { Chain = CreatureChainSettings.CaterpillarDefaults(); Speed = 2.5f; }
            else if (species == AlienSpecies.Centipede) { Chain = ProceduralMenagerieExperiment.CentipedeDefaults(); Speed = 4.8f; }
            else if (species == AlienSpecies.Leech) { Chain = ProceduralMenagerieExperiment.LeechDefaults(); Speed = 2.7f; }
            else if (species == AlienSpecies.Snake) { Speed = 4.5f; Scale = .55f; }
            else if (species == AlienSpecies.Slime) { Branch = BranchCreatureSettings.SlimeDefaults(); Speed = 2.5f; Health = 180; }
            else if (species == AlienSpecies.Roots) { Branch = BranchCreatureSettings.RootsDefaults(); Speed = 1.4f; AttackReach = 2.6f; }
            else if (species == AlienSpecies.Spider) { Speed = 5.5f; Scale = .45f; }
            else Speed = 3.2f;
            Fluid = species == AlienSpecies.Spider ? new SpiderFluidSettings() : species == AlienSpecies.Centipede ?
                ProceduralMenagerieExperiment.CentipedeFluidDefaults() : IsChain ? ChainFluidPresentation.Defaults(IsLarva) : BranchFluidPresentation.Defaults(BranchSpecies);
            // Defaults retain the accepted lab's world speed at its authored size;
            // later Scale edits still resize both stride geometry and travel speed.
            Speed /= Mathf.Max(.1f, Scale);
        }
    }
}
