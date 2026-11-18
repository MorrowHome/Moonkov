// 资源子场景在这里列出客户端输入实体和各个可生成角色的 Ghost 模板。Baker 把这些模板变成 ECS 实体引用，供开局或重生配对使用。
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

public class PlayerEntityPrefabsAuthoring : MonoBehaviour {
    [field: SerializeField] public GhostAuthoringComponent ClientInputEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerRifleEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerShotgunEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent DollSingerEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent HatsuneMikuEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent ShianEntityPrefab { get; private set; }
}

public struct PlayerEntityPrefabs : IComponentData {
    public Entity ClientInputEntityPrefab;
    public Entity PlayerRifleEntityPrefab;
    public Entity PlayerShotgunEntityPrefab;
    public Entity DollSingerEntityPrefab;
    public Entity HatsuneMikuEntityPrefab;
    public Entity ShianEntityPrefab;
}

public class PlayerEntityPrefabsBaker : Baker<PlayerEntityPrefabsAuthoring> {
    public override void Bake(PlayerEntityPrefabsAuthoring authoring) {
        // 输入 Ghost 是玩家入场的必需模板。预制件配表里缺它时，这个 Baker 直接停止生成配表。
        if (authoring.ClientInputEntityPrefab == null) return;
        var playerPrefabsEntity = GetEntity(TransformUsageFlags.None);
        AddComponent(playerPrefabsEntity, new PlayerEntityPrefabs {
            ClientInputEntityPrefab = GetEntity(authoring.ClientInputEntityPrefab.gameObject, TransformUsageFlags.None),
            PlayerRifleEntityPrefab = authoring.PlayerRifleEntityPrefab != null
                ? GetEntity(authoring.PlayerRifleEntityPrefab.gameObject, TransformUsageFlags.None)
                : Entity.Null,
            PlayerShotgunEntityPrefab = authoring.PlayerShotgunEntityPrefab != null
                ? GetEntity(authoring.PlayerShotgunEntityPrefab.gameObject, TransformUsageFlags.None)
                : Entity.Null,
            // 每个角色有自己的网络预制件。它们可以选配，缺的那一项变成 Entity.Null，让运行时按可用模板处理。
            DollSingerEntityPrefab = authoring.DollSingerEntityPrefab != null
                ? GetEntity(authoring.DollSingerEntityPrefab.gameObject, TransformUsageFlags.None)
                : Entity.Null,
            HatsuneMikuEntityPrefab = authoring.HatsuneMikuEntityPrefab != null
                ? GetEntity(authoring.HatsuneMikuEntityPrefab.gameObject, TransformUsageFlags.None)
                : Entity.Null,
            ShianEntityPrefab = authoring.ShianEntityPrefab != null
                ? GetEntity(authoring.ShianEntityPrefab.gameObject, TransformUsageFlags.None)
                : Entity.Null
        });
    }
}