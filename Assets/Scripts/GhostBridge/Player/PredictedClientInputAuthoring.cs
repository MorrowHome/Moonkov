// 把 PredictedClientInput 加到输入 Ghost 上。这样本机预测系统就能在属于本连接的输入实体中找到按 tick 保存的指令。
using UnityEngine;
using Unity.NetCode;
using Unity.Entities;
using Unity.Mathematics;

[DisallowMultipleComponent]
public class PredictedClientInputAuthoring : MonoBehaviour {
}

public class PredictedClientInputBaker : Baker<PredictedClientInputAuthoring> {
    public override void Bake(PredictedClientInputAuthoring authoring) {
        var entity = GetEntity(TransformUsageFlags.None);
        AddComponent<PredictedClientInput>(entity);
    }
}