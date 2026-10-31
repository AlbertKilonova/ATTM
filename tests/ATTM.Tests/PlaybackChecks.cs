using System.IO;
using ATTM.Core.Playback;
using ATTM.Core.Syntax;

namespace ATTM.Tests;

internal static class PlaybackChecks
{
    public static void Run()
    {
        const string header = "bpm = 120\n1 = C 4/4\n\n";

        Program.Check("播放：按时间轴发送 NoteOn / NoteOff", () =>
        {
            var score = AttmParser.Parse(header + "3,02,2,0").Score!;
            var clock = new FakeClock();
            var output = new RecordingMidiOutput();
            using var player = new ScorePlayer(output, clock);

            player.SetScore(score);
            player.Play();

            if (player.State != PlayerState.Playing) return false;

            player.Advance();
            clock.Elapsed = TimeSpan.FromSeconds(0.5);
            player.Advance();
            clock.Elapsed = TimeSpan.FromSeconds(1.05);
            player.Advance();
            clock.Elapsed = TimeSpan.FromSeconds(2.2);
            player.Advance();

            var expected = new[]
            {
                "reset",
                "on 64 90",
                "off 64",
                "on 62 90",
                "off 62",
                "on 62 90",
                "off 62",
                "alloff"
            };

            if (!output.Events.SequenceEqual(expected))
            {
                Console.WriteLine("      实际：" + string.Join(" | ", output.Events));
                return false;
            }

            return player.State == PlayerState.Stopped;
        });

        Program.Check("播放：暂停发 AllNotesOff，恢复重发正在响的音", () =>
        {
            var score = AttmParser.Parse(header + "1").Score!;
            var clock = new FakeClock();
            var output = new RecordingMidiOutput();
            using var player = new ScorePlayer(output, clock);

            player.SetScore(score);
            player.Play();

            clock.Elapsed = TimeSpan.FromSeconds(0.3);
            player.Advance();
            player.Pause();

            var pausedPosition = player.Position;
            var pausedState = player.State;

            player.Play();
            clock.Elapsed = TimeSpan.FromSeconds(1.8);
            player.Advance();

            var expected = new[] { "reset", "on 60 90", "alloff", "on 60 90", "off 60", "alloff" };
            if (!output.Events.SequenceEqual(expected))
            {
                Console.WriteLine("      实际：" + string.Join(" | ", output.Events));
                return false;
            }

            return pausedState == PlayerState.Paused
                   && Math.Abs(pausedPosition.TotalSeconds - 0.3) < 0.001;
        });

        Program.Check("播放：停止后回到开头", () =>
        {
            var score = AttmParser.Parse(header + "1").Score!;
            var clock = new FakeClock();
            var output = new RecordingMidiOutput();
            using var player = new ScorePlayer(output, clock);

            player.SetScore(score);
            player.Play();
            clock.Elapsed = TimeSpan.FromSeconds(1.2);
            player.Advance();
            player.Stop();

            return player.State == PlayerState.Stopped
                   && player.Position == TimeSpan.Zero
                   && output.Events[^1] == "alloff";
        });

        Program.Check("播放：高亮区间跟随播放位置", () =>
        {
            var text = header + "1,2,3,4";
            var score = AttmParser.Parse(text).Score!;
            var clock = new FakeClock();
            var output = new RecordingMidiOutput();
            using var player = new ScorePlayer(output, clock);

            player.SetScore(score);
            player.Play();

            clock.Elapsed = TimeSpan.FromSeconds(0.1);
            player.Advance();
            var first = player.GetActiveSpans();

            clock.Elapsed = TimeSpan.FromSeconds(0.6);
            player.Advance();
            var second = player.GetActiveSpans();

            var firstText = first.Count > 0 ? text.Substring(first[0].Start, first[0].Length) : "(空)";
            var secondText = second.Count > 0 ? text.Substring(second[0].Start, second[0].Length) : "(空)";

            return first.Count == 1 && firstText == "1"
                   && second.Count == 1 && secondText == "2";
        });

        Program.Check("播放：空乐谱不会崩溃", () =>
        {
            var clock = new FakeClock();
            var output = new RecordingMidiOutput();
            using var player = new ScorePlayer(output, clock);

            player.SetScore(null);
            player.Play();
            player.Advance();
            player.Pause();
            player.Stop();

            return player.State == PlayerState.Stopped;
        });

        Program.Check("MIDI 输出：能枚举系统端口", () =>
        {
            var devices = Win32MidiOutput.GetDevices();
            if (devices.Any(d => string.IsNullOrWhiteSpace(d.Name))) return false;

            Console.WriteLine($"      检测到 {devices.Count} 个 MIDI 输出设备："
                              + string.Join("、", devices.Select(d => d.Name)));
            return true;
        });

        Program.Check("MIDI 输出：能打开设备并安全关闭", () =>
        {
            var devices = Win32MidiOutput.GetDevices();
            if (devices.Count == 0) return true;

            var ok = Win32MidiOutput.TryOpen(devices[0].Index, out var output, out var error);
            if (!ok || output is null)
            {
                Console.WriteLine("      " + error);
                return false;
            }

            output.NoteOn(0, 60, 60);
            output.NoteOff(0, 60);
            output.Dispose();
            return true;
        });

        Program.Check("播放：从指定位置起播（光标处播放）", () =>
        {
            var result = AttmParser.Parse("""
bpm = 120
1 = C 4/4

3,02,2,0

1,2,3,4
""");
            var score = result.Score!;

            if (result.MeasureStartLines.Count != 2)
            {
                Console.WriteLine("      小节起始行：" + string.Join(",", result.MeasureStartLines));
                return false;
            }

            var clock = new FakeClock();
            var output = new RecordingMidiOutput();
            using var player = new ScorePlayer(output, clock);

            player.SetScore(score);

            // 第 2 小节在 120 BPM 4/4 下正好是 2.0 秒
            var start = TimeSpan.FromSeconds(2.0);
            player.Play(start);

            if (Math.Abs((player.Position - start).TotalSeconds) > 0.01) return false;

            player.Advance();
            clock.Elapsed = TimeSpan.FromSeconds(0.5);
            player.Advance();

            var events = string.Join(" | ", output.Events);
            Console.WriteLine("      实际：" + events);

            // 第 1 小节的音不应再发送，直接是第 2 小节的第一个音（1 = C4 = 60）
            return output.Events.Count > 1
                   && output.Events[1] == "on 60 90"
                   && !output.Events.Contains("on 64 90");
        });

        Program.Check("播放：小节序号按光标行换算", () =>
        {
            const string text = """
bpm = 120
1 = C 4/4

1,2,3,4

3,4,5,6

1,0,0,0
""";
            var result = AttmParser.Parse(text);

            var first = text.IndexOf("1,2,3", StringComparison.Ordinal);
            var second = text.IndexOf("3,4,5", StringComparison.Ordinal);
            var third = text.IndexOf("1,0,0", StringComparison.Ordinal);

            Console.WriteLine($"      小节起始行：{string.Join(",", result.MeasureStartLines)}");

            return result.MeasureStartLines.Count == 3
                   && result.GetMeasureIndex(first) == 0
                   && result.GetMeasureIndex(second) == 1
                   && result.GetMeasureIndex(third) == 2
                   && result.GetMeasureIndex(0) == 0;
        });
    }

    private sealed class FakeClock : IPlaybackClock
    {
        public TimeSpan Elapsed { get; set; }

        public void Restart() => Elapsed = TimeSpan.Zero;

        public void Stop() { }

        public void Reset() => Elapsed = TimeSpan.Zero;
    }

    private sealed class RecordingMidiOutput : IMidiOutput
    {
        public List<string> Events { get; } = new();

        public string Name => "recording";

        public bool IsOpen => true;

        public void NoteOn(int channel, int pitch, int velocity) => Events.Add($"on {pitch} {velocity}");

        public void NoteOff(int channel, int pitch) => Events.Add($"off {pitch}");

        public void ControlChange(int channel, int controller, int value) { }

        public void AllNotesOff(int channel) => Events.Add("alloff");

        public void ResetControllers(int channel) => Events.Add("reset");

        public void Dispose() { }
    }
}
