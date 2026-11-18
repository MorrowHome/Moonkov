// 处理 Unity 编辑器跳过域重载的情况。进入 Play 时识别带 ResetOnPlayMode 的类型，调用它们指定的静态清理方法，防止上一轮状态留到下一轮。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class ResetOnPlayModeAttribute : Attribute {
    /// <summary>
    /// 进入 Play 时要调用的重置方法名，该方法需要是静态的。
    /// </summary>
    public string ResetMethodName { get; private set; }

    public ResetOnPlayModeAttribute(string resetMethod) { ResetMethodName = resetMethod; }
}

#if UNITY_EDITOR
public static class DomainReloadGuard {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnEnterRuntime() {
        // 正常进 Play 会随域重载重建静态变量。开启跳过域重载后，才按特性名单补做相应的静态清理。
        if (!EditorSettings.enterPlayModeOptionsEnabled ||
            !EditorSettings.enterPlayModeOptions.HasFlag(EnterPlayModeOptions.DisableDomainReload))
            return;
        ResetAllStaticState();
    }

    public static void ResetAllStaticState() {
        var classes = FindClassesNeedingReset();
        int resetCount = 0;
        foreach (var t in classes) {
            var attrs = t.GetCustomAttributes<ResetOnPlayModeAttribute>();
            foreach (var a in attrs) {
                // 用特性中写着的方法名查静态函数。每种类型可以为自己清理实例、回调或设备资源，找不到时输出提示。
                var method = t.GetMethod(a.ResetMethodName,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                if (method != null)
                    //Debug.Log($"Reset class state: {t.Name}.{a.ResetMethodName}");
                    method?.Invoke(null, null);
                else
                    Debug.LogWarning(
                        $"Couldn't find reset method '{a.ResetMethodName}' for class: {t.Name}. Maybe it's not static?");
            }
            resetCount++;
        }
        Debug.Log($"No Domain Reload: {resetCount} class(es) reset");
    }

    private static IEnumerable<Type> FindClassesNeedingReset() {
        return AppDomain.CurrentDomain.GetAssemblies()
            // 按程序集名称过滤，避免扫描从引用载入的大量项目外程序集。
            // 只从游戏和项目编辑器程序集找带该标记的类型。工具包和外部库的静态状态由它们自己管理。
            .Where(a => a.FullName.StartsWith("Assembly-CSharp")).SelectMany(a => a.GetTypes()).Where(t =>
                !t.ContainsGenericParameters && (!t.IsAbstract || IsStaticClass(t)) &&
                t.GetCustomAttributes<ResetOnPlayModeAttribute>().Any());
    }

    private static bool IsStaticClass(Type t) { return t.IsClass && t.IsSealed && t.IsAbstract; }
}
#endif