// 给玩家实体烘焙 PredictedPlayerGhost、输入组件和预测状态缓冲。角色被生成后，预测与同步系统就能拿到这些数据。
using Unity.Entities;
using UnityEngine;

[DisallowMultipleComponent]
public class PredictedPlayerGhostAuthoring : MonoBehaviour {
    public float DisabledPredictionLerpFactor = 10f;
}

public class PredictedPlayerGhostBaker : Baker<PredictedPlayerGhostAuthoring> {
    public override void Bake(PredictedPlayerGhostAuthoring authoring) {
        var entity = GetEntity(TransformUsageFlags.None);
        // 预制件刚生成时接入角色网络状态组件。服务器在入场、出生和战局更新中继续给血量、朝向和装备字段填相应数值。
        AddComponent(entity,
            new PredictedPlayerGhost { DisabledPredictionLerpFactor = authoring.DisabledPredictionLerpFactor });
        // 状态缓冲记按拍整理的控制器状态，输入组件单独记录操作。预测系统查询角色时可以按这个结构取各自数据。
        AddBuffer<PredictedPlayerGhostState>(entity);
        AddComponent<PlayerInputComponent>(entity);
    }
}