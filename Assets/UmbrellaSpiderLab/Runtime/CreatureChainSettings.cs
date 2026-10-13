using System;
using UnityEngine;

namespace Unity.MP_FPS.SpiderLab
{
    [Serializable]
    public sealed class CreatureChainSettings
    {
        [Header("体形 / 米")]
        [Range(.8f, 8)] public float length = 4.6f;
        [Range(.06f, .4f)] public float radius = .14f;
        [Range(6, 30)] public int segments = 18;
        [Range(.45f, 1.2f)] public float verticalScale = .78f;
        public Vector3 headScale = new Vector3(.32f, .19f, .43f);

        [Header("运动波 / 运行时可调")]
        [Range(.1f, 8)] public float speed = 3.2f;
        [Range(1, 3)] public float burstMultiplier = 1.6f;
        [Range(60, 540)] public float turnSpeed = 190;
        [Range(0, .7f)] public float lateralAmplitude = .28f;
        [Range(.4f, 4)] public float wavelength = 1.65f;
        [Range(.1f, 4)] public float waveFrequency = 1.4f;
        [Range(0, .5f)] public float contraction = .28f;
        [Range(0, .25f)] public float archHeight = .075f;
        [Range(0, .25f)] public float headLift = .045f;
        [Range(0, .8f)] public float bendSmoothing = .55f;
        [Range(0, .45f)] public float segmentGrooveDepth = .24f;
        [Range(0, .6f)] public float sprintHeadLift = .24f;
        [Header("身体约束与落脚预测")]
        [Range(.4f, .95f)] public float collisionRadiusScale = .65f;
        [Range(0, 1)] public float selfSeparation = .65f;
        [Range(1, 2)] public float maximumStretch = 1.45f;
        [Range(20, 90)] public float maximumJointAngle = 55;
        [Range(0, .5f)] public float footPrediction = .12f;
        [Range(.3f, 2)] public float stuckDelay = .8f;

        [Header("毛毛虫腹足与刚毛")]
        [Range(3, 10)] public int legPairs = 6;
        [Range(.08f, .5f)] public float legReach = .27f;
        [Range(.015f, .2f)] public float footLift = .09f;
        [Range(.015f, .08f)] public float legRadius = .035f;
        [Range(0, 12)] public int bristlesPerSegment = 6;
        [Range(0, .3f)] public float bristleLength = .10f;

        [Header("材质")]
        [ColorUsage(false)] public Color bodyColour = new Color(.035f, .055f, .048f);
        [ColorUsage(false)] public Color bellyColour = new Color(.17f, .13f, .09f);
        [Range(0, 1)] public float smoothness = .48f;
        [Range(0, 1)] public float metallic = .08f;

        public static CreatureChainSettings CaterpillarDefaults() => new CreatureChainSettings
        {
            length = 2.9f, radius = .23f, speed = 1.35f, burstMultiplier = 1.7f,
            turnSpeed = 125, lateralAmplitude = .035f, wavelength = 1.05f,
            waveFrequency = 1.6f, headLift = .06f,
            headScale = new Vector3(.37f, .30f, .34f),
            bodyColour = new Color(.055f, .043f, .03f),
            bellyColour = new Color(.11f, .085f, .055f), smoothness = .30f
        };
    }
}
