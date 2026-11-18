// 这是池里真正带 AudioSource 的对象。SoundEmitter 把播放任务交给它，它把音量、音高、距离曲线和滤波落实到声源。
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Random = UnityEngine.Random;

/// <summary> 
/// 一个带空间位置和处理效果的音源 GameObject。
/// 管理滤波、空间音及随距离变化的细节。
/// </summary>
public class SoundGameObject {
    /// <summary>
    ///
    /// 指定如何计算和更新 SoundGameObject 的位置。
    /// </summary>
    public enum PositionType {
        Position,
        ParentTransform
    }

    /// <summary>
    /// 保存挂在该 GameObject 上的各个音频组件引用。
    /// </summary>
    public class AudioComponentData {
        public AudioSource ASource;
        public AudioLowPassFilter LowPassFilter;
        public AudioHighPassFilter HighPassFilter;
        public AudioDistortionFilter DistortionFilter;
    }

    public SoundGameObject
        NextAvailable; // 链表中的下一个空闲 SoundGameObject，null 表示后面没有空闲对象。

    public AudioSource m_AudioSource;
    private const float SOUND_VOL_CUTOFF = -60.0f;
    private AudioLowPassFilter m_LowPassFilter;
    private AudioHighPassFilter m_HighPassFilter;
    private AudioDistortionFilter m_DistortionFilter;
    private SoundEmitter m_Emitter; // 当前控制这个 SoundObject 的发声器。
    private bool m_Active;
    private int m_CurrentLoopCount;
    private int m_LastTimeSamples;
    private float m_Volume;
    private GameObject m_SoundGameObjectPool;
    private GameObject m_Parent;
    private PositionType m_PositionType;
    private SoundDef m_SoundDef;
    private Transform m_ListenerTransform;
    private Interpolator m_Curve;
    private bool m_CalculateDistance;
    private float m_LPFCurveNormalizedResult;
    private float m_HPFCurveNormalizedResult;
    private float m_SpatialCurveNormalizedResult;
    private float m_LPFRandomValue;
    private float m_HPFRandomValue;
    private float m_SpatialBlendValue;

    // PlayDelayed 或 PlayScheduled 真正开始发声的 DSP 时间。
    // 0 表示立即播放，或已经开始。
    private double m_PendingStartDsp;
    private float m_NextOcclusion, m_OcclusionGain = 1, m_OcclusionCutoff = 22000;
    private bool m_Occluded;
    private static readonly RaycastHit[] s_Obstacles = new RaycastHit[12];
    private static int s_OcclusionFrame, s_OcclusionBudget;
    private static readonly int s_OcclusionMask = LayerMask.GetMask("Default", "Ground");
#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init() { s_Counter = 0; }

    private static int s_Counter = 0;
    private string m_DebugName;
#endif

    /// <summary>
    /// 使用指定的父对象和音频组件初始化新的 SoundGameObject。
    /// </summary>
    /// <param name="parent">容纳这个声音对象的父 GameObject。</param>
    /// <param name="audioComponentData">供播放和声音处理使用的音频组件。</param>
    public SoundGameObject(GameObject parent, AudioComponentData audioComponentData) {
#if UNITY_EDITOR
        m_DebugName = audioComponentData.ASource.gameObject.name;
#endif
        InitAudioComponents(parent, audioComponentData);
    }

    private void InitAudioComponents(GameObject parent, AudioComponentData audioComponentData) {
        m_SoundGameObjectPool = parent;
        m_Parent = parent;
        m_AudioSource = audioComponentData.ASource;
        m_LowPassFilter = audioComponentData.LowPassFilter;
        m_HighPassFilter = audioComponentData.HighPassFilter;
        m_DistortionFilter = audioComponentData.DistortionFilter;
        m_AudioSource.playOnAwake = false;
        m_Curve = new Interpolator();
    }

