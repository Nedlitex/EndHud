namespace EndHud.Vision;

/// <summary>
/// Which kind of battle the player is in, told by the top left of the screen (owner, 2026-10-25: "look at the left top
/// for difference, if there is 撤离 that is battle"). Kept by <see cref="PartyTracker"/> on
/// <see cref="PartyState.BattleKind"/>; the overlays each have a setting for whether they show in the open world.
/// </summary>
public enum BattleKind
{
    /// <summary>Neither sign seen since EndHud started reading this window (it started mid-fight in an open-world event,
    /// or something has covered the top left all along). Treated as before the kinds were told apart: every overlay shows.</summary>
    Unknown,

    /// <summary>The evacuate bar (撤离, <see cref="EvacuateBar"/>) was the last sign seen: a dungeon, a boss battle.</summary>
    Instance,

    /// <summary>The mode switch (探索 / 工业, <see cref="ModePill"/>) was the last sign seen: the open world, where an
    /// event's fight may draw its own buttons at the top left instead of the switch.</summary>
    OpenWorld,
}
