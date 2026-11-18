// 调用方用这个句柄调音量或停止播放。对象会被复用，所以每次操作前还要核对借用序号，防止旧句柄停掉别人的声音。
/// <summary>
/// Represents a handle to a sound emitter that allows validation and tracking.
/// Used to ensure that operations on sound emitters are still valid even if the emitter has been recycled.
/// </summary>
public class SoundHandle {
    public SoundEmitter Emitter; // SoundEmitter that is used to play the SoundAudioObjects
    public int Seq;

    // 同一个 SoundEmitter 被后来一次播放借走时，引用地址会相同。只有序号也一致且还在分配中，这个句柄才属于原来的声音。
    /// <summary>
    /// Checks if this handle still references a valid sound emitter.
    /// An emitter may become invalid if it has finished playing and been reallocated.
    /// </summary>
    /// <returns>True if the handle is still valid, false otherwise</returns>
    public bool IsValid() {
        return Emitter != null && Emitter.Allocated &&
               Emitter.SeqId == Seq; // Are we still attached to the same SoundEmitter ?
    }

    /// <summary>
    /// Initializes a new empty SoundHandle.
    /// </summary>
    public SoundHandle() { Emitter = null; }

    /// <summary>
    /// Initializes a new SoundHandle with the specified sound emitter.
    /// </summary>
    /// <param name="soundEmitter">The sound emitter to create a handle for</param>
    public SoundHandle(SoundEmitter soundEmitter) {
        Emitter = soundEmitter;
        // 发句柄时把当前借用序号抄下来。后来的音量更新或停止请求会拿它重新核对。
        Seq = soundEmitter != null ? soundEmitter.SeqId : -1;
    }
}