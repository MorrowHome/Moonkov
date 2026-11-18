// 把效果、音乐和菜单等分组接到 Unity AudioMixer，并更新各分组的音量。业务层的音量设置从 MoonkovAudio 传进来。
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 管理不同声音分类的混音组和音量。
/// 将振幅转换成分贝，然后传给 Unity 混音器。
/// </summary>
public class SoundMixer {
    // 游戏音效和音乐可分别放大，同时限制在混音器的增益范围内。
    public const float MaximumCategoryVolume = 3f;

    [ConfigVar(Name = "sound.mute", DefaultValue = "-1",
        Description = "Is audio enabled. -1 causes default behavior (on when window has focus)",
        Flags = ConfigVar.Flags.None)]
    public static ConfigVar soundMute;

    // 仅用于调试。
    [ConfigVar(Name = "sound.mastervol", DefaultValue = "1", Description = "Master volume",
        Flags = ConfigVar.Flags.None)]
    public static ConfigVar soundMasterVol;

    // 供设置菜单修改。
    [ConfigVar(Name = "sound.menuvol", DefaultValue = "1", Description = "Menu volume", Flags = ConfigVar.Flags.None)]
    public static ConfigVar soundMenuVol;

    [ConfigVar(Name = "sound.sfxvol", DefaultValue = "1", Description = "SFX volume", Flags = ConfigVar.Flags.Save)]
    public static ConfigVar soundSFXVol;

    [ConfigVar(Name = "sound.musicvol", DefaultValue = "1", Description = "Music volume", Flags = ConfigVar.Flags.Save)]
    public static ConfigVar soundMusicVol;

    /// <summary>
    /// 定义音效分类可用的混音组。
    /// </summary> 
    public enum SoundMixerGroup {
        Music,
        SFX,
        Footsteps,
        Menu
    }

    public AudioMixerGroup[] MixerGroups;
    private const float m_SoundVolumeCutoff = -60f;
    private float m_SoundAmplitudeCutoff;
    private AudioMixer m_AudioMixer;

    /// <summary>
    /// 用指定的 Unity 混音器初始化新的 SoundMixer。
    /// </summary>
    public SoundMixer(AudioMixer audioMixer) { Init(audioMixer); }

    /// <summary>
    /// 建立混音组映射，并准备音量下限等参数。
    /// 把 SoundMixerGroup 枚举对应到真实的 AudioMixerGroup 对象。
    /// </summary>
    /// <param name="audioMixer">用于初始化的 Unity AudioMixer。</param>
    public void Init(AudioMixer audioMixer) {
        m_SoundAmplitudeCutoff = Mathf.Pow(2.0f, m_SoundVolumeCutoff / 6.0f);
        m_AudioMixer = audioMixer;

        // 建立混音组映射。
        // 按枚举名在实际 AudioMixer 资源里找分组。声源用枚举下标去查，缺专用分组的声音会走默认输出。
        string[] subBussNames = SoundMixerGroup.GetNames(typeof(SoundMixerGroup));
        MixerGroups = new AudioMixerGroup[subBussNames.Length];
        for (int i = 0; i < subBussNames.Length; i++)
            if (TryFindMatchingMixerGroup(audioMixer, subBussNames[i], out MixerGroups[i]))
                Debug.Log($"Found MixerGroup: {subBussNames[i]}");
            else
                Debug.Log($"Missing MixerGroup: {subBussNames[i]} Setting to Master output");
    }

    /// <summary>
    /// 将当前音量转换为分贝，更新给混音器。
    /// 每帧调用，使正在播放的声音及时响应音量变化。
    /// </summary>
    /// <param name="masterVolume">主音量倍率，范围为 0 到 1。</param>
    public void Update(float masterVolume) {
        // 这里把几个线性音量倍率乘好，再换成混音器参数接受的分贝。菜单、效果、音乐的上限按各自设置处理。
        m_AudioMixer.SetFloat("MasterVolume",
            DecibelFromAmplitude(Mathf.Clamp(soundMasterVol.FloatValue, 0.0f, 1.0f) * masterVolume));
        m_AudioMixer.SetFloat("MusicVolume",
            DecibelFromAmplitude(Mathf.Clamp(soundMusicVol.FloatValue, 0.0f, MaximumCategoryVolume)));
        m_AudioMixer.SetFloat("SFXVolume",
            DecibelFromAmplitude(Mathf.Clamp(soundSFXVol.FloatValue, 0.0f, MaximumCategoryVolume)));
        m_AudioMixer.SetFloat("MenuVolume", DecibelFromAmplitude(Mathf.Clamp(soundMenuVol.FloatValue, 0.0f, 1.0f)));
    }

    private bool TryFindMatchingMixerGroup(AudioMixer audioMixer, string groupName, out AudioMixerGroup firstGroup) {
        var groups = audioMixer.FindMatchingGroups(groupName);
        if (groups.Length == 0) {
            firstGroup = null; // AudioMixerGroup 里找不到所需的混音定义时，回到默认 Master 输出。
            return false;
        }
        firstGroup = groups[0];
        return true;
    }

    private float DecibelFromAmplitude(float amplitude) {
        // 接近静音时直接用固定的最低分贝。这样零音量也不必拿来算对数。
        if (amplitude < m_SoundAmplitudeCutoff) return -60.0f;
        return 6.0f * Mathf.Log(amplitude) / Mathf.Log(2.0f);
    }
}