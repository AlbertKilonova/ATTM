using System.Diagnostics;

namespace ATTM.Core.Playback;

/// <summary>播放时钟抽象（测试里可以替换成手动推进的假时钟）。</summary>
public interface IPlaybackClock
{
    TimeSpan Elapsed { get; }
    void Restart();
    void Stop();
    void Reset();
}

public sealed class StopwatchPlaybackClock : IPlaybackClock
{
    private readonly Stopwatch _stopwatch = new();

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public void Restart() => _stopwatch.Restart();

    public void Stop() => _stopwatch.Stop();

    public void Reset() => _stopwatch.Reset();
}
