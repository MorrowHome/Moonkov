using System;
using UnityEngine;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>Artist controls shared by the volume, particle solver and filaments.
    /// The experiment owns this serialized instance; runtime edits are applied each frame.</summary>
    [Serializable]
    public sealed class SpiderFluidSettings
    {
        [Header("流动与吸引 / F2 ORIGINAL 保留旧版")]
        public bool heavy = true;
        public bool attraction = true;
        [Range(0, 30)] public float gravity = 12;
        [Range(0, 1000)] public float bodyAttraction = 220;
        [Range(0, 1000)] public float footAttraction = 650;
        [Range(0, 100)] public float bodyDamping = 34;
        [Range(0, 100)] public float footDamping = 56;
        [Range(0, 80)] public float viscosity = 28;
        [Range(0, 150)] public float pressure = 48;
        [Range(0, 20)] public float cohesion = 2.2f;
        [Range(0, .1f)] public float freeFlowRadius = .012f;
        [Range(100, 1500)] public float bodyAccelerationLimit = 240;
        [Range(100, 2000)] public float tipAccelerationLimit = 420;
        [Range(10, 80)] public float bodySpeedLimit = 28;
        [Range(10, 100)] public float tipSpeedLimit = 40;

        [Header("积液与肢体截面 / 1 = 原始截面")]
        [Range(.2f, 1.5f)] public float mantleThickness = .84f;
        [Range(.2f, 1.5f)] public float rootThickness = .72f;
        [Range(.2f, 1.5f)] public float kneeThickness = .50f;
        [Range(.2f, 1.5f)] public float toeThickness = .38f;
        [Range(0, .7f)] public float legAsymmetry = .42f;
        [Range(0, .4f)] public float localThicknessVariation = .15f;
        [Range(.1f, 1)] public float liquidMass = .45f;
        [Range(.1f, 1)] public float appendageMass = 1;
        [Range(.05f, 1)] public float surfaceThreshold = .50f;
        [Range(0, .25f)] public float motionTrail = .12f;

        [Header("黏滞拉丝 / 米")]
        [Tooltip("900 条候选连接中的显示比例；0 关闭。避免把整个蜘蛛铺成蛛网。")]
        [Range(0, 1)] public float filamentDensity = .32f;
        [Range(.001f, .04f)] public float minimumRadius = .004f;
        [Range(.001f, .06f)] public float maximumRadius = .022f;
        [Range(0, 1)] public float radiusVariation = .85f;
        [Range(0, .98f)] public float necking = .82f;
        [Range(0, 3)] public float rootPooling = 1.4f;
        [Range(0, 1)] public float sag = .12f;
        [Range(0, .04f)] public float velocityLag = .008f;
        [Range(.2f, 2)] public float minimumBreakLength = .8f;
        [Range(1.1f, 6)] public float breakStretch = 3.8f;
        [Range(0, 30)] public float bondTension = 12;
        [Range(0, 10)] public float bondDamping = 3;

        [Header("主体与拉丝共用材质")]
        [ColorUsage(false)] public Color colour = new Color(.0252f, .03456f, .0396f);
        [Range(0, 1)] public float smoothness = .82f;
        [Range(0, .2f)] public float wetFilmVariation = .045f;
        [Range(0, 1)] public float clearCoat = .45f;
        [Range(0, 1)] public float clearCoatSmoothness = .88f;

        public void ApplyMaterial(Material material, bool original = false)
        {
            material.SetColor("_BaseColor", original ? new Color(.035f, .048f, .055f) : colour);
            material.SetFloat("_Smoothness", original ? .94f : smoothness);
            material.SetFloat("_FilmVariation", original ? 0 : wetFilmVariation);
            material.SetFloat("_CoatMask", original ? .85f : clearCoat);
            material.SetFloat("_CoatSmoothness", original ? .97f : clearCoatSmoothness);
        }
    }
}
