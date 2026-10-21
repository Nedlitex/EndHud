using System.Windows;
using System.Windows.Controls;

namespace EndHud;

/// <summary>
/// The inn's banners drawn into a stack, row by row (<see cref="InnBannerRows"/>; owner, 2026-10-21: "the recommended dish
/// flashes heavily and it jumps around ... the recommendation should stay steady"):
/// <list type="bullet">
/// <item>a banner already up for the same call stays as it is: the same element, not drawn or slid in again;</item>
/// <item>a kitchen's row opens once and stays: its banner changing slides out and in within the row, and the row stays
/// empty between calls, so the banners below never move;</item>
/// <item>another banner (Full Blaze, a last call) opens its row with it and closes it with it, below the kitchens.</item>
/// </list>
/// </summary>
/// <param name="stack">The panel the rows go in, top to bottom.</param>
/// <param name="build">A banner's element for its model (<see cref="InnCallView.Banner"/>).</param>
internal sealed class InnBannerStack(Panel stack, Func<InnBannerModel, Border> build)
{
    /// <summary>A row as drawn: its holder, and the banner in it (none: a kitchen's row with no call now).</summary>
    private sealed class Row(string name, Grid holder)
    {
        public string Name { get; } = name;
        public Grid Holder { get; } = holder;
        public string? Key { get; set; }
        public Border? Element { get; set; }
    }

    private readonly List<Row> _rows = [];

    /// <summary>Rows drawn (sliding out ones not counted).</summary>
    public int Count => _rows.Count;

    /// <summary>The banner shown in a row now (the tests: the same element while its call stands); null for none.</summary>
    public Border? BannerIn(string row) => _rows.FirstOrDefault(r => r.Name == row)?.Element;

    /// <summary>A row's holder (the tests: where it sits in the stack).</summary>
    public Grid? HolderOf(string row) => _rows.FirstOrDefault(r => r.Name == row)?.Holder;

    /// <summary>Draws <paramref name="rows"/>, top to bottom (none: everything slides out).</summary>
    public void Update(IReadOnlyList<InnBannerRow> rows)
    {
        // Rows no longer there slide out and close.
        foreach (var gone in _rows.Where(b => rows.All(r => r.Row != b.Name)).ToList())
        {
            _rows.Remove(gone);
            if (gone.Element is { } element) ComboBanner.SlideOut(gone.Holder, element, () => stack.Children.Remove(gone.Holder));
            else stack.Children.Remove(gone.Holder);
        }
        for (int order = 0; order < rows.Count; order++)
        {
            var row = rows[order];
            var drawn = _rows.FirstOrDefault(b => b.Name == row.Row);
            if (drawn is null)
            {
                // A new row in its place (rows still sliding out count too): a kitchen's opens and stays; another opens
                // with its banner and closes with it.
                if (!row.IsKitchen && row.Banner is null) continue;
                var element = row.Banner is { } model ? build(model) : null;
                var holder = row.IsKitchen ? ComboBanner.OpenHolder(InnCallView.BannerHeight, order) : ComboBanner.Holder(element!, order);
                int index = stack.Children.OfType<FrameworkElement>().Count(c => c.Tag is int other && other < order);
                stack.Children.Insert(index, holder);
                _rows.Add(new Row(row.Row, holder) { Key = row.Banner?.Key, Element = element });
                if (element is null) continue;
                if (row.IsKitchen)
                {
                    holder.Children.Add(element);
                    ComboBanner.SlideInPlace(element);
                }
                else ComboBanner.SlideIn(holder, element);
                continue;
            }
            drawn.Holder.Tag = order;
            if (drawn.Key == row.Banner?.Key) continue;
            // The kitchen's banner changed: the old one slides out in place, the new one in; the row stays.
            if (drawn.Element is { } old)
            {
                var holder = drawn.Holder;
                ComboBanner.SlideOutInPlace(old, () => holder.Children.Remove(old));
            }
            (drawn.Key, drawn.Element) = (null, null);
            if (row.Banner is not { } shown) continue;
            var banner = build(shown);
            drawn.Holder.Children.Add(banner);
            (drawn.Key, drawn.Element) = (shown.Key, banner);
            ComboBanner.SlideInPlace(banner);
        }
    }
}
