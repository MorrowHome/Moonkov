// GameManager 通过这套接口管理播放和停止。普通客户端接 SoundSystem，专用服务器可以接静音实现，让调用方走同一套流程。
using UnityEngine;
using UnityEngine.Audio;

public interface ISoundSystem {
    void Init(
        Transform listenerTransform,
        int maxSoundEmitters,
        SoundGameObjectPool soundGameObjectPool,
        AudioMixer mixer);

    // 跟着角色或设备发声时传 Transform。带 localPosition 的版本可选物体上的发声点，传 Vector3 的版本则把声音留在世界某个位置。
    SoundSystem.SoundInfo CreateEmitter(SoundDef soundDef, Transform transform, float volume = 1);
    SoundSystem.SoundInfo CreateEmitter(SoundDef soundDef, Vector3 position, float volume = 1);

    SoundSystem.SoundInfo CreateEmitter(
        SoundDef soundDef,
        Transform transform,
        Vector3 localPosition,
        float volume = 1);

    // 需要在播放前自定义音高、空间感时，可以先构造 SoundInfo，再交给 PlayEmitter。拿到句柄的调用方还能让声音淡出或立即收回。
    bool PlayEmitter(SoundSystem.SoundInfo soundInfo, float volume = 1);
    void UpdateSoundSystem(bool muteSound = false);
    bool Stop(SoundSystem.SoundInfo soundInfo, float fadeOutTime = 0.0f);
    void KillAll();
    bool Kill(SoundSystem.SoundInfo soundInfo);
    bool SetListenerTransform(Transform listenerTransform);
}