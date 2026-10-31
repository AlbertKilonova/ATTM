using System.Runtime.InteropServices;

namespace ATTM.Core.Playback;

public sealed record MidiDeviceInfo(int Index, string Name);

/// <summary>
/// winmm 的 MIDI 输出端口。直接向系统 MIDI 设备发送短消息
/// （Microsoft GS Wavetable Synth、loopMIDI、Pianoteq、DAW 虚拟端口等）。
/// </summary>
public sealed class Win32MidiOutput : IMidiOutput
{
    private const uint MidiMapperDeviceId = unchecked((uint)-1);

    private IntPtr _handle;
    private bool _disposed;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiOutCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;

        public ushort Technology;
        public ushort Voices;
        public ushort Notes;
        public ushort ChannelMask;
        public uint Support;
    }

    [DllImport("winmm.dll", EntryPoint = "midiOutGetNumDevs")]
    private static extern uint MidiOutGetNumDevs();

    [DllImport("winmm.dll", EntryPoint = "midiOutGetDevCapsW", CharSet = CharSet.Unicode)]
    private static extern uint MidiOutGetDevCaps(nuint deviceId, ref MidiOutCaps caps, uint size);

    [DllImport("winmm.dll", EntryPoint = "midiOutOpen")]
    private static extern uint MidiOutOpen(out IntPtr handle, uint deviceId, IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll", EntryPoint = "midiOutClose")]
    private static extern uint MidiOutClose(IntPtr handle);

    [DllImport("winmm.dll", EntryPoint = "midiOutShortMsg")]
    private static extern uint MidiOutShortMsg(IntPtr handle, uint message);

    [DllImport("winmm.dll", EntryPoint = "midiOutReset")]
    private static extern uint MidiOutReset(IntPtr handle);

    /// <summary>枚举所有 MIDI 输出端口。</summary>
    public static IReadOnlyList<MidiDeviceInfo> GetDevices()
    {
        var devices = new List<MidiDeviceInfo>();
        var count = MidiOutGetNumDevs();

        for (nuint i = 0; i < count; i++)
        {
            var caps = new MidiOutCaps { Name = string.Empty };
            var size = (uint)Marshal.SizeOf<MidiOutCaps>();
            if (MidiOutGetDevCaps(i, ref caps, size) != 0) continue;

            devices.Add(new MidiDeviceInfo((int)i, caps.Name));
        }

        return devices;
    }

    public string Name { get; private set; } = "(MIDI)";

    public bool IsOpen => _handle != IntPtr.Zero;

    /// <param name="deviceIndex">设备序号；null 表示使用系统默认映射器（MIDI Mapper）。</param>
    public static bool TryOpen(int? deviceIndex, out Win32MidiOutput? output, out string error)
    {
        output = null;
        error = string.Empty;

        var deviceId = deviceIndex is >= 0 ? (uint)deviceIndex.Value : MidiMapperDeviceId;
        var result = MidiOutOpen(out var handle, deviceId, IntPtr.Zero, IntPtr.Zero, 0);

        if (result != 0)
        {
            error = $"无法打开 MIDI 输出设备（winmm 错误码 {result}）";
            return false;
        }

        var name = deviceIndex is >= 0
            ? GetDevices().FirstOrDefault(d => d.Index == deviceIndex)?.Name ?? "(MIDI)"
            : "系统默认 MIDI 输出";

        output = new Win32MidiOutput { _handle = handle, Name = name };
        return true;
    }

    public void NoteOn(int channel, int pitch, int velocity)
        => SendShort((uint)(0x90 | (channel & 0x0F)) | ((uint)(pitch & 0x7F) << 8) | ((uint)(velocity & 0x7F) << 16));

    public void NoteOff(int channel, int pitch)
        => SendShort((uint)(0x80 | (channel & 0x0F)) | ((uint)(pitch & 0x7F) << 8));

    public void ControlChange(int channel, int controller, int value)
        => SendShort((uint)(0xB0 | (channel & 0x0F)) | ((uint)(controller & 0x7F) << 8) | ((uint)(value & 0x7F) << 16));

    public void AllNotesOff(int channel)
    {
        ControlChange(channel, 123, 0); // All Notes Off
        ControlChange(channel, 120, 0); // All Sound Off
    }

    public void ResetControllers(int channel)
    {
        AllNotesOff(channel);
        ControlChange(channel, 64, 0);  // 延音踏板
        ControlChange(channel, 66, 0);  // 持音踏板
        ControlChange(channel, 67, 0);  // 弱音踏板
    }

    private void SendShort(uint message)
    {
        if (_handle == IntPtr.Zero || _disposed) return;
        MidiOutShortMsg(_handle, message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle == IntPtr.Zero) return;

        try
        {
            AllNotesOff(0);
            ResetControllers(0);
            MidiOutReset(_handle);
        }
        catch
        {
            // 设备被拔出时忽略
        }

        MidiOutClose(_handle);
        _handle = IntPtr.Zero;
    }
}
