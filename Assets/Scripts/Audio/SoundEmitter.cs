// 一个 SoundEmitter 代表一次发声任务，管理这一声用到的一个或多个 SoundGameObject。它负责重复次数、淡出以及把声源还回池子。
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Random = UnityEngine.Random;

[Serializable]
public class SoundEmitter {
    public enum ReservedInfo {
        FreeAfterPlaybackCompletes,
        ReservedEmitter,
        ReservedEmitterAndAudioSources
    };

    /// <summary>
    /// 存放可在播放时覆盖的 SoundDef 参数。
    /// 可为单次播放设不同的音高、音量和滤波。
    /// </summary>
    public class SoundDefOverrideData {
        // null 沿用资源的空间音设置，武器可按操作者身份覆盖单次播放。
        public float? SpatialBlend;

        // 可为有指定时长的武器机构声设置片段窗口。null 沿用素材组默认设定。
        public float? StartOffsetFraction;
        public float? StopAfterSeconds;
        public float BasePitchInCents; // 从 SoundDef 复制，方便调用者按需改写。
        public float VolumeScale; // 从 SoundDef 复制。
        public float BaseLowPassCutoff; // 从 SoundDef 复制。

        public SoundDefOverrideData() {
            BaseLowPassCutoff = 0;
            BasePitchInCents = 0;
            VolumeScale = 1;
        }

        public SoundDefOverrideData(SoundDef soundDef) {
            BasePitchInCents = soundDef.BasePitchInCents;
            VolumeScale = soundDef.VolumeScale;
            BaseLowPassCutoff = soundDef.BaseLowPassCutoff;
        }
    }

    public bool Allocated; // true 表示这个发声器已分配使用。

    public ReservedInfo
        Reserved; // 发声器的预留方式。使用 RandomNotLast 或 Sequential 选片段时，需保留这个发声器。

    [NonSerialized]
    public List<SoundGameObject>
        ActiveSoundGameObjects; // 当前为此 SoundDef 分配的 SoundGameObjects 列表，数量按 SoundDef.PlayCount。

    public uint RandomClipIndex; // 记录上次随机播的片段编号，使下次随机时能跳过它。
    public uint UserClipIndex; // SoundDef 的片段选择设为 User 时，调用者可在这里指定编号。
    public uint SequentialClipIndex; // SoundDef 的片段选择设为 Sequential 时使用这个编号。

    public float
        Volume; // 发声器基础音量。实际音量为 Volume × SoundDefOverrideInfo.VolumeScale × RandomVol(min/max) × KeyOffFadeOutVol。

    [NonSerialized] public SoundDefOverrideData SoundDefOverrideInfo;

    [NonSerialized]
    public Interpolator FadeOutTime; // 收到停止请求时，SoundGameObject 渐弱的秒数。

    public int SeqId; // 这个 SoundEmitter 的 ID，用来确认播放、停止等请求仍对应本次分配。
    public bool Active; // true 表示还有活动的 SoundGameObjects，或者 SoundEmitter 仍被预留。

    private SoundGameObjectPool
        m_SoundGameObjectPool; // 此发声器可以从这个池里取 SoundGameObjects。

    public SoundDef Definition => m_SoundDef;
    private SoundDef m_SoundDef; // 这个发声器正在使用的 SoundDef。
    private int m_RepeatCount; // 记住当前重复次数，对照 SoundDef RandomRepeat(min/max)；填 0 或 1 都会播一次。
    private Transform m_ListenerTransform;

    /// <summary>
    /// 从 SoundDef 复制参数，给运行时播放留出覆盖空间。
    /// </summary>
    /// <param name="soundDef">提供原始参数的 SoundDef。</param>
    public void CopyOverrideDataFromSoundDef(SoundDef soundDef) {
        SoundDefOverrideInfo.SpatialBlend = null;
        SoundDefOverrideInfo.StartOffsetFraction = null;
        SoundDefOverrideInfo.StopAfterSeconds = null;
        SoundDefOverrideInfo.BasePitchInCents = soundDef.BasePitchInCents;
        SoundDefOverrideInfo.VolumeScale = soundDef.VolumeScale;
        SoundDefOverrideInfo.BaseLowPassCutoff = soundDef.BaseLowPassCutoff;
    }

