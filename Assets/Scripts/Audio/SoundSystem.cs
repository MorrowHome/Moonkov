// GameManager 持有声音系统。业务传来 SoundDef 和播放位置，这里安排发声任务，再由 SoundGameObject 上的 AudioSource 真正播出。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

// 专用服可以照常调用同一套接口。这里用空操作返回，不创建声源也不占用混音器，服务端仍继续跑玩法。
/// <summary>
/// 用给定参数初始化静音版的声音系统。
/// 这里提供空实现，调用后不执行播放等操作。
/// </summary>
public class SoundSystemNull : ISoundSystem {
    public void Init(
        Transform listenerTransform,
        int maxSoundEmitters,
        SoundGameObjectPool soundGameObjectPool,
        AudioMixer mixer) {
    }

    public SoundSystem.SoundInfo CreateEmitter(SoundDef soundDef, Transform transform, float volume = 1) {
        return default;
    }

    public SoundSystem.SoundInfo CreateEmitter(SoundDef soundDef, Vector3 position, float volume = 1) {
        return default;
    }

    public SoundSystem.SoundInfo CreateEmitter(
        SoundDef soundDef,
        Transform transform,
        Vector3 localPosition,
        float volume = 1) {
        return default;
    }

    public bool PlayEmitter(SoundSystem.SoundInfo soundInfo, float volume = 1) { return true; }
    public void UpdateSoundSystem(bool muteSound = false) { }
    public bool Stop(SoundSystem.SoundInfo soundInfo, float fadeOutTime = 0.0f) { return true; }
    public void KillAll() { }
    public bool Kill(SoundSystem.SoundInfo soundInfo) { return true; }
    public bool SetListenerTransform(Transform listenerTransform) { return true; }
}

/// <summary>
/// 主声音系统读取 SoundDef ScriptableObject，经发声器和声音对象来播放。
/// 负责复用音源、空间化和其他声音效果。
/// 播放一份 SoundDef 需要分配一个 SoundEmitter。
/// 每个 SoundEmitter 可以控制多个 SoundGameObjects。
/// SoundGameObject 应用 SoundDef 参数。实际 AudioClips 由它里面的 AudioSource 播放。
/// </summary>
public class SoundSystem : ISoundSystem {
    public class SoundInfo {
        public SoundDef SoundDefinition;
        public Transform SoundTransform;

        public SoundEmitter.SoundDefOverrideData
            SoundDefOverrideInfo; // 可从 SoundDef 复制参数，让调用者在播放前改这一次的值。

        public SoundEmitter.ReservedInfo Reserved;
        public Vector3 Position;
        public SoundHandle Handle;

