namespace EndHud.Vision.Inn;

/// <summary>
/// The operators the player can put in a kitchen, as far as the setup screens have shown them:
/// every card the chef picker named while the player scrolled its list (a screen shows twelve), the
/// chefs chosen in a picker, and those placed on 营业设定. In memory only; cleared when the setup
/// screens close (a session starts, or the player leaves them), so a roster that changed in between
/// is seen afresh. Nobody is added from a guess: an unnamed card adds nothing.
/// </summary>
public sealed class ChefPickerMemory
{
    private readonly List<string> _available = [];

    /// <summary>A chef picker has been read since the last <see cref="Clear"/>.</summary>
    public bool PickerSeen { get; private set; }

    /// <summary>The operators seen, in the order first seen (operator ids).</summary>
    public IReadOnlyList<string> Available => _available;

    /// <summary>Takes in a reading of the chef picker: its named cards and chosen chefs.</summary>
    public void Take(ChefPickerReading reading)
    {
        PickerSeen = true;
        Add(reading.Named);
    }

    /// <summary>Takes in the chefs placed on 营业设定 (null: an empty or unread place).</summary>
    public void TakePlaced(IEnumerable<string?> chefs) => Add(chefs.OfType<string>());

    public void Clear()
    {
        _available.Clear();
        PickerSeen = false;
    }

    private void Add(IEnumerable<string> ids)
    {
        foreach (string id in ids)
            if (!_available.Contains(id)) _available.Add(id);
    }
}
