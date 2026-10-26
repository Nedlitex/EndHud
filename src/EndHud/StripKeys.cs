using EndHud.Vision;

namespace EndHud;

/// <summary>
/// The key that casts a step's battle skill, as the strip shows it in a small chip above the step's circle (owner,
/// 2026-10-25: "in the rotation, show the key for battle skill (1-4) show above the circle").
/// </summary>
/// <param name="Slot">The operator's squad slot now, 1-4: with keyboard and mouse the key is that digit.</param>
/// <param name="Controller">The game draws the controller layout: the chip shows the face button in the slot's direction
/// of the skill diamond instead (left slot 1, top 2, right 3, bottom 4), held with L1/LB.</param>
internal sealed record StripKey(int Slot, bool Controller)
{
    /// <summary>
    /// The face button's glyph: the PlayStation symbols. Which controller family the player holds isn't read (the HUD
    /// layout reading tells controller from keyboard, not Sony from Xbox), and the symbols are drawn as shapes, so they
    /// say the direction as plainly on either pad.
    /// </summary>
    public FaceButton Button => Slot switch
    {
        1 => FaceButton.Square,
        2 => FaceButton.Triangle,
        3 => FaceButton.Circle,
        _ => FaceButton.Cross,
    };
}

/// <summary>A controller's face buttons, by the PlayStation symbol drawn for each (left, top, right, bottom of the diamond).</summary>
internal enum FaceButton
{
    /// <summary>Left: □ (Xbox X).</summary>
    Square,

    /// <summary>Top: △ (Xbox Y).</summary>
    Triangle,

    /// <summary>Right: ○ (Xbox B).</summary>
    Circle,

    /// <summary>Bottom: × (Xbox A).</summary>
    Cross,
}

/// <summary>
/// Where each operator's battle-skill key is now: the recognised squad slot by slot, in the order the game has them on
/// this reading, and the HUD layout. A rotation is for its four operators in any order (owner, 2026-10-25: "if the squad
/// order changes, but the overall operators are the same, the battle skill key will change based on squad"), so the
/// key is never taken from the rotation's own order nor kept from an earlier reading.
/// </summary>
/// <param name="Squad">Operator ids by slot (slot 1 first); null where nobody is recognised.</param>
/// <param name="Controller">The controller layout: face buttons instead of digits.</param>
internal sealed record StripKeys(IReadOnlyList<string?> Squad, bool Controller)
{
    /// <summary>The key for this operator's battle skill; null when they aren't in the recognised squad.</summary>
    public StripKey? For(string operatorId) => null;

    /// <summary>The keys as this reading has them; null without a reading.</summary>
    public static StripKeys? Of(PartyState? state) => null;

    /// <summary>
    /// The editor's preview: the keys only while the squad EndHud recognises now is the rotation's (any order), in the
    /// order the game has them; none otherwise (no squad recognised, or another squad's rotation being edited).
    /// </summary>
    /// <param name="currentSquad">The recognised squad by slot, as the overlay last read it.</param>
    public static StripKeys? ForPreview(Vision.Rotations.Rotation rotation, IReadOnlyList<string?> currentSquad, HudLayout layout) => null;
}
