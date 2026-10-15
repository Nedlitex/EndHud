using System.IO;
using System.Text;
using System.Windows;

namespace EndHud;

/// <summary>"Export diagnostics", from the tray or the menu: see <see cref="DiagnosticExport"/>.</summary>
public partial class OverlayWindow
{
    /// <summary>Write the diagnostic zip to the desktop; its path, or null (and why, logged) if it couldn't be written.</summary>
    internal async Task<string?> ExportDiagnosticsAsync()
    {
        string path = DiagnosticExport.NewPath();
        try
        {
            DiagnosticLog.Info("diagnostics exported by the player");
            await DiagnosticExport.WriteAsync(path, DiagnosticSummary(), _party.LatestFrame, ConfigStore.FilePath);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Error("diagnostics export failed", ex);
            return null;
        }
    }

    private string DiagnosticSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"exported {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.Append(DiagnosticExport.DescribeComputer());
        sb.AppendLine();
        sb.AppendLine($"game window: {(_game == IntPtr.Zero ? "not found" : Native.Describe(_game))}");
        sb.AppendLine($"HUD {(_hudOn ? "on" : "off")}, auto show {_config.AutoShowOnGameFocus}, squad reading {_config.PartyOcrEnabled}, menu {(_menuOpen ? "open" : "closed")}");
        sb.AppendLine($"debug panel says: {PartyText.Text.ReplaceLineEndings(" / ")}");
        sb.AppendLine(_party.Describe());
        if (_lastState is { } state)
        {
            sb.AppendLine($"last reading: panel {(state.PanelVisible ? "seen" : "not seen")} ({state.PanelMarks}/8 marks), in combat {state.InCombat}, skill points {state.SkillPoints?.ToString("F2") ?? "-"}, controlled slot {state.ActiveSlot?.ToString() ?? "-"}");
            foreach (var slot in state.Slots)
                sb.AppendLine($"  slot {slot.Number}: {slot.Operator?.Operator.Id ?? "not named"}, level {slot.Level?.ToString() ?? "-"}, hp {slot.Hp?.ToString("P0") ?? "-"}{(slot.Dead ? ", fallen" : "")}");
        }
        sb.AppendLine();
        sb.Append(DiagnosticExport.DescribeLibrary(PartyMonitor.LibraryFolder));
        return sb.ToString();
    }

    private async void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        ExportDiagnosticsButton.IsEnabled = false;
        try
        {
            string? path = await ExportDiagnosticsAsync();
            DiagnosticsStatus.Text = path is null ? Loc.Get("Diagnostics.ExportFailed") : Loc.Format("Diagnostics.Exported", Path.GetFileName(path));
            if (path is not null) DiagnosticExport.Reveal(path);
        }
        finally
        {
            ExportDiagnosticsButton.IsEnabled = true;
        }
    }
}
