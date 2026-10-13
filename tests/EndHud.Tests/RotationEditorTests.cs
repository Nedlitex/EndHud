using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;
using Rotation = EndHud.Vision.Rotations.Rotation;

namespace EndHud.Tests;

/// <summary>The editor and recommended rotations: never changed in place, and deleting one hides it.</summary>
public sealed class RotationEditorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static Rotation Published() => new(Rotation.CurrentFormat, "laevatain-standard", "标准轴", ["Laevatain", "Camille", "Arcane", "Ardelia"],
        [new RotationPhase("p", "充能轴", [new RotationStep("Ardelia", StepAction.BattleSkill)])], [new PhaseUse("p")]);

    [Fact]
    public void Saving_a_recommended_rotation_saves_the_players_own_copy_and_leaves_it_as_it_was()
    {
        var library = new RotationLibrary(_folder);
        library.Published.Save(Published());
        Rotation? saved = null;

        OnEditor(library, saved_ => saved = saved_, editor =>
        {
            Assert.Equal(Visibility.Visible, ((TextBlock)editor.FindName("PublishedNote")).Visibility);
            ((TextBox)editor.FindName("NameBox")).Text = "标准轴 改";
            Click(editor, "SaveButton");
        });

        var own = Assert.Single(library.Own.Load().Rotations);
        Assert.NotEqual("laevatain-standard", own.Id);
        Assert.Equal(own.Id, saved!.Id); // and it becomes the one used for the squad
        Assert.Equal("标准轴", Assert.Single(library.Published.Load().Rotations).Name);
        Assert.Equal(2, library.Load().Rotations.Count);
    }

    [Fact]
    public void Deleting_a_recommended_rotation_hides_it()
    {
        var library = new RotationLibrary(_folder);
        library.Published.Save(Published());

        OnEditor(library, _ => { }, editor => editor.DeleteSelected(confirm: false));

        Assert.Empty(library.Load().Rotations);
        Assert.Contains("laevatain-standard", library.Hidden());
    }

    private static void Click(Window window, string name) =>
        ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private void OnEditor(RotationLibrary library, Action<Rotation?> changed, Action<RotationEditor> act)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var operators = OperatorLibrary.LoadAsync(Path.Combine(_folder, "user"), Path.Combine(_folder, "reference")).GetAwaiter().GetResult();
                var editor = new RotationEditor(library, operators, [null, null, null, null], null, () => Task.FromResult<RotationSyncResult?>(null), changed);
                act(editor);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