    /// <summary>
    /// 根据 SoundDef 配置启用滤波。
    /// 只打开实际会用到的处理组件，减少 CPU 开销。
    /// </summary>
    /// <param name="soundDef">含滤波设置的声音定义。</param>
    /// <returns>有已启用的效果需要计算距离时为 true。</returns>
    public bool EnableAudioFilterComponents(SoundDef soundDef) {
        bool calcDistance = false;
        if (m_LowPassFilter != null)
            if (m_LowPassFilter.enabled = soundDef.LowPassFilter.EnableComponent ? true : false)
                calcDistance = true;
        if (m_HighPassFilter != null)
            if (m_HighPassFilter.enabled = soundDef.HighPassFilter.EnableComponent ? true : false)
                calcDistance = true;
        if (m_DistortionFilter) m_DistortionFilter.enabled = soundDef.DistortionFilter.EnableComponent;
#if UNITY_EDITOR
        if (!EditorApplication.isPlaying)
            return calcDistance; // 在 Unity 检视面板试听时关闭空间混合。
#endif
        if (soundDef.DistanceInfo.SpatialBlendCurveType != Interpolator.CurveType.None &&
            soundDef.DistanceInfo.SpatialBlend_MaxDistance != 0)
            calcDistance = true;
        return calcDistance;
    }

    /// <summary>
    /// 启用 SoundGameObject，并把它交给指定发声器。
    /// </summary>
    /// <param name="soundEmitter">要控制这个声音对象的发声器。</param>
    public void Enable(SoundEmitter soundEmitter) {
        m_Active = true;
        m_Emitter = soundEmitter;
        m_AudioSource.transform.position = Vector3.zero;
    }

    /// <summary>
    /// 若 SoundGameObject 仍归该发声器，结束播放并清理。
    /// </summary>
    /// <param name="emitter">发出结束请求的发声器。</param>
    public void Kill(SoundEmitter emitter) {
        if (m_Emitter == emitter) {
            StopAudioSource();
            // 退场或退出 Play 时，共用的池子或挂靠的音源可能先被销毁。
            // 持有者随后才释放发声器。这时 Unity 对象和 null 比较会相等，
            // 即使 C# 里的托管引用还在。
            if (m_PositionType == PositionType.ParentTransform && m_AudioSource && m_SoundGameObjectPool)
                m_AudioSource.transform.parent = m_SoundGameObjectPool.transform;
            m_Active = false;
            m_Emitter = null;
        }
    }

    /// <summary>
    /// 立即停止 AudioSource。
    /// </summary>
    public void StopAudioSource() {
        if (m_AudioSource != null) m_AudioSource.Stop();
    }

    /// <summary>
    /// 检查 AudioSource 当前是否在播。
    /// </summary>
    /// <returns>在播放时为 true，否则为 false。</returns>
    public bool IsPlaying() {
        if (m_AudioSource == null) return false;
        return m_AudioSource.isPlaying;
    }

    /// <summary>
    /// 设置这个 SoundGameObject 是否启用。
    /// </summary>
    /// <param name="activeFlag">true 为启用，false 为停用。</param>
    public void SetActive(bool activeFlag) { m_Active = activeFlag; }

    /// <summary>
    /// 根据当前状态更新 SoundGameObject，处理音频参数、循环与回收。
    /// </summary>
    /// <param name="soundEmitter">控制该对象的发声器。</param>
    /// <param name="repeatCount">当前剩余重复次数。</param>
    /// <param name="count">活动音源计数器的引用。</param>
    /// <param name="fatal">不可恢复错误标记的引用。</param>
    /// <returns>仍然活动且在播放时为 true，否则为 false。</returns>
    public bool Update(SoundEmitter soundEmitter, int repeatCount, ref int count, ref bool fatal) {
        fatal = false;
        if (m_Active == false) return false;
        // 播放任务可能还在，但跟随的物体已经被销毁。用 fatal 把这种情况告知上一层，它会检查池对象并归还任务。
        if (m_AudioSource == null) // 对应的 GameObject 已被销毁。
        {
            fatal = true;
            return false;
        }

        // 预约或延迟播放的音源在开播时间前会报告 isPlaying == false。
        // 若将其误认成已播完，声音还没响就会回收发声器，
        // 使用 DelayMin、DelayMax 或 StopDelay 的 SoundDef 也会被静默丢掉。
        // 预约时间到之前，Unity 还会报告声源没有在播。把等待中的声源也算在使用中，才能等到声音真正开始。
        if (m_PendingStartDsp > 0.0) {
            if (AudioSettings.dspTime < m_PendingStartDsp) {
                count++;
                return true;
            }
            m_PendingStartDsp = 0.0;
        }
        if (m_AudioSource.isPlaying)
            // 检查 AudioSource 是否仍在播。正在停则处理淡出，并更新循环次数。
        {
            m_PendingStartDsp = 0.0;
            UpdateAudioParameters(soundEmitter);
            CheckForAudioSourceLoop();
            count++;
            return true;
        }

        // 所有 AudioSource 已播完。只要未预留，
        // 且 SoundEmitter 也不需要重复播放，就可归还 SoundGameObject。
        if (soundEmitter.Reserved != SoundEmitter.ReservedInfo.ReservedEmitterAndAudioSources)
            if (repeatCount < 2)
                Kill(soundEmitter); // 没有预留，可供另一个发声器再用。
        return false; // 播放已结束。
    }

