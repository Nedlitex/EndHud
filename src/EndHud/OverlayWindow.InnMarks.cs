using System.Windows;

namespace EndHud;

/// <summary>
/// The marks on the open dispatch panel (<see cref="InnPanelMarks"/>): the suggested dish's card outlined, the other
/// kitchens' tabs labelled. They follow the game's layout (no dragging, nothing in config); drawn again when the marks,
/// the window's size or the inn's size change; with the banners' setting (InnCallsEnabled).
/// </summary>
public partial class OverlayWindow
{
    private readonly List<FrameworkElement> _innMarks = [];
    private string? _innMarksKey;

    private void RenderInnMarks(IReadOnlyList<InnPanelMark> marks)
    {
        double width = HintCanvas.ActualWidth, height = HintCanvas.ActualHeight, scale = InnScale();
        string? key = marks.Count == 0 ? null : $"{width:F0}x{height:F0}@{scale:F3}\n" + string.Join("\n", marks.Select(m => m.Key));
        if (key == _innMarksKey) return;
        _innMarksKey = key;
        foreach (var element in _innMarks) HintCanvas.Children.Remove(element);
        _innMarks.Clear();
        foreach (var mark in marks)
        {
            var element = InnPanelMarks.Element(mark, width, height, scale);
            element.Opacity = _config.InnStripOpacity;
            HintCanvas.Children.Add(element);
            _innMarks.Add(element);
        }
    }
}
