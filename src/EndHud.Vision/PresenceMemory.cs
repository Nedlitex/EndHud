namespace EndHud.Vision;

/// <summary>
/// Whether something seen on screen (the squad panel) is still there, across readings: one reading
/// can miss it (an effect covering it for a moment), so it only counts as gone after
/// <see cref="ReadingsToLeave"/> readings in a row without it (2 s at four readings a second).
/// </summary>
public sealed class PresenceMemory
{
    public const int ReadingsToLeave = 8;

    private int _sinceSeen = ReadingsToLeave;

    /// <summary>Gone now, without waiting out <see cref="ReadingsToLeave"/>.</summary>
    public void Forget() => _sinceSeen = ReadingsToLeave;

    /// <summary>Take whether it's seen in this reading; true while it still counts as there.</summary>
    public bool Update(bool seen)
    {
        _sinceSeen = seen ? 0 : Math.Min(_sinceSeen + 1, ReadingsToLeave);
        return _sinceSeen < ReadingsToLeave;
    }
}