    /// <summary>
    /// 检查刚才是否完成一轮循环，并更新计数。
    /// </summary>
    /// <returns>观察到一次循环时为 true，否则为 false。</returns>
    public bool CheckForAudioSourceLoop() {
        bool looped = false;
        if (m_CurrentLoopCount > 1 && m_AudioSource.timeSamples < m_LastTimeSamples)
            // 如果音频刚好绕回开头，就把剩余循环次数减一。
        {
            m_CurrentLoopCount--;
            looped = true;
            if (m_CurrentLoopCount == 1) m_AudioSource.loop = false;
        }
        m_LastTimeSamples = m_AudioSource.timeSamples;
        return looped;
    }

    /// <summary>
    /// 确认关联的 GameObject 仍存在且可访问。
    /// </summary>
    /// <returns>对象仍有效时为 true，对应 GameObject 已销毁时为 false。</returns>
    public bool IsValid() {
        return m_AudioSource != null ? true : false;
        // SoundGameObject 可能已换过父节点，而新父节点后来又被销毁。
    }

    /// <summary>
    /// 按 SoundDef 提供的全部参数，让 AudioSource 开始播放。
    /// 配好音频片段、音高、音量、空间位置及所需处理效果。
    /// </summary>
    /// <param name="listenerTransform">音频监听器的 Transform。</param>
    /// <param name="soundDef">包含所有播放参数的声音定义。</param>
    /// <param name="soundEmitter">控制该对象的发声器。</param>
    /// <param name="spatialBlend">空间混合量，0 为 2D，1 为 3D。</param>
    /// <param name="mixerGroups">提供输出路由的音频混音组列表。</param>
    public void TriggerAudioSource(
        Transform listenerTransform,
        SoundDef soundDef,
        SoundEmitter soundEmitter,
        float spatialBlend,
        AudioMixerGroup[] mixerGroups) {
        m_SoundDef = soundDef;
        var soundDefOverrideData = soundEmitter.SoundDefOverrideInfo;
        m_CalculateDistance = InitDistanceCurves(soundDef);
        m_ListenerTransform = listenerTransform;
        m_Occluded = false;
        m_OcclusionGain = 1;
        m_OcclusionCutoff = 22000;
        m_NextOcclusion = 0;
        m_AudioSource.priority = soundDef.Priority;

        // 按索引方式从列表选片段，例如随机、指定或顺序。
        // 播放开始前先固定这次用的片段和随机音高。音高从音分换成播放倍率，1200 音分对应翻倍。
        uint clipIndex = GetClipIndex(soundDef, soundEmitter);
        m_AudioSource.clip = soundDef.Clips[(int)clipIndex];

        // 音高从音分换成线性倍率。100 音分等于一个半音。
        float cents = soundDefOverrideData.BasePitchInCents +
                      Random.Range(soundDef.PitchAndVolumeInfo.PitchMin, soundDef.PitchAndVolumeInfo.PitchMax);
        m_AudioSource.pitch = Mathf.Pow(2.0f, cents / 1200.0f);

        // 设置最小和最大距离衰减。
        m_AudioSource.minDistance = soundDef.DistanceInfo.VolumeDistMin;
        m_AudioSource.maxDistance = soundDef.DistanceInfo.VolumeDistMax;
        m_AudioSource.rolloffMode = soundDef.DistanceInfo.VolumeRolloffMode;
#if UNITY_EDITOR
        m_AudioSource.gameObject.name = m_DebugName + " (" + soundDef.name + " " + s_Counter + ")";
        s_Counter++;
#endif

        // 音量结合 SoundGameObject 的随机值、soundDef 与 SoundEmitter 的音量倍率。
        float vMin = AmplitudeFromDecibel(soundDef.PitchAndVolumeInfo.VolumeMin);
        float vMax = AmplitudeFromDecibel(soundDef.PitchAndVolumeInfo.VolumeMax);
        m_Volume = Random.Range(vMin, vMax);

        // 设置是否循环。Update 按实际循环次数递减，到最后一轮时停播。
        // AudioSource 按循环方式播放，代码再用样本位置的回绕次数限制实际循环次数。配置为零时可以一直循环到调用方停止它。
        m_AudioSource.loop = soundDef.RepeatInfo.LoopCount != 1; // loopcount!=1 时启用循环设置。
        // 0 表示无限循环，1 表示播一次，2 表示播两次，以此类推。
        m_CurrentLoopCount = soundDef.RepeatInfo.LoopCount;
        m_LastTimeSamples = 0;

        // 设置目标混音组。
        if (mixerGroups != null) m_AudioSource.outputAudioMixerGroup = mixerGroups[(int)soundDef.MixerGroup];

        // 将播放起点设到指定采样位置。
        float startPercentOffset = Random.Range(soundDef.StartStopInfo.StartOffsetPercentMin,
            soundDef.StartStopInfo.StartOffsetPercentMax) / 100.0f;
        // 播放偏移以片段长度的比例给出，换成样本编号后能跳到想播放的位置。单次覆盖常用来截取装填动作的一小段。
        startPercentOffset = Mathf.Clamp(soundDefOverrideData.StartOffsetFraction ?? startPercentOffset, 0, .999f);
        m_AudioSource.timeSamples = (int)((float)m_AudioSource.clip.samples * startPercentOffset);
        m_AudioSource.dopplerLevel = soundDef.DistanceInfo.DopplerScale;
        m_LPFRandomValue = soundDefOverrideData.BaseLowPassCutoff +
                           Random.Range(soundDef.LowPassFilter.CutoffMin, soundDef.LowPassFilter.CutoffMax);
        m_HPFRandomValue = Random.Range(soundDef.HighPassFilter.CutoffMin, soundDef.HighPassFilter.CutoffMax);
        m_SpatialBlendValue = spatialBlend;
        if (m_DistortionFilter != null && m_DistortionFilter.enabled)
            m_DistortionFilter.distortionLevel = Random.Range(soundDef.DistortionFilter.DistortionMin,
                soundDef.DistortionFilter.DistortionMax);

        // 设置左右声道的平衡。
        // 在计算 3D 位置影响前应用这个声像。
        // 因此实际空间化之前，先能单独规定左右的起始分配。
        m_AudioSource.panStereo = Random.Range(soundDef.DistanceInfo.PanMin, soundDef.DistanceInfo.PanMax);
        UpdateAudioParameters(soundEmitter);

        // 按需延迟开始播放。
        // 配有 stopDelay（经过指定秒数后停止 audioSource）时，使用 PlayScheduled 和 SetScheduledEndTime。
        float delay = Random.Range(soundDef.StartStopInfo.DelayMin, soundDef.StartStopInfo.DelayMax);
        // 用音频系统自己的 DSP 时钟预约开播和停播。画面帧率抖动时，声音的开始和结束仍按自己的时刻处理。
        float stopAfter = soundDefOverrideData.StopAfterSeconds ?? soundDef.StartStopInfo.StopDelay;
        if (stopAfter > 0) {
            double startTime = AudioSettings.dspTime;
            startTime += delay;
            double endTime = startTime + stopAfter;
            m_PendingStartDsp = startTime;
            m_AudioSource.PlayScheduled(startTime);
            m_AudioSource.SetScheduledEndTime(endTime);
        } else {
            if (delay > 0.0f) {
                m_PendingStartDsp = AudioSettings.dspTime + delay;
                m_AudioSource.PlayDelayed(delay);
            } else {
                m_PendingStartDsp = 0.0;
                m_AudioSource.Play();
            }
        }
    }

