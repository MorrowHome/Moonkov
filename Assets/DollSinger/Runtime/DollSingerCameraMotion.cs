using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
/// <summary>Retains the animated gait while reducing its translation; look and root motion stay immediate.</summary>
internal sealed class DollSingerCameraMotion
{
    private Vector3 reference, previous;
    private float weight;
    private bool initialized;

    public void Reset() { initialized = false; weight = 0f; }

    public Vector3 Step(Vector3 anchor, bool walking, float aim, Vector3 scale, float seconds, float deltaTime)
    {
        if (!initialized || (anchor - previous).sqrMagnitude > .25f * .25f)
        {
            reference = previous = anchor;
            weight = 0;
            initialized = true;
            return Vector3.zero;
        }
        previous = anchor;
        if (deltaTime <= 0) return Vector3.zero;
        reference = Vector3.Lerp(reference, anchor, 1f - Mathf.Exp(-deltaTime / Mathf.Max(.02f, seconds)));
        weight = Mathf.MoveTowards(weight, walking ? 1f : 0f, deltaTime / .12f);
        float aiming = Mathf.Lerp(1f, .55f, Mathf.SmoothStep(0f, 1f, aim));
        var retained = Vector3.Lerp(Vector3.one, new Vector3(Mathf.Clamp01(scale.x), Mathf.Clamp01(scale.y),
            Mathf.Clamp01(scale.z)) * aiming, weight);
        Vector3 correction = Vector3.Scale(anchor - reference, retained - Vector3.one);
        // Posture changes and animation interruptions must not leave a detached eye.
        return new Vector3(Mathf.Clamp(correction.x, -.045f, .045f), Mathf.Clamp(correction.y, -.055f, .055f),
            Mathf.Clamp(correction.z, -.035f, .035f));
    }
}
}
