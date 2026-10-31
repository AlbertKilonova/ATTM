namespace ATTM.Core.Syntax;

/// <summary>
/// 同音高重叠覆盖：后出现的音会截断前一个音（"同音高重叠则覆盖"）。
/// </summary>
public static class NoteMerger
{
    public static IReadOnlyList<NoteEvent> Apply(IEnumerable<NoteEvent> notes)
    {
        var ordered = notes
            .Where(n => n.DurationTicks > 0)
            .OrderBy(n => n.Pitch)
            .ThenBy(n => n.StartTicks)
            .ThenBy(n => n.VoiceIndex)
            .ToList();

        var merged = new List<NoteEvent>(ordered.Count);

        foreach (var note in ordered)
        {
            if (merged.Count > 0)
            {
                var previous = merged[^1];
                if (previous.Pitch == note.Pitch && note.StartTicks < previous.EndTicks)
                {
                    if (note.StartTicks <= previous.StartTicks)
                    {
                        merged.RemoveAt(merged.Count - 1);
                    }
                    else
                    {
                        merged[^1] = previous with { DurationTicks = note.StartTicks - previous.StartTicks };
                    }
                }
            }

            merged.Add(note);
        }

        return merged
            .OrderBy(n => n.StartTicks)
            .ThenBy(n => n.Pitch)
            .ToList();
    }
}