    /// <summary>
    /// 设置 SoundGameObject 在世界中的位置，供空间音频使用。
    /// </summary>
    /// <param name="position">世界坐标。</param>
    public void SetPosition(Vector3 position) {
        m_AudioSource.transform.position = position;
        m_PositionType = PositionType.Position;
        m_Parent = m_SoundGameObjectPool;
    }

    /// <summary>
    /// 把 SoundGameObject 挂到父 Transform 下，再应用局部位置偏移。
    /// </summary>
    /// <param name="parent">要挂靠的父 Transform。</param>
    /// <param name="localPosition">相对于父节点的位置偏移。</param>
    public void SetParent(Transform parent, Vector3 localPosition) {
        m_AudioSource.transform.parent = parent;
        m_AudioSource.transform.localPosition = localPosition;
        m_Parent = parent.gameObject;
        m_PositionType = PositionType.ParentTransform;
    }

    private float AmplitudeFromDecibel(float decibel) {
        if (decibel <= SOUND_VOL_CUTOFF) return 0;
        return Mathf.Pow(2.0f, decibel / 6.0f);
    }

    public void RefreshListener(Transform listener) { m_ListenerTransform = listener; }
    private Vector3 GetPosition() { return m_AudioSource ? m_AudioSource.transform.position : Vector3.zero; }

