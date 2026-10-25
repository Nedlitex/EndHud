using System.IO;
using System.Text;
using System.Windows;
using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>"Export diagnostics", from the tray or the menu: see <see cref="DiagnosticExport"/>.</summary>
public partial class OverlayWindow
{
    /// <summary>Write the diagnostic zip to the desktop; its path, or null (and why, logged) if it couldn't be written.</summary>
    internal async Task<string?> ExportDiagnosticsAsync(DiagnosticSize size)
    {
        string path = DiagnosticExport.NewPath(size);
        try
        {
            DiagnosticLog.Info($"diagnostics exported by the player ({size})");
            await DiagnosticExport.WriteAsync(size, path, DiagnosticSummary(), _party.LatestFrame, ConfigStore.FilePath, _party.RecentFrames);
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
        sb.AppendLine(Vision.Capture.GraphicsInfo.Describe(_game));
        if (_lastState is { } state)
        {
            sb.AppendLine($"last reading: panel {(state.PanelVisible ? "seen" : "not seen")} ({PartyMonitor.DescribeLayout(state)}), in combat {state.InCombat}, mode switch {state.ModePill}, battle kind {state.BattleKind}, skill points {Vision.SkillPointBar.Describe(state)}, controlled slot {state.ActiveSlot?.ToString() ?? "-"}");
            foreach (var slot in state.Slots)
                sb.AppendLine($"  slot {slot.Number}: {slot.Operator?.Operator.Id ?? "not named"}, level {slot.Level?.ToString() ?? "-"}, hp {slot.Hp?.ToString("P0") ?? "-"}{(slot.Dead ? ", fallen" : "")}");
        }
        foreach (string line in CastLines(_rotationFollower.CastTally))
            sb.AppendLine(line);
        foreach (string line in PressLines(_config.ReadSkillPresses ? _skillInput.Describe() : "off (not consented)", _rotationFollower.PressTally))
            sb.AppendLine(line);
        sb.AppendLine();
        sb.Append(DiagnosticExport.DescribeLibrary(PartyMonitor.LibraryFolder));
        return sb.ToString();
    }

    /// <summary>
    /// The summary's lines about the battle skills the step follower was handed, per slot and by what named the slot
    /// (<see cref="ScreenCastJudge"/>), since start. Counts only: never a time (a list of cast or press times would be
    /// a keystroke timeline), so nothing about them goes into the recent readings either. A tally of zeros with skill
    /// points spent tells a fast path that never ran (the controller layout, the screen fallback).
    /// </summary>
    internal static IEnumerable<string> CastLines(int[,] tally)
    {
        int Count(int row, CastSource source) => tally[row, (int)source];
        yield return "battle skills seen cast (the step follower's, since start):";
        for (int slot = 0; slot < 4; slot++)
            yield return $"  slot {slot + 1}: by flash {Count(slot, CastSource.Flash)}, by countdown {Count(slot, CastSource.Cooldown)}, " +
                $"by key press {Count(slot, CastSource.Press)}, empowered without a point {Count(slot, CastSource.Empowered)}";
        yield return $"  skill points spent with nobody named: {Count(4, CastSource.Unattributed)}";
    }

    /// <summary>
    /// The summary's lines about battle-skill key reading: whether it reads, then for each slot how
    /// its presses were judged since start. Counts only, like <see cref="CastLines"/>.
    /// </summary>
    internal static IEnumerable<string> PressLines(string status, int[,] tally)
    {
        yield return $"battle-skill key reading: {status}";
        for (int slot = 0; slot < 4; slot++)
        {
            int Count(PressVerdict verdict) => tally[slot, (int)verdict];
            yield return $"  slot {slot + 1} battle skill: counted {Count(PressVerdict.Counted)}, cooling {Count(PressVerdict.Cooling)}, " +
                $"no skill points {Count(PressVerdict.NoSkillPoints)}, repeat {Count(PressVerdict.Repeat)}, " +
                $"not in battle {Count(PressVerdict.NotInBattle)}, empty slot {Count(PressVerdict.EmptySlot)}";
        }
    }

    private void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e) => ExportFromMenu(DiagnosticSize.Mini);

    private void ExportFullDiagnosticsButton_Click(object sender, RoutedEventArgs e) => ExportFromMenu(DiagnosticSize.Full);

    private async void ExportFromMenu(DiagnosticSize size)
    {
        ExportDiagnosticsButton.IsEnabled = ExportFullDiagnosticsButton.IsEnabled = false;
        try
        {
            string? path = await ExportDiagnosticsAsync(size);
            DiagnosticsStatus.Text = path is null ? Loc.Get("Diagnostics.ExportFailed") : Loc.Format("Diagnostics.Exported", Path.GetFileName(path));
            if (path is not null) DiagnosticExport.Reveal(path);
        }
        finally
        {
            ExportDiagnosticsButton.IsEnabled = ExportFullDiagnosticsButton.IsEnabled = true;
        }
    }
}