        public SoundInfo() {
            SoundDefinition = null;
            SoundTransform = null;
            SoundDefOverrideInfo = null;
            Reserved = SoundEmitter.ReservedInfo.FreeAfterPlaybackCompletes;
            Position = Vector3.zero;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init() { s_SoundMute = false; }

    public static bool s_SoundMute;
    public SoundGameObjectPool SoundGameObjects;
    public List<SoundEmitter> m_SoundEmitterPool; // 保存所有供分配的发声器，包括已分配的对象。

    public List<SoundEmitter>
        m_SoundEmitterActiveList; // 保存正在播放或仍被预留的发声器。

    private int m_SequenceId;
    private readonly Dictionary<SoundDef, float> m_LastStarted = new();
    private readonly Dictionary<SoundDef, int> m_LastClip = new();
    private Interpolator m_MasterVolume = new(1.0f, Interpolator.CurveType.SmoothStep);
    private SoundMixer m_SoundMixer;
    private Transform m_ListenerTransform;

    /// <summary>
    /// 用发声器和声音对象池初始化系统。
    /// 创建指定数量的发声器，并设置混音器。
    /// </summary>
    /// <param name="listenerTransform">代表音频监听器的 Transform。</param>
    /// <param name="maxSoundEmitters">要创建的发声器数量上限。</param>
    /// <param name="soundGameObjectPool">存放音源的声音对象池。</param>
    /// <param name="mixer">管理混音组的音频混音器。</param>
    public void Init(
        Transform listenerTransform,
        int maxSoundEmitters,
        SoundGameObjectPool soundGameObjectPool,
        AudioMixer mixer) {
        // 初始化一个新系统时清掉之前的播放时间和上一片段。限频与避免连播同一片都从新的系统状态开始计。
        m_LastStarted.Clear();
        m_LastClip.Clear();
        SoundGameObjects = soundGameObjectPool; // 
        m_ListenerTransform = null;
        SetListenerTransform(listenerTransform);
        m_SoundMixer = new SoundMixer(mixer); // 初始化混音组。
        m_SequenceId = 0;
        m_SoundEmitterPool = new List<SoundEmitter>();
        for (int i = 0; i < maxSoundEmitters; i++) {
            var emitter = CreateSoundEmitterForPool(SoundGameObjects);
            m_SoundEmitterPool.Add(emitter);
        }
        m_SoundEmitterActiveList = new List<SoundEmitter>(); // 这里保存当前处于活动状态的发声器。
    }

    // 所有者离开时也要回收正在淡出的音源，即使调用者手里已没播放句柄。
    public void StopAttachedTo(Transform owner) {
        // 角色退场时反查当前挂在其子物体上的声源。已经开始淡出的声源也在活动列表里，可以随角色一次性清理。
        if (!owner) return;
        for (int i = m_SoundEmitterActiveList.Count - 1; i >= 0; i--) {
            var emitter = m_SoundEmitterActiveList[i];
            bool attached = false;
            foreach (var source in emitter.ActiveSoundGameObjects)
                if (source.m_AudioSource && source.m_AudioSource.transform.IsChildOf(owner)) {
                    attached = true;
                    break;
                }
            if (!attached) continue;
            emitter.UnreserveAndKill();
            m_SoundEmitterActiveList.RemoveAt(i);
        }
    }

    public bool IsWithinHearing(Vector3 position, float range) {
        return m_ListenerTransform && (position - m_ListenerTransform.position).sqrMagnitude <= range * range;
    }

    /// <summary>
    /// 设置空间音频计算使用的监听器 Transform。
    /// 通常用带 AudioListener 组件的 GameObject 的 Transform。
    /// </summary>
    /// <param name="listenerTransform">监听器的 Transform。</param>
    /// <returns>成功设置监听器 Transform 时为 true，未设置时为 false。</returns>
    public bool SetListenerTransform(Transform listenerTransform) {
        if (listenerTransform == null) return false;
        m_ListenerTransform = listenerTransform;
        if (Application.isPlaying && listenerTransform.TryGetComponent<AudioListener>(out var listener) &&
            !listener.TryGetComponent<MoonkovPeakLimiter>(out _))
            listener.gameObject.AddComponent<MoonkovPeakLimiter>();
        return true;
    }

    /// <summary>
    /// 停止 SoundInfo 对应的发声器，也可以先安排淡出。
    /// fadeOutTime 为 0 时立即结束，未预留的发声器可当场归还池里。
    /// </summary>
    /// <param name="soundInfo">含要停止的发声器信息。</param>
    /// <param name="fadeOutTime">淡出秒数，默认 0 表示立即停止。</param>
    /// <returns>停止成功为 true，声音无效时为 false。</returns>false 表示停止失败。</returns>
    public bool Stop(SoundInfo soundInfo, float fadeOutTime = 0.0f) {
        // 停止前确认句柄仍属于同一笔任务。池对象被下一个声音借走以后，旧停止请求就不会把新声音停掉。
        if (soundInfo == null || soundInfo.Handle == null || !soundInfo.Handle.IsValid()) return false;
        if (fadeOutTime == 0)
            // 通过 Kill() 回收，才能同时把发声器从活动列表移除。
            // 若直接调用 Emitter.Kill()，活动列表会留下旧记录，
            // 后续更新仍可能把它当作正在播旧声音。
            return Kill(soundInfo);
        soundInfo.Handle.Emitter.FadeOut(fadeOutTime);
        return true;
    }

    /// <summary>
    /// 立即停止并回收所有活动发声器及声音对象。
    /// 所有发声器交还分配池，供后续播放复用。
    /// </summary>
    public void KillAll() {
        foreach (var soundEmitter in m_SoundEmitterActiveList)
            soundEmitter.UnreserveAndKill(); // 回收全部发声器和 SoundGameObjects。
        m_SoundEmitterActiveList.Clear();
        m_LastStarted.Clear();
        m_LastClip.Clear();
    }

    /// <summary>
    /// 立即回收指定发声器，并把它从活动列表中移除。
    /// </summary>
    /// <param name="soundInfo">含要回收的发声器信息。</param>
    /// <returns>回收成功为 true，声音无效时为 false。</returns>
    public bool Kill(SoundInfo soundInfo) {
        if (soundInfo == null || soundInfo.Handle == null || !soundInfo.Handle.IsValid()) return false;
        var soundHandle = soundInfo.Handle;
        soundHandle.Emitter.UnreserveAndKill();
        for (int i = 0; i < m_SoundEmitterActiveList.Count; i++)
            if (m_SoundEmitterActiveList[i] == soundHandle.Emitter) {
                m_SoundEmitterActiveList.RemoveAt(i);
                break;
            }
        return true;
    }

    /// <summary>
    /// 更新全部活动发声器，处理主音量和静音。
    /// 每帧调用才能持续维护播放与回收。
    /// </summary>
    /// <param name="muteSound">是否静音所有声音。</param>
    public void UpdateSoundSystem(bool muteSound = false) {
        // 即使当前没有声音也更新混音器电平。
        // 玩家在菜单改音量后，接下来第一条声音就用新值。
        float masterVolume = muteSound == true ? 0 : m_MasterVolume.GetValue();
        if (m_SoundMixer != null) m_SoundMixer.Update(masterVolume);
        if (m_SoundEmitterActiveList.Count == 0) return; // 当前没有播放内容。
        UpdateActiveSoundEmitters();
    }

    /// <summary>
    /// 按给定参数创建并立即播放一条声音。
    /// 适合只触发一次、之后无需单独控制的音效。
    /// </summary>
    /// <param name="soundDef">含音频片段和配置的声音定义。</param>
    /// <param name="transform">发声器挂靠的 Transform。</param>
    /// <param name="localPosition">相对该 Transform 的局部偏移。</param>
    /// <param name="volume">音量倍率，默认为 1。</param>
    /// <returns>可用于控制这条声音的 SoundInfo。</returns>
    public SoundInfo CreateEmitter(SoundDef soundDef, Transform transform, Vector3 localPosition, float volume = 1) {
        var soundInfo = new SoundInfo {
            SoundDefinition = soundDef,
            SoundTransform = transform,
            Position = localPosition
        };
        return AllocateAndPlayEmitter(soundInfo, volume);
    }

    /// <summary>
    /// 创建发声器挂到给定 Transform 下并立即播放。
    /// 适合只触发一次、之后无需单独控制的音效。
    /// </summary>
    /// <param name="soundDef">含音频片段和配置的声音定义。</param>
    /// <param name="transform">发声器挂靠的 Transform。</param>
    /// <param name="volume">音量倍率，默认为 1。</param>
    /// <returns>可用于控制这条声音的 SoundInfo。</returns>
    public SoundInfo CreateEmitter(SoundDef soundDef, Transform transform, float volume = 1) {
        return CreateEmitter(soundDef, transform, Vector3.zero, volume);
    }

    /// <summary>
    /// 在给定的世界坐标创建发声器并立即播放。
    /// 适合只触发一次、之后无需单独控制的音效。
    /// </summary>
    /// <param name="soundDef">含音频片段和配置的声音定义。</param>
    /// <param name="position">声音所在的世界坐标。</param>
    /// <param name="volume">音量倍率，默认为 1。</param>
    /// <returns>可用于控制这条声音的 SoundInfo。</returns>
    public SoundInfo CreateEmitter(SoundDef soundDef, Vector3 position, float volume = 1) {
        var soundInfo = new SoundInfo {
            SoundDefinition = soundDef,
            Position = position
        };
        return AllocateAndPlayEmitter(soundInfo, volume);
    }

    /// <summary>
    /// 用 SoundInfo 中的参数播放发声器。
    /// SoundInfo 的句柄仍有效时，复用同一个发声器。
    /// 这样可以继续追踪 Random_But_Not_Last 与 Sequential 等选片段方式。
    /// </summary>
    /// <param name="soundInfo">含播放参数的声音信息。</param>
    /// <param name="volume">音量倍率，默认为 1。</param>
    /// <returns>发声器成功播放时为 true，否则为 false。</returns>
    public bool PlayEmitter(SoundInfo soundInfo, float volume = 1) {
        if (AllocateAndPlayEmitter(soundInfo, volume) == null) return false;
        return true;
    }

    private SoundEmitter CreateSoundEmitterForPool(SoundGameObjectPool soundGameObjectPool) {
        // SoundEmitter.Init() 重置 FadeOutTime，让回收的发声器以干净状态开始。
        return new SoundEmitter(soundGameObjectPool);
    }

    // 每次借用都换一个序号。旧的 SoundHandle 即使还握着同一个对象，也会因为序号不同而失效。
    private SoundEmitter GetSoundEmitterFromPool(SoundGameObjectPool soundGameObjectPool) {
        foreach (var emitter in m_SoundEmitterPool)
            if (!emitter.Allocated) {
                emitter.SeqId = m_SequenceId++;
                emitter.Init(soundGameObjectPool);
                emitter.Allocated = true;
                return emitter;
            }
        var soundEmitter =
            CreateSoundEmitterForPool(
                soundGameObjectPool); // 没有空闲 SoundEmitters 时创建一个，并加入池子。
        soundEmitter.SeqId = m_SequenceId++;
        soundEmitter.Allocated = true;
        m_SoundEmitterPool.Add(soundEmitter);
        return soundEmitter;
    }

    private SoundInfo AllocateAndPlayEmitter(SoundInfo soundInfo, float volume) {
        if (soundInfo == null || soundInfo.SoundDefinition == null) return null;
        var definition = soundInfo.SoundDefinition;
        if (definition.Clips == null || definition.Clips.Count == 0) return null;
        // 池有有限的实体声源。确定某个三维声音在听觉范围外会完全静音时，播放入口可以直接跳过这次申请。
        var origin = soundInfo.SoundTransform
            ? soundInfo.SoundTransform.TransformPoint(soundInfo.Position)
            : soundInfo.Position;
        float spatial = soundInfo.SoundDefOverrideInfo?.SpatialBlend ?? definition.DistanceInfo.SpatialBlend;
        // 线性衰减在 maxDistance 处已经听不到，无需占一个池里的音源。
        if (m_ListenerTransform && spatial >= .99f &&
            definition.DistanceInfo.VolumeRolloffMode == AudioRolloffMode.Linear &&
            (origin - m_ListenerTransform.position).sqrMagnitude >=
            definition.DistanceInfo.VolumeDistMax * definition.DistanceInfo.VolumeDistMax)
            return null;
        // 下面的间隔和并发上限只管新借出的发声任务。已经持有有效句柄的调用可以继续使用原任务。
        bool freshlyAllocated = soundInfo.Handle == null || !soundInfo.Handle.IsValid();
        if (freshlyAllocated) {
            if (definition.MinimumInterval > 0 && m_LastStarted.TryGetValue(definition, out float started) &&
                Time.unscaledTime - started < definition.MinimumInterval)
                return null;
            if (definition.MaxVoices > 0) {
                int voices = 0;
                foreach (var active in m_SoundEmitterActiveList)
                    if (active.Active && active.Definition == definition)
                        voices++;
                if (voices >= definition.MaxVoices) return null;
            }
        }
        if (freshlyAllocated) {
            // 音源池满时，新的攻击预兆或开火声比环境与拟音更需要保留。
            // 每个声音可能占多个 AudioSource。空位不足时先腾出更低优先级、且允许自动回收的声音。
            while (SoundGameObjects.AvailableSoundObjectCount < definition.PlayCount) {
                int victim = -1, lowestPriority = definition.Priority;
                for (int i = 0; i < m_SoundEmitterActiveList.Count; i++) {
                    var candidate = m_SoundEmitterActiveList[i];
                    if (candidate.Reserved != SoundEmitter.ReservedInfo.FreeAfterPlaybackCompletes ||
                        candidate.Definition.Priority <= lowestPriority)
                        continue;
                    lowestPriority = candidate.Definition.Priority;
                    victim = i;
                }
                if (victim < 0) return null;
                m_SoundEmitterActiveList[victim].UnreserveAndKill();
                m_SoundEmitterActiveList.RemoveAt(victim);
            }
            soundInfo.Handle =
                AllocateSoundEmitter(
                    soundInfo); // 分配 SoundEmitter，绑定 soundDef 与 Reserve 信息。
        }
        var soundHandle = soundInfo.Handle;
        if (!soundHandle.IsValid()) {
            Debug.Log("Invalid Handle");
            return null;
        }
        var soundEmitter = soundHandle.Emitter;
        if (soundInfo.SoundDefOverrideInfo != null)
            soundEmitter.SoundDefOverrideInfo = soundInfo.SoundDefOverrideInfo;
        else
            soundEmitter.CopyOverrideDataFromSoundDef(soundInfo.SoundDefinition);
        soundEmitter.SetVolume(volume); // 在第一个 DSP 音频块到来之前就要设置好。
        // 上一片段按音效配方记。就算换了另一个池槽，也能避免同一个音效连续抽到完全相同的录音。
        if (definition.PlaybackType == SoundDef.PlaybackTypes.RandomNotLast && definition.Clips.Count > 1) {
            int index = Random.Range(0, definition.Clips.Count);
            if (m_LastClip.TryGetValue(definition, out int last) && index == last)
                index = (last + Random.Range(1, definition.Clips.Count)) % definition.Clips.Count;
            soundEmitter.UserClipIndex = (uint)index;
        }
        if (Play(soundEmitter, soundInfo.Position, soundInfo.SoundTransform) == true) {
            m_LastStarted[definition] = Time.unscaledTime;
            m_LastClip[definition] = (int)soundEmitter.UserClipIndex;
            // 近处枪响告诉 MoonkovSoundMix 短时压低环境和衣物声。远处枪响仍按位置播放，且不会挤下本机所有环境声。
            if (definition.Role == SoundDef.MixRole.Weapon && m_ListenerTransform &&
                (origin - m_ListenerTransform.position).sqrMagnitude < 144)
                MoonkovSoundMix.NotifyWeapon();
            return soundInfo;
        }

        // 申请到了发声任务但没能播出去时，当场退回池子。它还没进活跃列表，后续的每帧清理找不到它。
        // 播放没能启动时，通常是 SoundGameObject 池中没有足够空闲音源，
        // 无法满足该 SoundDef 的 PlayCount。发声器已被标记为 Allocated 和 Active，
        // 后面却没有其他地方会对它调用 Update()，
        // 每次失败都会漏掉一个槽位，积累后整个系统可能失声。
        if (freshlyAllocated) {
            soundEmitter.UnreserveAndKill();
            soundInfo.Handle = null;
        }
        return null;
    }

    private SoundHandle AllocateSoundEmitter(SoundInfo soundInfo) {
        var newEmitter = GetSoundEmitterFromPool(SoundGameObjects); // 取一个尚未使用的发声器。
        newEmitter.Reserved =
            soundInfo.Reserved; // Reserve 信息只在初次分配时保存，预留的发声器始终沿用原信息。
        newEmitter.SetSoundDef(soundInfo
            .SoundDefinition); // SoundDef 也只在分配时记录，预留的发声器继续使用原定义。
        return new SoundHandle(newEmitter);
    }

    private bool Play(SoundEmitter soundEmitter, Vector3 position, Transform parent = null) {
        if (soundEmitter == null || soundEmitter.Allocated == false || (soundEmitter.Active == true &&
                                                                        soundEmitter.Reserved ==
                                                                        SoundEmitter.ReservedInfo
                                                                            .FreeAfterPlaybackCompletes))
            return false;
        soundEmitter.Activate();
        if (soundEmitter.PrepareSoundGameObjects(parent, position) ==
            false) // 如果未预留声音对象，则先分配 SoundGameObjects，再设坐标或挂靠节点。
            return false; // 没有取到 SoundGameObjects。
        if (!m_SoundEmitterActiveList.Contains(soundEmitter)) m_SoundEmitterActiveList.Add(soundEmitter);
        soundEmitter.Play(m_ListenerTransform, m_SoundMixer.MixerGroups);
        return true;
    }

    private void UpdateActiveSoundEmitters() {
        for (int i = 0; i < m_SoundEmitterActiveList.Count; i++)
            // 源对象的回收由每帧更新推进。任务报告结束后把它移出活动列表，下一次借用还能找到它。
            if (m_SoundEmitterActiveList[i].Update(m_SoundMixer.MixerGroups, m_ListenerTransform) == false) {
                m_SoundEmitterActiveList.RemoveAt(i); // 发声器已不再活动，从活动列表里移除。
                i--;
            }
    }
}