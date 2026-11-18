// 给 Ghost 根实体的 LocalTransform 指定默认网络格式。坐标、旋转和缩放按设定精度发送，客户端插值或短时外推。
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Scripting;
using Unity.NetCode;

/// <summary>
/// NetCode 用于 <see cref="Unity.Transforms.LocalTransform"/> 的默认序列化方式。
/// </summary>
[Preserve]
[GhostComponentVariation(typeof(LocalTransform), "Ghost Transform 3D")]
[GhostComponent(PrefabType = GhostPrefabType.All, SendTypeOptimization = GhostSendType.AllClients)]
public struct TransformDefaultVariant {
    /// <summary>
    /// 位置的量化系数为 1000，每个分量约精确到 1 毫米。
    /// 同步后的位置支持插值与外推。
    /// </summary>
    [GhostField(Quantization = 1000, Smoothing = SmoothingAction.InterpolateAndExtrapolate, MaxSmoothingDistance = 2)]
    // 这里的 1000 代表坐标按约千分之一单位编码。插值和外推用于平滑相邻快照，距离超过阈值时避免把大跳变拖成长动画。
    public float3 Position;

    /// <summary>
    /// 缩放数值的量化系数为 1000。
    /// 同步后的缩放支持插值与外推。
    /// </summary>
    [GhostField(Quantization = 1000, Smoothing = SmoothingAction.InterpolateAndExtrapolate)]
    public float Scale;

    /// <summary>
    /// 用四元数同步旋转。各分量量化后的精度约为 10 位或更高。
    /// </summary>
    [GhostField(Quantization = 1000, Smoothing = SmoothingAction.InterpolateAndExtrapolate)]
    public quaternion Rotation;
}

/// <summary>为示例注册采用的默认序列化版本。Transform 组件
/// 有多种自定义版本，这里选择默认版本，并定义子实体怎么处理。</summary>
[CreateBefore(typeof(TransformDefaultVariantSystem))]
internal sealed partial class DefaultGhostVariantSystem : DefaultVariantSystemBase {
    protected override void RegisterDefaultVariants(Dictionary<ComponentType, Rule> defaultVariants) {
        // 只给 Ghost 的根实体默认同步坐标。子节点的骨骼或部件仍可由预制件层级和本地动画更新。
        defaultVariants.Add(typeof(LocalTransform), Rule.OnlyParents(typeof(TransformDefaultVariant)));
    }
}