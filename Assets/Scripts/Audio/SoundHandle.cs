// 调用方用这个句柄调音量或停止播放。对象会被复用，所以每次操作前还要核对借用序号，防止旧句柄停掉别人的声音。
/// <summary>
/// 指向发声器的播放句柄，可检验是否仍代表原来的声音。
/// 发声器会被复用。操作之前验证句柄，避免去暂停或改变后来的声音。
/// </summary>
public class SoundHandle {
    public SoundEmitter Emitter; // 驱动 SoundAudioObjects 播放的 SoundEmitter。
    public int Seq;

    // 同一个 SoundEmitter 被后来一次播放借走时，引用地址会相同。只有序号也一致且还在分配中，这个句柄才属于原来的声音。
    /// <summary>
    /// 检查句柄是否还指向可操作的那次发声器分配。
    /// 发声器播完后可能已经交给下一段声音，此时原句柄失效。
    /// </summary>
    /// <returns>句柄仍有效时为 true，否则为 false。</returns>
    public bool IsValid() {
        return Emitter != null && Emitter.Allocated &&
               Emitter.SeqId == Seq; // 检查这个 SoundEmitter 是否仍对应原来的播放编号。
    }

    /// <summary>
    /// 初始化一个空 SoundHandle。
    /// </summary>
    public SoundHandle() { Emitter = null; }

    /// <summary>
    /// 用指定发声器创建一个 SoundHandle。
    /// </summary>
    /// <param name="soundEmitter">这个句柄要引用的发声器。</param>
    public SoundHandle(SoundEmitter soundEmitter) {
        Emitter = soundEmitter;
        // 发句柄时把当前借用序号抄下来。后来的音量更新或停止请求会拿它重新核对。
        Seq = soundEmitter != null ? soundEmitter.SeqId : -1;
    }
}