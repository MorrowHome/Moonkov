using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Keep STP's normal precision path in D3D11-only Windows builds.</summary>
public sealed class MoonkovStpShaderStripper : IPreprocessComputeShaders
{
    public int callbackOrder => -100;

    public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
    {
        var target = EditorUserBuildSettings.activeBuildTarget;
        if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneWindows) return;
        var apis = PlayerSettings.GetGraphicsAPIs(target);
        // The D3D compiler enum covers both D3D11 and D3D12. Never strip the
        // latter's supported native-16-bit variants when it is part of the build.
        if (apis.Length == 0 || apis.Any(api => api != GraphicsDeviceType.Direct3D11)) return;
        string path = AssetDatabase.GetAssetPath(shader);
        const string root = "Packages/com.unity.render-pipelines.core/Runtime/STP/";
        if (path != root + "StpSetup.compute" && path != root + "StpPreTaa.compute" && path != root + "StpTaa.compute") return;

        var native16Bit = new ShaderKeyword(shader, "UNITY_DEVICE_SUPPORTS_NATIVE_16BIT");
        for (int i = data.Count - 1; i >= 0; i--)
            if (data[i].shaderCompilerPlatform == ShaderCompilerPlatform.D3D && data[i].shaderKeywordSet.IsEnabled(native16Bit))
                data.RemoveAt(i);
    }
}
