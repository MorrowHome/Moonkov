// 给玩家实体烘焙 PredictedPlayerGhost、输入组件和预测状态缓冲。角色被生成后，预测与同步系统就能拿到这些数据。
using Unity.Entities;
using UnityEngine;

[DisallowMultipleComponent]
public class PredictedPlayerGhostAuthoring : MonoBehaviour
{
    public float DisabledPredictionLerpFactor = 10f;
}

public class PredictedPlayerGhostBaker : Baker<PredictedPlayerGhostAuthoring>
{
    public override void Bake(PredictedPlayerGhostAuthoring authoring)
    {
        var entity = GetEntity(TransformUsageFlags.None);
        AddComponent(entity, new PredictedPlayerGhost { DisabledPredictionLerpFactor = authoring.DisabledPredictionLerpFactor });
        AddBuffer<PredictedPlayerGhostState>(entity);

        AddComponent<PlayerInputComponent>(entity);
    }
}
