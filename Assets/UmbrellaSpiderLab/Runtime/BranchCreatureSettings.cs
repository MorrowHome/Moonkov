using System;
using UnityEngine;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>Authoring controls for the three radial/branched creatures. Shape edits
    /// rebuild owned topology; movement, contact and material edits apply immediately.</summary>
    [Serializable]
    public sealed class BranchCreatureSettings
    {
        [Header("体形与拓扑 / 米")]
        public Vector3 bodyRadii = new Vector3(.38f, .30f, .52f);
        [Range(3, 8)] public int arms = 8;
        [Range(.5f, 2.5f)] public float reach = 1.7f;
        [Range(.05f, .23f)] public float rootRadius = .13f;
        [Range(.02f, .1f)] public float tipRadius = .045f;
        [Header("移动与抓点预测")]
        [Range(.1f, 6)] public float speed = 2.8f;
        [Range(1, 3)] public float burstMultiplier = 1.8f;
        [Range(60, 540)] public float turnSpeed = 260;
        [Range(.2f, 4)] public float frequency = 1.1f;
        [Range(.3f, .85f)] public float stanceFraction = .62f;
        [Range(0, .6f)] public float prediction = .22f;
        [Range(.1f, .8f)] public float lift = .32f;
        [Range(.05f, .7f)] public float curl = .30f;
        [Range(2, 40)] public float follow = 16;
        [Range(.3f, 2)] public float stuckDelay = .85f;
        [Header("流动形变与主动动作 / Space")]
        [Range(0, .8f)] public float bodyStretch = .38f;
        [Range(0, .25f)] public float ripple = .045f;
        [Range(.2f, 2)] public float actionDuration = .75f;
        [Range(0, 1)] public float actionExtension = .65f;
        [Range(.05f, 1)] public float growthRate = .35f;
        [Header("实体回退材质")]
        [ColorUsage(false)] public Color colour = new Color(.025f, .035f, .041f);
        [Range(0, 1)] public float smoothness = .84f;

        public static BranchCreatureSettings SlimeDefaults() => new BranchCreatureSettings
        {
            arms = 6, bodyRadii = new Vector3(.58f, .19f, .67f), reach = 1.25f,
            rootRadius = .20f, tipRadius = .065f, speed = 2.3f, frequency = .85f,
            lift = .12f, curl = .10f, bodyStretch = .65f, ripple = .055f
        };
        public static BranchCreatureSettings RootsDefaults() => new BranchCreatureSettings
        {
            arms = 6, bodyRadii = new Vector3(.30f, .36f, .38f), reach = 2.1f,
            rootRadius = .09f, tipRadius = .035f, speed = 1.4f, frequency = .65f,
            stanceFraction = .75f, lift = .18f, curl = .42f, bodyStretch = .15f,
            growthRate = .28f, actionDuration = 1.1f
        };
    }
}
