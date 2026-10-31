using ATTM.Core.Midi;
using ATTM.Core.Syntax;

namespace ATTM.Core.Playback;

public enum PlayerState
{
    Stopped,
    Playing,
    Paused
}

/// <summary>
/// 播放引擎：把乐谱转成 MIDI 短消息，按时间轴发送到输出端口。
/// 暂停时发送 All Notes Off，恢复时重发仍在时值内的音。
/// </summary>
public sealed class ScorePlayer : IDisposable
{
    private static readonly TimeSpan Lookahead = TimeSpan.FromMilliseconds(12);

    private readonly IPlaybackClock _clock;
    private readonly List<ScheduledEvent> _events = new();
    private readonly List<NoteEvent> _sounding = new();
    private readonly Random _random = new();

    private IMidiOutput _output;
    private Score? _score;
    private TimeSpan _base;
    private int _next;
    private bool _disposed;

    public ScorePlayer(IMidiOutput output, IPlaybackClock? clock = null)
    {
        _output = output;
        _clock = clock ?? new StopwatchPlaybackClock();
    }

    public event EventHandler? StateChanged;
    public event EventHandler? PositionChanged;
    public event EventHandler? Finished;

    public PlayerState State { get; private set; } = PlayerState.Stopped;

    public IMidiOutput Output => _output;

    public Score? Score => _score;

    public TimeSpan Duration => _score?.Duration ?? TimeSpan.Zero;

    public bool IsSilent => !_output.IsOpen;

    public TimeSpan Position => State switch
    {
        PlayerState.Playing => Clamp(_base + _clock.Elapsed),
        PlayerState.Paused => Clamp(_base),
        _ => TimeSpan.Zero
    };

    public void SetOutput(IMidiOutput output)
    {
        if (ReferenceEquals(output, _output)) return;

        Stop();
        var old = _output;
        _output = output;
        try { old.Dispose(); } catch { /* 忽略 */ }
    }

    public void SetScore(Score? score)
    {
        Stop();
        _score = score;
        _events.Clear();
        _next = 0;
    }

    /// <summary>
    /// 开始播放。<paramref name="startAt"/> 为空时从头或从暂停点继续；
    /// 指定位置时从该位置起播（已在时值内的音会补发，听感不会缺拍）。
    /// </summary>
    public void Play(TimeSpan? startAt = null)
    {
        if (_score is null || _score.Notes.Count == 0) return;

        if (State == PlayerState.Paused && startAt is null)
        {
            var position = Position;

            foreach (var note in _sounding)
                _output.NoteOn(MidiSequence.Channel, note.Pitch, Math.Clamp(note.Velocity, 1, 127));

            _sounding.Clear();
            _next = FindNextEventIndex(position);
            _clock.Restart();
            State = PlayerState.Playing;

            StateChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        BuildEvents();

        var startPosition = Clamp(startAt ?? TimeSpan.Zero);
        _base = startPosition;
        _next = FindNextEventIndex(startPosition);
        _sounding.Clear();

        _output.ResetControllers(MidiSequence.Channel);

        if (startPosition > TimeSpan.Zero)
        {
            CollectSounding(startPosition);
            foreach (var note in _sounding)
                _output.NoteOn(MidiSequence.Channel, note.Pitch, Math.Clamp(note.Velocity, 1, 127));

            _sounding.Clear();
        }

        _clock.Restart();
        State = PlayerState.Playing;

        StateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        if (State != PlayerState.Playing) return;

        var position = Position;
        _base = position;
        _clock.Stop();

        CollectSounding(position);
        _output.AllNotesOff(MidiSequence.Channel);

        State = PlayerState.Paused;
        StateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (State == PlayerState.Stopped)
        {
            _base = TimeSpan.Zero;
            return;
        }

        _output.AllNotesOff(MidiSequence.Channel);

        _base = TimeSpan.Zero;
        _clock.Reset();
        _next = 0;
        _sounding.Clear();
        State = PlayerState.Stopped;

        StateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>由 UI 定时器周期性调用：发送到期的 MIDI 消息。</summary>
    public void Advance()
    {
        if (State != PlayerState.Playing) return;

        var position = Position;
        var horizon = position + Lookahead;

        while (_next < _events.Count && _events[_next].Time <= horizon)
        {
            var scheduled = _events[_next++];
            if (scheduled.NoteOn)
                _output.NoteOn(MidiSequence.Channel, scheduled.Pitch, scheduled.Velocity);
            else
                _output.NoteOff(MidiSequence.Channel, scheduled.Pitch);
        }

        if (position >= Duration)
        {
            Complete();
            return;
        }

        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>当前正在发声的音符在源码里的位置（用于编辑器高亮）。</summary>
    public IReadOnlyList<SourceSpan> GetActiveSpans()
    {
        if (_score is null) return Array.Empty<SourceSpan>();

        var seconds = Position.TotalSeconds;
        if (seconds < 0) return Array.Empty<SourceSpan>();

        var secondsPerTick = _score.SecondsPerTick;
        var spans = new List<SourceSpan>(4);

        foreach (var note in _score.Notes)
        {
            var start = note.StartTicks * secondsPerTick;
            if (start > seconds) break;

            if (note.EndTicks * secondsPerTick > seconds)
                spans.Add(note.Span);
        }

        return spans;
    }

    private void Complete()
    {
        _output.AllNotesOff(MidiSequence.Channel);

        _base = TimeSpan.Zero;
        _clock.Reset();
        _next = 0;
        _sounding.Clear();
        State = PlayerState.Stopped;

        StateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
        Finished?.Invoke(this, EventArgs.Empty);
    }

    private void BuildEvents()
    {
        _events.Clear();
        if (_score is null) return;

        var secondsPerTick = _score.SecondsPerTick;
        foreach (var message in MidiSequence.BuildNoteMessages(_score, _random))
        {
            _events.Add(new ScheduledEvent(
                TimeSpan.FromSeconds(message.Tick * secondsPerTick),
                message.Kind == MidiEventKind.NoteOn,
                message.Pitch,
                message.Velocity));
        }
    }

    private void CollectSounding(TimeSpan position)
    {
        _sounding.Clear();
        if (_score is null) return;

        var seconds = position.TotalSeconds;
        var secondsPerTick = _score.SecondsPerTick;

        foreach (var note in _score.Notes)
        {
            var start = note.StartTicks * secondsPerTick;
            if (start >= seconds) break;

            if (note.EndTicks * secondsPerTick > seconds)
                _sounding.Add(note);
        }
    }

    private int FindNextEventIndex(TimeSpan position)
    {
        var low = 0;
        var high = _events.Count;

        while (low < high)
        {
            var mid = (low + high) / 2;
            // 用严格小于：起点正好落在某个事件的时刻时，该事件仍要发送
            if (_events[mid].Time < position) low = mid + 1;
            else high = mid;
        }

        return low;
    }

    private TimeSpan Clamp(TimeSpan position)
    {
        var duration = Duration;
        if (position < TimeSpan.Zero) return TimeSpan.Zero;
        return position > duration ? duration : position;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        try { _output.Dispose(); } catch { /* 忽略 */ }
    }

    private readonly record struct ScheduledEvent(TimeSpan Time, bool NoteOn, int Pitch, int Velocity);
}
