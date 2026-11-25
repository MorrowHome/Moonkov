using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.MP_FPS {
    // 可变战术道具状态按服务端 World 隔离，不能用进程静态单例共享。
    public sealed class TacticalServerWorld : IComponentData {
        // 飞行中、爆开后或摆在地上的状态独立存进这个对象，攻击者信息在创建时固定下来。客户端只收到它的显示快照。
        public sealed class Object {
            public uint Id;
            public int Owner;
            public Entity OwnerPlayer;
            public ClashDamageSource Source;
            public TacticalItemDefinition Definition;
            public TacticalWorldPhase Phase;
            public PhysicsScene Scene;
            public Transform Shooter;
            public Vector3 Position, Velocity;
            public bool Resting;
            public double Created, Until, NextSnapshot, ArmAt;
            public int Energy;
        }

        // 玩家读条时记录当时的角色、物品版本、受击与武器标记。完成前每个 tick 对照这些起始条件。
        public sealed class Use {
            public Entity Player;
            public int RaidId, Version;
            public uint Request, Target;
            public string ItemId;
            public TacticalItemAction Action;
            public TacticalItemDefinition Definition;
            public Vector3 Origin, Placement;
            public double Until;
            public uint Hit, DamageSequence, ShieldHit, Shot, Grenade, Reload, Weapon;
            public float Health;
        }

        // 燃烧、出血、刺激、疲劳等都按角色挂记录和截止时间。持续伤害沿用原始来源，攻击者掉线后也不会丢掉归属。
        public sealed class Status {
            public double Fire, Poison, Bleed, Stamina, Antidote, Wire, Cough;
            public double FlashUntil;
            public double Regeneration, RegenerationTick;
            public float FlashStrength;
            public TacticalSprintState Sprint;
            public float3 SprintPosition;
            public bool SprintPositionKnown;
            public int FireOwner, PoisonOwner, BleedOwner;
            public ClashDamageSource FireSource, PoisonSource, BleedSource;

            // 这个 NPC 自身的燃烧与出血扣血只在服务端记账，
            // 自我治疗可据此区分同次更新中的既有伤害与新攻击。
            public double PmcStatusHealthDamage;
            public ulong PmcStatusShieldHits;
        }

        public readonly List<Object> Objects = new();
        public readonly Dictionary<Entity, Use> Uses = new();
        public readonly Dictionary<Entity, TacticalItemFeedback> Feedbacks = new();
        public readonly Dictionary<Entity, Status> Statuses = new();
        public readonly List<Entity> Scratch = new();
        public readonly List<Entity> Victims = new();
        public uint Epoch = 1, NextId, Sequence;
        public object Map;
        public double NextOwnerSnapshot, Now;
        public int GrenadeReservations;
        public RaycastHit[] Hits = new RaycastHit[32];
        public readonly Collider[] Overlaps = new Collider[32];
    }
}