    /// <summary>
    /// 用指定的声音对象池创建一个新的 SoundEmitter。
    /// </summary>
    /// <param name="soundGameObjectPool">播放时分配声音对象的池。</param>
    public SoundEmitter(SoundGameObjectPool soundGameObjectPool) { Init(soundGameObjectPool); }

    /// <summary>
    /// 用默认设置和指定对象池初始化发声器。
    /// </summary>
    /// <param name="soundGameObjectPool">该发声器可取声音对象的池。</param>
    public void Init(SoundGameObjectPool soundGameObjectPool) {
        Allocated = false;
        Active = false;
        ActiveSoundGameObjects = new List<SoundGameObject>();
        Volume = 1.0f;
        SequentialClipIndex = 0;
        RandomClipIndex = 0; // 刚分配到本次播放，还没有播过任何片段。
        UserClipIndex = 0;
        m_RepeatCount = 0;
        Reserved = ReservedInfo.FreeAfterPlaybackCompletes;
        m_SoundGameObjectPool = soundGameObjectPool;
        SoundDefOverrideInfo = new SoundDefOverrideData();

        // FadeOutTime 也用作停止标记。Update() 读到 0 就结束这个发声器。
        // 池中的发声器会反复分配，因此每次初始化都要重置标记。
        // 例如船舱环境音在出发时会淡出。若保留上次淡出结果，
        // 这个对象被下一次声音复用后，
        // 第一次 Update() 就会把新声音停掉。
        if (FadeOutTime == null)
            FadeOutTime = new Interpolator(1.0f, Interpolator.CurveType.Linear);
        else
            FadeOutTime.SetValue(1.0f);
    }

    /// <summary>
    /// 取消预留并结束播放，让发声器可重新分配。
    /// </summary>
    public void UnreserveAndKill() {
        // 彻底清理时先解除保留。之后 Kill 才能把任务和它握着的声源都放回可借状态。
        Reserved = ReservedInfo.FreeAfterPlaybackCompletes;
        Kill(); // 停掉所有 SoundGameObjects 并回收发声器。
    }

    /// <summary>
    /// 结束发声器的播放。若未被预留，可以交还给池子复用。
    /// </summary>
    public void Kill() {
        Stop(); // 停止发声器，根据预留状态处理它的 SoundGameObjects。
        if (Reserved == ReservedInfo.FreeAfterPlaybackCompletes) // 未预留时归还发声器。
            Allocated = false;
    }

    /// <summary>
    /// 按对象是否预留来停止发声器和下面的声音对象。
    /// </summary>
    public void Stop() {
        // 只停一会儿的常驻声音可以继续占着声源，重新响时少一次借用。普通一次性声音停止时立即还给池。
        if (Reserved != ReservedInfo.ReservedEmitterAndAudioSources)
            KillSoundGameObjects(); // 结束这些 SoundGameObjects，使其他发声器能再次取用。
        else
            foreach (var soundGameObject in
                     ActiveSoundGameObjects) // 已为此发声器预留 SoundGameObjects 时，只停 AudioSource，仍保留它们。
                soundGameObject.StopAudioSource();
        Active = false;
    }

    /// <summary>
    /// 设置发声器的音量。
    /// </summary>
    /// <param name="volume">音量倍率，通常为 0 到 1。</param>
    public void SetVolume(float volume) { Volume = volume; }

    /// <summary>
    /// 用当前 SoundDef 和活动的 SoundGameObjects 播放发声器。
    /// </summary>
    /// <param name="listenerTransform">用于计算空间位置的音频监听器 Transform。</param>
    /// <param name="mixerGroups">用来选择输出路由的音频混音组列表。</param>
    public void Play(Transform listenerTransform, AudioMixerGroup[] mixerGroups) {
        if (m_SoundDef == null || m_SoundDef.Clips == null || m_SoundDef.Clips.Count == 0) return;
        m_ListenerTransform = listenerTransform;
        float spatialBlend;
#if UNITY_EDITOR
        if (!EditorApplication.isPlaying)
            spatialBlend =
                0; // 在检视面板试听时关闭 SpatialBlend，让声音能从所有扬声器听见。
        else
#endif
            // 某一声的调用者可以在 SoundInfo 里面覆盖空间感。没有覆盖时用配置里的距离设置，再把结果传给每一层声源。
            spatialBlend = Mathf.Clamp01(SoundDefOverrideInfo.SpatialBlend ?? m_SoundDef.DistanceInfo.SpatialBlend);
        foreach (var soundGameObject in ActiveSoundGameObjects)
            soundGameObject.TriggerAudioSource(m_ListenerTransform, m_SoundDef, this, spatialBlend, mixerGroups);
    }

