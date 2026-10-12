namespace EndHud.Vision;

/// <summary>The squad-panel regions resolved for a given frame size (for debugging layouts).</summary>
public static class PartyRegions
{
    public static IEnumerable<(string Name, PixelRect Region)> All(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        for (int i = 0; i < PartyLayout.SlotCount; i++)
        {
            yield return ($"slot{i + 1}-level", ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.LevelText, i)));
            yield return ($"slot{i + 1}-hp", ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.HpBar, i)));
        }
    }
}
