namespace ATTM.Core.Syntax;

/// <summary>一个发声音符。时间为绝对 tick（从文件开头算）。</summary>
public sealed record NoteEvent(
    int Pitch,
    int Velocity,
    long StartTicks,
    long DurationTicks,
    int MeasureIndex,
    int VoiceIndex,
    SourceSpan Span)
{
    public long EndTicks => StartTicks + DurationTicks;
}

/// <summary>解析成功后的乐谱（可能带有语法错误，出错时 UI 会禁止播放/保存）。</summary>
public sealed class Score
{
    public double Bpm { get; init; } = 120;
    public char KeyRoot { get; init; } = 'C';

    /// <summary>调式是否带升号（1 = #C）。</summary>
    public bool KeySharp { get; init; }
    public int BeatsPerMeasure { get; init; } = 4;
    public int BeatUnit { get; init; } = 4;
    public int VelocityBase { get; init; } = 90;
    public int VelocityJitter { get; init; }
    public int TicksPerQuarter { get; init; } = 480;
    public int MeasureCount { get; init; }
    public IReadOnlyList<NoteEvent> Notes { get; init; } = Array.Empty<NoteEvent>();

    public int MeasureTicks => TicksPerQuarter * 4 * BeatsPerMeasure / BeatUnit;

    public long TotalTicks => (long)MeasureCount * MeasureTicks;

    public double SecondsPerTick => 60.0 / (Bpm * TicksPerQuarter);

    public TimeSpan Duration => TimeSpan.FromSeconds(TotalTicks * SecondsPerTick);

    public string KeyName => $"{PitchMapper.DescribeKey(KeyRoot, KeySharp)} 大调";

    public string TimeSignatureName => $"{BeatsPerMeasure}/{BeatUnit}";

    public long TicksToMilliseconds(long ticks) => (long)Math.Round(ticks * SecondsPerTick * 1000.0);
}
