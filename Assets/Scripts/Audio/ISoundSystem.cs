// GameManager 通过这套接口管理播放和停止。普通客户端接 SoundSystem，专用服务器可以接静音实现，让调用方走同一套流程。
using UnityEngine;
using UnityEngine.Audio;

public interface ISoundSystem {
    void Init(
        Transform listenerTransform,
        int maxSoundEmitters,
        SoundGameObjectPool soundGameObjectPool,
        AudioMixer mixer);

    SoundSystem.SoundInfo CreateEmitter(SoundDef soundDef, Transform transform, float volume = 1);
    SoundSystem.SoundInfo CreateEmitter(SoundDef soundDef, Vector3 position, float volume = 1);

    SoundSystem.SoundInfo CreateEmitter(
        SoundDef soundDef,
        Transform transform,
        Vector3 localPosition,
        float volume = 1);

    bool PlayEmitter(SoundSystem.SoundInfo soundInfo, float volume = 1);
    void UpdateSoundSystem(bool muteSound = false);
    bool Stop(SoundSystem.SoundInfo soundInfo, float fadeOutTime = 0.0f);
    void KillAll();
    bool Kill(SoundSystem.SoundInfo soundInfo);
    bool SetListenerTransform(Transform listenerTransform);
}