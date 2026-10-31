using System.IO;
using System.Text;
using ATTM.Core.Syntax;

namespace ATTM.Core.Midi;

/// <summary>
/// 标准 MIDI 文件（SMF format 0，单轨）写入器。
/// 规格：PPQ 480、通道 0、大钢琴（Program 0）。
/// </summary>
public static class MidiFileWriter
{
    public static void Write(Score score, string path, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("保存路径为空", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var trackData = BuildTrackData(score, random);

        using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);

        writer.Write("MThd"u8);
        WriteBigEndian32(writer, 6);
        WriteBigEndian16(writer, 0);                                  // format 0
        WriteBigEndian16(writer, 1);                                  // 单轨
        WriteBigEndian16(writer, (ushort)score.TicksPerQuarter);

        writer.Write("MTrk"u8);
        WriteBigEndian32(writer, (uint)trackData.Length);
        writer.Write(trackData);
        writer.Flush();
    }

    public static byte[] BuildTrackData(Score score, Random? random = null)
    {
        var messages = MidiSequence.BuildNoteMessages(score, random);

        using var memory = new MemoryStream(1024);
        using var track = new BinaryWriter(memory, Encoding.ASCII, leaveOpen: true);

        long lastTick = 0;

        void EmitDelta(long tick)
        {
            WriteVarInt(track, Math.Max(0, tick - lastTick));
            lastTick = tick;
        }

        // 速度
        EmitDelta(0);
        var microseconds = (int)Math.Round(60_000_000.0 / score.Bpm);
        microseconds = Math.Clamp(microseconds, 1, 0xFFFFFF);
        track.Write((byte)0xFF);
        track.Write((byte)0x51);
        track.Write((byte)0x03);
        track.Write((byte)(microseconds >> 16));
        track.Write((byte)(microseconds >> 8));
        track.Write((byte)microseconds);

        // 拍号
        EmitDelta(0);
        var denominatorPower = score.BeatUnit switch
        {
            1 => 0,
            2 => 1,
            4 => 2,
            8 => 3,
            16 => 4,
            _ => 2
        };
        track.Write((byte)0xFF);
        track.Write((byte)0x58);
        track.Write((byte)0x04);
        track.Write((byte)score.BeatsPerMeasure);
        track.Write((byte)denominatorPower);
        track.Write((byte)24);   // 每 MIDI 时钟的 tick 数
        track.Write((byte)8);    // 每 24 个 MIDI 时钟的三十二分音符数

        // 音色：大钢琴
        EmitDelta(0);
        track.Write((byte)(0xC0 | MidiSequence.Channel));
        track.Write((byte)0x00);

        foreach (var message in messages)
        {
            EmitDelta(message.Tick);

            if (message.Kind == MidiEventKind.NoteOn)
            {
                track.Write((byte)(0x90 | MidiSequence.Channel));
                track.Write((byte)message.Pitch);
                track.Write((byte)message.Velocity);
            }
            else
            {
                track.Write((byte)(0x80 | MidiSequence.Channel));
                track.Write((byte)message.Pitch);
                track.Write((byte)0x00);
            }
        }

        EmitDelta(score.TotalTicks);
        track.Write((byte)0xFF);
        track.Write((byte)0x2F);
        track.Write((byte)0x00);

        track.Flush();
        return memory.ToArray();
    }

    private static void WriteBigEndian16(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteBigEndian32(BinaryWriter writer, uint value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteVarInt(BinaryWriter writer, long value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));

        Span<byte> buffer = stackalloc byte[5];
        var index = 4;
        buffer[index] = (byte)(value & 0x7F);
        value >>= 7;

        while (value > 0)
        {
            index--;
            buffer[index] = (byte)((value & 0x7F) | 0x80);
            value >>= 7;
        }

        writer.Write(buffer[index..]);
    }
}
