using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioListener))]
internal sealed class MoonkovPeakLimiter : MonoBehaviour
{
    private const float Ceiling = .89f;
    private const float ReleaseSeconds = .065f;
    private float m_Gain = 1f;
    private float m_Release;

    private void Awake() => m_Release = 1f - Mathf.Exp(-1f / (ReleaseSeconds * AudioSettings.outputSampleRate));

    private void OnAudioFilterRead(float[] data, int channels)
    {
        float peak = 0;
        foreach (float sample in data) peak = Math.Max(peak, Math.Abs(sample));
        float target = peak > Ceiling ? Ceiling / peak : 1f;
        float gain = Math.Min(target, m_Gain);
        if (gain >= .9999f)
        {
            m_Gain = 1f;
            return;
        }

        // One gain for all channels keeps the position of world sounds stable.
        // Attenuation happens before the block reaches the output. Release is gradual.
        for (int frame = 0; frame < data.Length; frame += channels)
        {
            for (int channel = 0; channel < channels; channel++)
                data[frame + channel] *= gain;
            gain = Math.Min(target, gain + (1f - gain) * m_Release);
        }
        m_Gain = gain;
    }
}
