using System.IO;
using System.Text;

namespace ATTM.Tests;

/// <summary>
/// 极简 SMF 读取器，只用于测试导出结果（format 0/1、单通道、无 running status 依赖）。
/// </summary>
internal static class MidiReader
{
    internal readonly record struct RawNote(long Start, long Duration, int Pitch, int Velocity);

    internal sealed record Result(int Format, int TrackCount, int TicksPerQuarter, int TempoMicroseconds,
        int BeatsPerMeasure, int BeatUnit, IReadOnlyList<RawNote> Notes);

    public static Result Read(string path)
    {
        var data = File.ReadAllBytes(path);
        var offset = 0;

        var magic = Encoding.ASCII.GetString(data, offset, 4);
        if (magic != "MThd") throw new InvalidDataException("不是 MIDI 文件");
        offset += 4;

        var headerLength = (int)ReadUInt32(data, ref offset);
        var format = ReadUInt16(data, ref offset);
        var trackCount = ReadUInt16(data, ref offset);
        var division = ReadUInt16(data, ref offset);
        offset += headerLength - 6;

        var tempo = 500_000;
        var beats = 4;
        var beatUnit = 4;
        var notes = new List<RawNote>();

        for (var t = 0; t < trackCount; t++)
        {
            var trackMagic = Encoding.ASCII.GetString(data, offset, 4);
            if (trackMagic != "MTrk") throw new InvalidDataException($"第 {t} 个轨道头不是 MTrk");
            offset += 4;

            var trackLength = (int)ReadUInt32(data, ref offset);
            var trackEnd = offset + trackLength;

            long tick = 0;
            int runningStatus = 0;
            var active = new Dictionary<int, (long Start, int Velocity)>();

            while (offset < trackEnd)
            {
                tick += ReadVarInt(data, ref offset);
                var status = (int)data[offset];

                if (status == 0xFF)
                {
                    offset++;
                    var type = data[offset++];
                    var length = (int)ReadVarInt(data, ref offset);
                    if (type == 0x51 && length == 3)
                    {
                        tempo = (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
                    }
                    else if (type == 0x58 && length >= 2)
                    {
                        beats = data[offset];
                        beatUnit = 1 << data[offset + 1];
                    }

                    offset += length;
                    continue;
                }

                if (status < 0x80)
                {
                    status = runningStatus;
                }
                else
                {
                    runningStatus = status;
                    offset++;
                }

                var command = status & 0xF0;
                switch (command)
                {
                    case 0x80:
                    case 0x90:
                    {
                        var pitch = data[offset];
                        var velocity = data[offset + 1];
                        offset += 2;
                        if (command == 0x90 && velocity > 0)
                        {
                            active[pitch] = (tick, velocity);
                        }
                        else if (active.Remove(pitch, out var start))
                        {
                            notes.Add(new RawNote(start.Start, tick - start.Start, pitch, start.Velocity));
                        }

                        break;
                    }
                    case 0xA0:
                    case 0xB0:
                    case 0xE0:
                        offset += 2;
                        break;
                    case 0xC0:
                    case 0xD0:
                        offset += 1;
                        break;
                    default:
                        throw new InvalidDataException($"无法识别的状态字节 0x{status:X2}");
                }
            }

            offset = trackEnd;
        }

        notes.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Pitch.CompareTo(b.Pitch));
        return new Result(format, trackCount, division, tempo, beats, beatUnit, notes);
    }

    private static int ReadUInt16(byte[] data, ref int offset)
    {
        var value = (data[offset] << 8) | data[offset + 1];
        offset += 2;
        return value;
    }

    private static uint ReadUInt32(byte[] data, ref int offset)
    {
        var value = ((uint)data[offset] << 24)
                    | ((uint)data[offset + 1] << 16)
                    | ((uint)data[offset + 2] << 8)
                    | data[offset + 3];
        offset += 4;
        return value;
    }

    private static long ReadVarInt(byte[] data, ref int offset)
    {
        long value = 0;
        while (true)
        {
            var b = data[offset++];
            value = (value << 7) | (long)(b & 0x7F);
            if ((b & 0x80) == 0) break;
        }

        return value;
    }
}