    /// <summary>
    /// 更新发声器，处理淡出、重复播放和 SoundGameObject 的回收。
    /// </summary>
    /// <param name="mixerGroups">音频混音组列表。</param>
    /// <returns>发声器仍活动时为 true，应移除时为 false。</returns>
    public bool Update(AudioMixerGroup[] mixerGroups, Transform listener = null) {
        if (!Active) return false;
        // 淡出走到零就结束这个发声任务。下一步每帧更新不必再检查已经听不见的声源。
        if (FadeOutTime.GetValue() == 0.0f) {
            Kill(); // 淡出完成时停止发声器，并处理所有音源，未预留时还可回收。
            return false;
        }
        // 菜单相机和玩家相机换听者时，正在播放的每层声源也要换参考点。距离衰减和遮挡随后会从新位置算。
        if (listener && listener != m_ListenerTransform) {
            m_ListenerTransform = listener;
            foreach (var source in ActiveSoundGameObjects) source.RefreshListener(listener);
        }
        int activeAudioSourceCounter = 0;
        foreach (var soundGameObject in ActiveSoundGameObjects) {
            bool fatal = false;
            soundGameObject.Update(this, m_RepeatCount, ref activeAudioSourceCounter, ref fatal);
            // 跟随目标销毁时可能连带删掉声源对象。修复池里的坏项并解除这个任务的占用，避免一处坏引用阻塞下次播放。
            if (fatal == true) // AudioSource 所在的 GameObject 已销毁，通常是因为父对象被销毁。停止并回收这一组声音。
            {
                ValidateSoundGameObjects();
                UnreserveAndKill(); // 取消预留并回收这个 SoundEmitter。
                return false;
            }
        }
        if (activeAudioSourceCounter > 0) // 检查是否还有 AudioSource 在播放。
            return true;
        // 一层播完时还要等其他同时播放的层。全部结束再开始下一轮重复，整声播完才回收任务。
        if (m_RepeatCount > 1) // 发声器的全部音源已播完，接着处理是否重复。
        {
            m_RepeatCount--;
            Play(m_ListenerTransform, mixerGroups); // 再播放一次这个发声器。
            return true;
        }
        Kill(); // 发声器已结束，未预留时交回池中。
        return false;
    }

    /// <summary>
    /// 开始在指定时长内淡出。
    /// </summary>
    /// <param name="fadeOutTime">淡出的秒数，0 表示立即停止。</param>
    public void FadeOut(float fadeOutTime) {
        if (fadeOutTime == 0.0f) { FadeOutTime.SetValue(0.0f); } else {
            FadeOutTime.SetValue(1.0f);
            // 渐弱只改变共享的音量系数。各声源在下一帧按照同一个淡出进度相乘，叠层可以一起退出。
            FadeOutTime.MoveTo(0.0f, fadeOutTime);
        }
    }

    /// <summary>
    /// 指定这个发声器接下来要用的 SoundDef。
    /// </summary>
    /// <param name="soundDef">含音频片段与设置的 SoundDef。</param>
    /// <returns>成功设置时为 true，传入 null 时为 false。</returns>
    public bool SetSoundDef(SoundDef soundDef) {
        if (soundDef == null) return false;
        m_SoundDef = soundDef;
        return true;
    }

    /// <summary>
    /// 激活发声器，并按 SoundDef 的配置算本次需要重复几次。
    /// </summary>
    public void Activate() {
        m_RepeatCount = Random.Range(m_SoundDef.RepeatInfo.RepeatMin, m_SoundDef.RepeatInfo.RepeatMax);
        Active = true;
    }

    /// <summary>
    /// 获取这个发声器当前的重复次数。
    /// </summary>
    /// <returns>剩下的重复次数。</returns>
    public int GetRepeatCount() { return m_RepeatCount; }

