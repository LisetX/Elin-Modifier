using System.IO.Compression;
using System.Text;

internal static class NpcCompendiumWorkbookPackageTests
{
    internal static void Run(CoreRegressionTestResult result)
    {
        var retainedPath = Environment.GetEnvironmentVariable("ELIN_MODIFIER_NPC_WORKBOOK_FIXTURE");
        var retain = !string.IsNullOrWhiteSpace(retainedPath);
        var directory = retain
            ? Path.GetDirectoryName(retainedPath!) ?? "."
            : Path.Combine(Path.GetTempPath(), "elin_modifier_npc_workbook_" + Guid.NewGuid().ToString("N"));
        var path = retain ? retainedPath! : Path.Combine(directory, "NPC.xlsx");
        try
        {
            Directory.CreateDirectory(directory);
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+RSPLOQAAAABJRU5ErkJggg==");
            using (var workbook = new NpcCompendiumWorkbookWriter(
                path,
                "NPC图鉴",
                new NpcWorkbookSearchLabels
                {
                    DataSource = "计算后数据来源：Elin Modifier",
                    ModificationRestriction = "严禁未经授权的二次修改、传播",
                    SheetName = "搜索NPC",
                    Icon = "图标",
                    Name = "名称",
                    Id = "ID",
                    Open = "查看NPC图鉴"
                }))
            {
                workbook.AddTitle("测试NPC [test_npc]", png, "测试NPC", "test_npc");
                workbook.AddSection("基础信息");
                workbook.AddFullWidthText("ID : test_npc | 名称 : 测试NPC");
                workbook.AddPreview("标本", png, "卡片", png);
                workbook.AddGrid(
                    new[]
                    {
                        new NpcWorkbookGridCell { Icon = png, Text = "力量 : 10" },
                        new NpcWorkbookGridCell { Icon = png, Text = "体质 : 12" }
                    },
                    4);
                workbook.AddDetail(png, "能力", "等级 3", "模式:单体\n使用概率:50%", false);
                workbook.AddTitle("第二个NPC [test_npc_2]", png, "第二个NPC", "test_npc_2");
                workbook.AddSection("基础信息");
                workbook.AddFullWidthText("ID : test_npc_2 | 名称 : 第二个NPC");
                workbook.Complete();
            }

            using var archive = ZipFile.OpenRead(path);
            Check(result, "npc workbook package created", File.Exists(path) && new FileInfo(path).Length > 0);
            Check(result, "npc workbook search worksheet present", archive.GetEntry("xl/worksheets/sheet1.xml") != null);
            Check(result, "npc workbook compendium worksheet present", archive.GetEntry("xl/worksheets/sheet2.xml") != null);
            Check(result, "npc workbook embedded image present", archive.GetEntry("xl/media/image1.png") != null);
            Check(result, "npc workbook search drawing relationship present", archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels") != null);
            Check(result, "npc workbook compendium drawing relationship present", archive.GetEntry("xl/drawings/_rels/drawing2.xml.rels") != null);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            var workbookXml = workbookEntry == null ? "" : ReadEntryText(workbookEntry);
            Check(result, "npc workbook search sheet first",
                workbookXml.IndexOf("搜索NPC", StringComparison.Ordinal) >= 0 &&
                workbookXml.IndexOf("搜索NPC", StringComparison.Ordinal) < workbookXml.IndexOf("NPC图鉴", StringComparison.Ordinal));
            var searchEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            var searchXml = searchEntry == null ? "" : ReadEntryText(searchEntry);
            Check(result, "npc workbook searchable index present",
                searchXml.Contains("autoFilter ref=\"A4:D", StringComparison.Ordinal) &&
                searchXml.Contains("hyperlink", StringComparison.Ordinal) &&
                searchXml.Contains("test_npc_2", StringComparison.Ordinal));
            Check(result, "npc workbook search source and restriction present",
                searchXml.Contains("计算后数据来源：Elin Modifier", StringComparison.Ordinal) &&
                searchXml.Contains("严禁未经授权的二次修改、传播", StringComparison.Ordinal) &&
                searchXml.Contains("mergeCells count=\"3\"", StringComparison.Ordinal));
            var sheetEntry = archive.GetEntry("xl/worksheets/sheet2.xml");
            var sheetXml = sheetEntry == null ? "" : ReadEntryText(sheetEntry);
            Check(result, "npc workbook formatted text preserved",
                sheetXml.Contains("测试NPC", StringComparison.Ordinal) &&
                sheetXml.Contains("模式:单体", StringComparison.Ordinal));
            Check(result, "npc workbook separator present", sheetXml.Contains(new string('-', 64), StringComparison.Ordinal));
            Check(result, "npc workbook merged border backing cells present",
                HasAllCells(sheetXml, 1, 0, 11) &&
                HasAllCells(sheetXml, 2, 0, 11) &&
                HasAllCells(sheetXml, 3, 0, 11) &&
                HasAllCells(sheetXml, 4, 0, 11) &&
                HasAllCells(sheetXml, 5, 0, 11) &&
                HasAllCells(sheetXml, 6, 0, 11) &&
                HasAllCells(sheetXml, 7, 0, 11));
            var stylesEntry = archive.GetEntry("xl/styles.xml");
            var stylesXml = stylesEntry == null ? "" : ReadEntryText(stylesEntry);
            Check(result, "npc workbook all borders present",
                stylesXml.Contains("<left style=\"thin\">", StringComparison.Ordinal) &&
                stylesXml.Contains("<right style=\"thin\">", StringComparison.Ordinal) &&
                stylesXml.Contains("<top style=\"thin\">", StringComparison.Ordinal) &&
                stylesXml.Contains("<bottom style=\"thin\">", StringComparison.Ordinal));
        }
        catch (Exception ex)
        {
            result.Failed++;
            result.Failures.Add("npc workbook package: " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try
            {
                if (!retain && Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch
            {
            }
        }
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static bool HasAllCells(string sheetXml, int row, int firstColumn, int lastColumn)
    {
        for (var column = firstColumn; column <= lastColumn; column++)
        {
            var address = ((char)('A' + column)).ToString() + row.ToString();
            if (!sheetXml.Contains("<c r=\"" + address + "\"", StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static void Check(CoreRegressionTestResult result, string name, bool passed)
    {
        if (passed)
        {
            result.Passed++;
            return;
        }
        result.Failed++;
        result.Failures.Add(name);
    }
}