    private bool InitDistanceCurves(SoundDef soundDef) {
        bool calcDistance = false;
        m_LPFCurveNormalizedResult = 1;
        m_HPFCurveNormalizedResult = 1;
        m_SpatialCurveNormalizedResult = 1;
#if UNITY_EDITOR
        if (!EditorApplication.isPlaying)
            return false; // 从检视面板测试音频时，无需计算监听器距离。
#endif
        calcDistance = EnableAudioFilterComponents(soundDef);
        return calcDistance;
    }

    private void UpdateDistanceCurves() {
        if (!m_CalculateDistance || m_ListenerTransform == null) return;
        var position = GetPosition();
        var listenerPosition = m_ListenerTransform.position;
        // 三个距离曲线各管一件事。低通和高通决定哪些频率更明显，空间感曲线决定这个声源听起来有多像在具体位置。
        float dist = Vector3.Distance(position, listenerPosition);
        var distanceInfo = m_SoundDef.DistanceInfo;
        if (m_LowPassFilter != null && m_LowPassFilter.enabled) {
            float v = Mathf.Clamp(dist, 0, distanceInfo.LPF_MaxDistance);
            m_LPFCurveNormalizedResult = 1.0f - m_Curve.GetNormalizedCurveValue(distanceInfo.LPFRollOffCurveType,
                v / distanceInfo.LPF_MaxDistance);
        }
        if (m_HighPassFilter != null && m_HighPassFilter.enabled) {
            float v = Mathf.Clamp(dist, 0, distanceInfo.HPF_MaxDistance);
            m_HPFCurveNormalizedResult = 1.0f - m_Curve.GetNormalizedCurveValue(distanceInfo.HPFRollOffCurveType,
                v / distanceInfo.HPF_MaxDistance);
        }
        if (distanceInfo.SpatialBlend_MaxDistance > 0) {
            float v = Mathf.Clamp(dist, 0, distanceInfo.SpatialBlend_MaxDistance);
            m_SpatialCurveNormalizedResult = m_Curve.GetNormalizedCurveValue(distanceInfo.SpatialBlendCurveType,
                v / distanceInfo.SpatialBlend_MaxDistance);
        }
    }

    private void SetDistanceCurveAudio() {
        if (m_LowPassFilter != null && m_LowPassFilter.enabled) {
            float cutoff = m_LPFRandomValue * m_LPFCurveNormalizedResult;
            if (m_SoundDef.DistanceInfo.LPFRollOffCurveType != Interpolator.CurveType.None &&
                m_SoundDef.DistanceInfo.LPF_MinCutoff > 0 && cutoff < m_SoundDef.DistanceInfo.LPF_MinCutoff)
                cutoff = m_SoundDef.DistanceInfo.LPF_MinCutoff;
            m_LowPassFilter.cutoffFrequency = cutoff;
        }
        if (m_HighPassFilter != null && m_HighPassFilter.enabled) {
            float cutoff = m_HPFRandomValue * m_HPFCurveNormalizedResult;
            if (m_SoundDef.DistanceInfo.HPFRollOffCurveType != Interpolator.CurveType.None &&
                m_SoundDef.DistanceInfo.HPF_MinCutoff > 0 && cutoff < m_SoundDef.DistanceInfo.HPF_MinCutoff)
                cutoff = m_SoundDef.DistanceInfo.HPF_MinCutoff;
            m_HighPassFilter.cutoffFrequency = cutoff;
        }
#if UNITY_EDITOR
        if (!EditorApplication.isPlaying)
            m_AudioSource.spatialBlend = 0;
        // 在检视面板试听时关闭 SpatialBlend，让声音能从所有扬声器听见。
        else
#endif
            m_AudioSource.spatialBlend = m_SpatialBlendValue * m_SpatialCurveNormalizedResult;
    }

