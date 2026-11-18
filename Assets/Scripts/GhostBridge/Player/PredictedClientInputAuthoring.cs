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
        // 输入模板只带预测用的标记和进度数据。真正属于哪个连接、缓冲里有怎样的操作，要等入场以及客户端初始化后才确定。
        AddComponent<PredictedClientInput>(entity);
    }
}