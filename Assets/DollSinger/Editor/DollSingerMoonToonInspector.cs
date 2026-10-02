using UnityEditor;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger.Editor
{
    public sealed class DollSingerMoonToonInspector : lilToon.lilToonInspector
    {
        private MaterialProperty nearShadow;
        private MaterialProperty farShadow;

        protected override void LoadCustomProperties(MaterialProperty[] props, Material material)
        {
            isCustomShader = true;
            isShowRenderMode = false;
            nearShadow = FindProperty("_MoonShadowNear", props);
            farShadow = FindProperty("_MoonShadowFar", props);
        }

        protected override void DrawCustomProperties(Material material)
        {
            EditorGUILayout.LabelField("月球阴影遮蔽", EditorStyles.boldLabel);
            m_MaterialEditor.ShaderProperty(nearShadow, "开始压暗距离（米）");
            m_MaterialEditor.ShaderProperty(farShadow, "完全压暗距离（米）");
        }
    }
}
