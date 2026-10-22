using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>What a kitchen's circle asks for.</summary>
internal enum InnCircleKind
{
    /// <summary>Queue the dish for a table's order.</summary>
    Order,

    /// <summary>Queue the dish to cook ahead: no order claims it yet.</summary>
    CookAhead,

    /// <summary>Take a dish cooked ahead out of the queue, then queue this one for a table's order.</summary>
    Swap,
}

/// <summary>One kitchen's circle.</summary>
internal sealed record InnCircleModel(int Kitchen, string Key, InnCircleKind Kind, string Label, string KindWord, string Dish, string Reason, string? PictureId);

/// <summary>The kitchens' calls as big circles (work in progress).</summary>
internal static class InnKitchenCircleView
{
    public const int Slots = KitchenMath.MaxKitchens;

    public const double CircleSize = 136, PictureSize = 108, SlotWidth = 240, LabelFont = 22, DishFont = 22, ReasonFont = 20, RowHeight = 230;

    public static IReadOnlyList<InnCircleModel?> Of(IReadOnlyList<InnCall> calls, Func<string, string> text) => throw new NotImplementedException();

    public static InnCircleModel Of(InnCall call, Func<string, string> text) => throw new NotImplementedException();

    public static Color ColorOf(InnCircleKind kind) => throw new NotImplementedException();

    public static FrameworkElement Circle(InnCircleModel model, Func<string, BitmapSource?> picture) => throw new NotImplementedException();

    public static IReadOnlyList<InnCircleModel?> Sample(InnData? data, Func<string, string> text) => throw new NotImplementedException();
}

/// <summary>The circles drawn into a row of slots, one per kitchen (work in progress).</summary>
internal sealed class InnCircleRow(System.Windows.Controls.Grid row, Func<InnCircleModel, FrameworkElement> build)
{
    public FrameworkElement? CircleIn(int kitchen) => throw new NotImplementedException();

    public void Update(IReadOnlyList<InnCircleModel?> slots) => throw new NotImplementedException();

    public void Settle() => throw new NotImplementedException();

    public static System.Windows.Controls.Grid NewGrid() => throw new NotImplementedException();
}

/// <summary>Where the circles go (work in progress).</summary>
internal static class InnKitchenCircleLayout
{
    public const double DefaultCenterX = 0.5, DefaultBottomY = 0.845;

    public static Rect Place(double width, double height, double scale, double centerX, double bottomY) => throw new NotImplementedException();
}