    /// <summary>
    /// 为指定位置的播放分配并准备 SoundGameObjects。
    /// 可以跟着父 Transform，也可以使用固定的世界坐标。
    /// </summary>
    /// <param name="parent">要挂靠的父 Transform，可为 null。</param>
    /// <param name="position">有父节点时是局部偏移，否则是世界坐标。</param>
    /// <returns>声音对象准备成功时为 true，否则为 false。</returns>
    // 开播前先取得足够的声源，再设定跟随物体或固定位置。准备失败时，SoundSystem 会负责收回这次借出的任务。
    public bool PrepareSoundGameObjects(Transform parent, Vector3 position) {
        if (ActiveSoundGameObjects.Count == 0 ||
            Reserved != ReservedInfo.ReservedEmitterAndAudioSources) // 尚未分配任何 SoundGameObjects。
        {
            KillSoundGameObjects();
            if (AllocateSoundGameObjects(m_SoundGameObjectPool, m_SoundDef.PlayCount) ==
                false) // 创建所需数量的 SoundGameObjects，也就是播放用的 AudioSource。
                return false; // 没有取到所需数量的 SoundGameObjects。
        }
        if (ValidateSoundGameObjects() ==
            false) // SoundGameObject 随父节点被销毁，无法继续播。清理，并取消发声器预留。
        {
            KillSoundGameObjects();
            UnreserveAndKill();
            return false;
        }

        // Reserved 为 ReservedInfo.ReserveEmitterAndAudioSources 时，ActiveSoundGameObjects 里仍会保留已预留的声音对象。
        foreach (var soundGameObject in ActiveSoundGameObjects) {
            soundGameObject.Enable(this);
            if (parent != null)
                soundGameObject.SetParent(parent,
                    position); // Position 作为 Transform.LocalPosition 使用，即相对父对象的偏移。
            else
                soundGameObject.SetPosition(position);
        }
        return true;
    }

    /// <summary>
    /// 确认 SoundGameObjects 仍在场景中。
    /// 如果声音对象挂在已销毁的 GameObject 下，需要补建一个 SoundGameObject。
    ///</summary>
    private bool ValidateSoundGameObjects() {
        bool isValid = true;
        for (int i = 0; i < ActiveSoundGameObjects.Count; i++) {
            var soundGameObject = ActiveSoundGameObjects[i];
            if (!soundGameObject.IsValid()) {
                var newSoundGameObject =
                    m_SoundGameObjectPool
                        .Create(); // 创建新的 SoundGameObject，补齐池子容量。
                m_SoundGameObjectPool.Replace(soundGameObject,
                    newSoundGameObject); // 在池子里用新对象替换已销毁的那个。
                ActiveSoundGameObjects[i] = newSoundGameObject;
                isValid = false;
            }
        }
        return isValid;
    }

    private void KillSoundGameObjects() {
        if (Reserved != ReservedInfo.ReservedEmitterAndAudioSources) {
            foreach (var soundGameObject in ActiveSoundGameObjects) {
                soundGameObject
                    .Kill(this); // 回收 SoundGameObject 时，把它挂回 SoundGameObjectPool 的父 GameObject。
                UpdateSoundObjectPoolLinkedList(
                    soundGameObject); // 把 soundGameObject 放回 SoundObjectPool 的链表，下一次可优先取它。
            }
            ActiveSoundGameObjects.Clear();
        }
    }

    private void UpdateSoundObjectPoolLinkedList(SoundGameObject newSoundGameObject) {
        var nextSoundGameObject = m_SoundGameObjectPool.NextSoundGameObject;
        m_SoundGameObjectPool.NextSoundGameObject =
            newSoundGameObject; // 将这个 SoundGameObject 设为池中下一个可用对象。
        newSoundGameObject.NextAvailable =
            nextSoundGameObject; // 让此前排在最前面的可用对象接到 newSoundGameObject 后面。
        m_SoundGameObjectPool.AvailableSoundObjectCount++;
    }

    private bool AllocateSoundGameObjects(SoundGameObjectPool soundGameObjectPool, int count) {
        if (soundGameObjectPool.AvailableSoundObjectCount <
            count) // 检查池子还有没有足够的空闲 SoundGameObjects 给这个发声器。
            return false;
        for (int i = 0; i < count; i++) {
            var soundGameObject = soundGameObjectPool.NextSoundGameObject;
            soundGameObjectPool.NextSoundGameObject = soundGameObject.NextAvailable;
            soundGameObject.SetActive(true);
            soundGameObjectPool.AvailableSoundObjectCount--;
            ActiveSoundGameObjects.Add(soundGameObject);
        }
        return true;
    }
}