    // 把任务自己的音量和淡出、障碍物造成的衰减、短时混音压低合在一起。每个声源只需更新自己的最后一个音量值。
    private void UpdateAudioParameters(SoundEmitter soundEmitter) {
        if (m_CalculateDistance) UpdateDistanceCurves();
        SetDistanceCurveAudio();
        if (m_LowPassFilter && !m_SoundDef.LowPassFilter.EnableComponent) m_LowPassFilter.enabled = false;
        UpdateOcclusion();
        if (m_LowPassFilter && m_SoundDef.Occlusion && m_SpatialBlendValue > .5f) {
            float distanceCutoff = m_SoundDef.LowPassFilter.EnableComponent ? m_LowPassFilter.cutoffFrequency : 22000;
            m_LowPassFilter.enabled = true;
            m_LowPassFilter.cutoffFrequency = Mathf.Min(distanceCutoff, m_OcclusionCutoff);
        }
        m_AudioSource.volume = m_Volume * soundEmitter.Volume * soundEmitter.SoundDefOverrideInfo.VolumeScale *
                               m_OcclusionGain * soundEmitter.FadeOutTime.GetValue() *
                               MoonkovSoundMix.Gain(m_SoundDef.Role);
    }

    private uint GetClipIndex(SoundDef soundDef, SoundEmitter soundEmitter) {
        uint clipIndex = 0;
        // 先按音效的规则选片。调用者指定、随机或顺序轮播共用同一片段表，最后都取得片段表范围内的编号。
        if (soundDef.PlaybackType == SoundDef.PlaybackTypes.User) {
            clipIndex = soundEmitter.UserClipIndex;
            clipIndex %= (uint)soundDef.Clips.Count;
        } else if (soundDef.PlaybackType == SoundDef.PlaybackTypes.RandomNotLast) {
            // SoundSystem 按 SoundDef 选出片段。发声器对象复用不会改变这个选片段记录。
            clipIndex = soundEmitter.UserClipIndex % (uint)soundDef.Clips.Count;
        } else if (soundDef.PlaybackType == SoundDef.PlaybackTypes.Random) {
            clipIndex = (uint)Random.Range(0, soundDef.Clips.Count);
        } else if (soundDef.PlaybackType == SoundDef.PlaybackTypes.Sequential) {
            soundEmitter.SequentialClipIndex++;
            soundEmitter.SequentialClipIndex %= (uint)soundDef.Clips.Count;
            // 从用户指定改成顺序选择，或反向切换时，先保证索引未越界。
            clipIndex = soundEmitter.SequentialClipIndex;
        }
        return clipIndex;
    }

    // 有距离感的声音会抽样检查到听者之间是否被挡住。滤波和音量逐步靠近目标，避免跨过遮挡边缘时声音突然跳变。
    private void UpdateOcclusion() {
        if (!m_SoundDef.Occlusion || m_SpatialBlendValue <= .5f || !m_ListenerTransform) return;
        // 所有声源共用每帧遮挡射线额度。每个声源自己记下次采样时间，这样开枪和角色很多时也能控制物理查询数量。
        if (s_OcclusionFrame != Time.frameCount) {
            s_OcclusionFrame = Time.frameCount;
            s_OcclusionBudget = 6;
        }
        if (Time.unscaledTime >= m_NextOcclusion && s_OcclusionBudget > 0) {
            s_OcclusionBudget--;
            m_NextOcclusion = Time.unscaledTime + .16f;
            Vector3 start = m_ListenerTransform.position, delta = GetPosition() - start;
            float distance = delta.magnitude;
            m_Occluded = false;
            if (distance > 1.2f) {
                var scene = m_AudioSource.gameObject.scene.GetPhysicsScene();
                int count = scene.Raycast(start, delta / distance, s_Obstacles, distance - .35f, s_OcclusionMask,
                    QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++) {
                    var hit = s_Obstacles[i];
                    // 刚从听者身体出发，射线可能擦到玩家自己。还要排除发声对象的子物件，剩下真正在听者与声源之间的障碍才算遮挡。
                    if (hit.distance < .3f || hit.collider.transform.IsChildOf(m_ListenerTransform.root) ||
                        (m_PositionType == PositionType.ParentTransform && m_Parent &&
                         hit.collider.transform.IsChildOf(m_Parent.transform)))
                        continue;
                    m_Occluded = true;
                    break;
                }
            }
        }
        float blend = 1 - Mathf.Exp(-Time.unscaledDeltaTime * 14);
        m_OcclusionGain = Mathf.Lerp(m_OcclusionGain, m_Occluded ? .42f : 1, blend);
        m_OcclusionCutoff = Mathf.Lerp(m_OcclusionCutoff, m_Occluded ? 1500 : 22000, blend);
    }
}