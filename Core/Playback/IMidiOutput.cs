namespace ATTM.Core.Playback;

/// <summary>MIDI 输出端口的抽象，方便测试时替换成录音实现。</summary>
public interface IMidiOutput : IDisposable
{
    string Name { get; }

    bool IsOpen { get; }

    void NoteOn(int channel, int pitch, int velocity);

    void NoteOff(int channel, int pitch);

    void ControlChange(int channel, int controller, int value);

    void AllNotesOff(int channel);

    /// <summary>松开三种踏板 + 关闭所有残留音（开始播放前和停止时调用）。</summary>
    void ResetControllers(int channel);
}

/// <summary>没有可用设备时的静音输出：不发声，但时间轴与高亮照常工作。</summary>
public sealed class NullMidiOutput : IMidiOutput
{
    public string Name => "（静音：没有可用的 MIDI 输出设备）";

    public bool IsOpen => false;

    public void NoteOn(int channel, int pitch, int velocity) { }
    public void NoteOff(int channel, int pitch) { }
    public void ControlChange(int channel, int controller, int value) { }
    public void AllNotesOff(int channel) { }
    public void ResetControllers(int channel) { }
    public void Dispose() { }
}
