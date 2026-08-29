using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

internal sealed partial class NpcInfoModule
{
    private sealed class NpcExportState
    {
        internal string WorkbookPath = "";
        internal readonly List<NpcRecord> Npcs = new List<NpcRecord>();
        internal readonly Dictionary<string, double> CurrentZoneProbabilities =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, byte[]> ImageCache =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);
        internal NpcCompendiumWorkbookWriter? Workbook;
        internal string Error = "";
    }

    internal bool IsExporting { get; private set; }

    internal IEnumerator ExportData(string pluginDirectory, Action? completed)
    {
        if (IsExporting)
            yield break;

        IsExporting = true;
        Log = T("正在导出NPC数据…", "Exporting NPC data...");
        NpcExportState? state = null;
        try
        {
            state = CreateNpcExportState(pluginDirectory);
            if (state.Error.Length > 0)
            {
                Log = T("NPC数据导出失败：", "NPC data export failed: ") + state.Error;
                yield break;
            }

            for (var i = 0; i < state.Npcs.Count; i++)
            {
                var npc = state.Npcs[i];
                state.CurrentZoneProbabilities.TryGetValue(npc.Id, out var currentZoneProbability);
                NpcAnalysis? analysis = null;
                try
                {
                    analysis = AnalyzeNpcForExport(npc.Id, currentZoneProbability);
                    if (analysis != null && state.Workbook != null)
                        _host.AppendNpcCompendiumWorkbookEntry(state.Workbook, analysis, state.ImageCache);
                }
                catch (Exception ex)
                {
                    state.Error = ex.GetType().Name + ": " + ex.Message;
                }
                if (state.Error.Length > 0)
                    break;
                Log = T("正在导出NPC数据：", "Exporting NPC data: ") +
                      (i + 1).ToString(CultureInfo.InvariantCulture) + " / " +
                      state.Npcs.Count.ToString(CultureInfo.InvariantCulture);
                yield return null;
            }

            if (state.Error.Length == 0)
            {
                try
                {
                    state.Workbook?.Complete();
                    TryRemoveLegacyNpcExport(pluginDirectory);
                }
                catch (Exception ex)
                {
                    state.Error = ex.GetType().Name + ": " + ex.Message;
                }
            }
            if (state.Error.Length > 0)
            {
                Log = T("NPC数据导出失败：", "NPC data export failed: ") + state.Error;
                yield break;
            }

            Log = T("NPC数据导出完成，共 ", "NPC data export completed: ") +
                  state.Npcs.Count.ToString(CultureInfo.InvariantCulture) +
                  T(" 个NPC", " NPCs") + " · " + state.WorkbookPath;
        }
        finally
        {
            state?.Workbook?.Dispose();
            IsExporting = false;
            completed?.Invoke();
        }
    }

    private NpcExportState CreateNpcExportState(string pluginDirectory)
    {
        var state = new NpcExportState();
        try
        {
            EnsureData();
            var exportDirectory = Path.Combine(pluginDirectory, "export");
            state.WorkbookPath = Path.Combine(exportDirectory, "NPC.xlsx");
            Directory.CreateDirectory(exportDirectory);
            state.Npcs.AddRange(_npcs.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase));
            var zone = AnalyzeCurrentZone();
            if (zone != null)
            {
                for (var i = 0; i < zone.Npcs.Count; i++)
                    state.CurrentZoneProbabilities[zone.Npcs[i].Npc.Id] = zone.Npcs[i].Probability;
            }
            state.Workbook = new NpcCompendiumWorkbookWriter(
                state.WorkbookPath,
                T("NPC图鉴", "NPC Compendium"),
                new NpcWorkbookSearchLabels
                {
                    DataSource = T("计算后数据来源：Elin Modifier", "Calculated data source: Elin Modifier"),
                    ModificationRestriction = T(
                        "严禁未经授权的二次修改、传播",
                        "Unauthorized secondary modification or distribution is strictly prohibited."),
                    SheetName = T("搜索NPC", "Search NPCs"),
                    Icon = T("图标", "Icon"),
                    Name = T("名称", "Name"),
                    Id = "ID",
                    Open = T("查看NPC图鉴", "View NPC Compendium")
                });
        }
        catch (Exception ex)
        {
            state.Error = ex.GetType().Name + ": " + ex.Message;
        }
        return state;
    }

    private static void TryRemoveLegacyNpcExport(string pluginDirectory)
    {
        try
        {
            var exportDirectory = Path.Combine(pluginDirectory, "export");
            var csvPath = Path.Combine(exportDirectory, "NPC.csv");
            var iconDirectory = Path.Combine(exportDirectory, "NPC_Icons");
            if (File.Exists(csvPath))
                File.Delete(csvPath);
            if (Directory.Exists(iconDirectory))
                Directory.Delete(iconDirectory, true);
        }
        catch
        {
        }
    }

}
