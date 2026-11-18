// 创建游戏时提前准备一批带 AudioSource 和滤波器的对象，发声时借用，播完再还。声源的父物体跨场景保留。
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 管理带 AudioSource 的 SoundGameObjects 对象池。
/// 集中分配和归还音源，避免每次播放都创建与销毁。
/// </summary> 
public class SoundGameObjectPool {
    // 独立验证可以主动清理自己的对象池。正常游戏仍由 GameManager 管理。
    public void Dispose() {
        if (m_SourceHolder) Object.Destroy(m_SourceHolder);
        SoundGameObjectList?.Clear();
        NextSoundGameObject = null;
        AvailableSoundObjectCount = 0;
    }

    public List<SoundGameObject> SoundGameObjectList;
    public SoundGameObject NextSoundGameObject;
    public int AvailableSoundObjectCount;
    private GameObject m_SourceHolder;
#if UNITY_EDITOR
    private int m_SoundObjectCounter;
#endif

    /// <summary>
    /// 按给定容量初始化声音对象池。
    /// 提前造好所需对象，空闲对象用链表串联，方便快速取用。
    /// </summary>
    /// <param name="parentGameObjectName">容纳全部声音对象的父 GameObject 名称。</param>
    /// <param name="maxSoundGameObjects">要创建的声音对象上限。</param>
    public SoundGameObjectPool(string parentGameObjectName, int maxSoundGameObjects) {
        if (maxSoundGameObjects <= 0) return;
        SoundGameObjectList = new List<SoundGameObject>();
        m_SourceHolder =
            new GameObject(parentGameObjectName); // 所有 SoundGameObjects 都建在这个父节点下。
        // 菜单和战局共用同一批声源。池子挂在常驻父节点下，播放期间跟随别的物体，归还时再接回池。
        GameObject.DontDestroyOnLoad(m_SourceHolder);
        // 初始化时把所有槽位连成一圈，并记下还可借多少。发声任务据此决定还有没有足够的声源叠出完整音效。
        AvailableSoundObjectCount = maxSoundGameObjects;
        SoundGameObject lastAllocated = null;
        for (int i = 0; i < maxSoundGameObjects; i++) {
            var soundGameObject = Create();
            if (lastAllocated != null) lastAllocated.NextAvailable = soundGameObject;
            lastAllocated = soundGameObject;
            SoundGameObjectList.Add(soundGameObject);
        }
        SoundGameObjectList[maxSoundGameObjects - 1].NextAvailable = SoundGameObjectList[0];
        NextSoundGameObject = SoundGameObjectList[0];
    }

    /// <summary>
    /// 创建一个 SoundGameObject，并装上播放所需的音频组件。
    /// 添加 AudioSource、AudioDistortionFilter、AudioLowPassFilter 和 AudioHighPassFilter。
    /// </summary>
    /// <returns>可供播放的全新 SoundGameObject。</returns>
    public SoundGameObject Create() {
        GameObject go;
#if UNITY_EDITOR
        go = new GameObject("SGO" + " " + m_SoundObjectCounter++);
        // 用名称加计数给 GameObject 命名，方便在 Hierarchy 窗口中定位。
#else
        go = new GameObject("SGO");
#endif
        go.transform.parent = m_SourceHolder.transform;
        var audioComponentData = new SoundGameObject.AudioComponentData {
            ASource = go.AddComponent<AudioSource>(),
            DistortionFilter = go.AddComponent<AudioDistortionFilter>(),
            LowPassFilter = go.AddComponent<AudioLowPassFilter>(),
            HighPassFilter = go.AddComponent<AudioHighPassFilter>()
        };
        var audioSourceObject = new SoundGameObject(m_SourceHolder, audioComponentData);
        return audioSourceObject;
    }

    /// <summary>
    /// 用新对象替换已失效的 SoundGameObject，并修复空闲链表。
    /// 原对象随父节点被销毁后，由这里补建并接回池子。
    /// </summary>
    /// <param name="oldSGO">要替换的失效 SoundGameObject。</param>
    /// <param name="newSGO">用来补上的新 SoundGameObject。</param>
    /// <returns>替换成功为 true，找不到原对象时为 false。</returns>
    public bool Replace(SoundGameObject oldSGO, SoundGameObject newSGO) {
        for (int i = 0; i < SoundGameObjectList.Count; i++)
            // 声源被跟随对象一并销毁后，用新对象补同一个位置。链表里原来指向旧对象的链接也要指到新对象。
            if (SoundGameObjectList[i] == oldSGO) {
                SoundGameObjectList[i] = newSGO; // 用新 SoundGameObject 替代旧对象。
                // 父节点销毁时可能连带销毁它，池子需要补位。
                for (int j = 0; j < SoundGameObjectList.Count; j++)
                    if (SoundGameObjectList[j].NextAvailable != null && SoundGameObjectList[j].NextAvailable == oldSGO)
                        SoundGameObjectList[j].NextAvailable = newSGO;
                // 只有对象在空闲并且还挂在 SoundGameObjectPool 下时，才能在这里找到。
                // 平时很少发生。
                return true;
            }
        return false; // 无法修复空闲链表。
    }
}