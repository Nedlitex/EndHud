using System.Globalization;

namespace EndHud.Vision.Input;

/// <summary>
/// Presses for a recording (endhud-cli trace-video --presses): lines "t,slot", seconds from the
/// start of the clip and the squad slot (1-4) whose battle skill was cast; '#' starts a comment,
/// blank lines are skipped.
/// </summary>
public sealed class FileSkillPresses(IEnumerable<SkillPress> presses) : ISkillPressSource
{
    private readonly Queue<SkillPress> _queue = new(presses.OrderBy(p => p.At));

    /// <summary>Reads the file's text; a bad line throws a <see cref="FormatException"/> naming its line number.</summary>
    public static FileSkillPresses Parse(string text)
    {
        var presses = new List<SkillPress>();
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int comment = line.IndexOf('#');
            if (comment >= 0) line = line[..comment];
            line = line.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(',');
            if (parts.Length != 2
                || !double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                || !double.IsFinite(seconds) || seconds < 0
                || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)
                || slot is < 1 or > 4)
                throw new FormatException($"presses, line {i + 1}: expected \"seconds,slot\" (seconds from the start, slot 1-4), got \"{line}\"");
            presses.Add(new SkillPress(slot, TimeSpan.FromSeconds(seconds)));
        }
        return new FileSkillPresses(presses);
    }

    public IReadOnlyList<SkillPress> TakeUntil(TimeSpan readAt)
    {
        var taken = new List<SkillPress>();
        while (_queue.Count > 0 && _queue.Peek().At <= readAt) taken.Add(_queue.Dequeue());
        return taken;
    }
}
