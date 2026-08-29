using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

internal sealed class NpcWorkbookGridCell
{
    internal byte[]? Icon;
    internal string Text = "";
}

internal sealed class NpcWorkbookSearchLabels
{
    internal string DataSource = "Calculated data source: Elin Modifier";
    internal string ModificationRestriction = "Unauthorized secondary modification or distribution is strictly prohibited.";
    internal string SheetName = "Search NPCs";
    internal string Icon = "Icon";
    internal string Name = "Name";
    internal string Id = "ID";
    internal string Open = "View NPC Compendium";
}

internal sealed class NpcCompendiumWorkbookWriter : IDisposable
{
    private sealed class WorkbookImage
    {
        internal int Index;
        internal string RelationshipId = "";
        internal string FilePath = "";
    }

    private sealed class NpcWorkbookIndexEntry
    {
        internal string Name = "";
        internal string Id = "";
        internal byte[]? Icon;
        internal int TargetRow;
    }

    private const int ColumnCount = 12;
    private readonly string _outputPath;
    private readonly string _sheetName;
    private readonly NpcWorkbookSearchLabels _searchLabels;
    private readonly string _workDirectory;
    private readonly string _mediaDirectory;
    private readonly string _rowsPath;
    private readonly string _anchorsPath;
    private readonly StreamWriter _rows;
    private readonly StreamWriter _anchors;
    private readonly List<string> _merges = new List<string>();
    private readonly List<NpcWorkbookIndexEntry> _indexEntries = new List<NpcWorkbookIndexEntry>();
    private readonly Dictionary<string, WorkbookImage> _images =
        new Dictionary<string, WorkbookImage>(StringComparer.Ordinal);
    private int _rowIndex;
    private int _shapeIndex;
    private bool _completed;
    private bool _disposed;

    internal NpcCompendiumWorkbookWriter(
        string outputPath,
        string sheetName,
        NpcWorkbookSearchLabels? searchLabels = null)
    {
        _outputPath = outputPath;
        _sheetName = string.IsNullOrWhiteSpace(sheetName) ? "NPC" : sheetName;
        _searchLabels = searchLabels ?? new NpcWorkbookSearchLabels();
        var directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(directory))
            directory = ".";
        Directory.CreateDirectory(directory);
        _workDirectory = Path.Combine(directory, ".npc_workbook_" + Guid.NewGuid().ToString("N"));
        _mediaDirectory = Path.Combine(_workDirectory, "media");
        _rowsPath = Path.Combine(_workDirectory, "rows.xml");
        _anchorsPath = Path.Combine(_workDirectory, "anchors.xml");
        Directory.CreateDirectory(_mediaDirectory);
        _rows = CreateXmlFragmentWriter(_rowsPath);
        _anchors = CreateXmlFragmentWriter(_anchorsPath);
    }

    internal int EmbeddedImageCount => _images.Count;

    internal void AddSpacer(double height = 12d)
    {
        WriteRow(height, Array.Empty<NpcWorkbookCellValue>());
    }

    internal void AddTitle(string title, byte[]? icon, string npcName = "", string npcId = "")
    {
        if (_indexEntries.Count > 0)
            AddNpcSeparator();
        var row = NextRow();
        WriteRowStart(row, 48d);
        WriteCell(row, 0, "", 1);
        WriteMergedCells(row, 1, 11, title, 1);
        WriteRowEnd();
        AddImage(icon, row, 0, 38, 38, 4, 4);
        _indexEntries.Add(new NpcWorkbookIndexEntry
        {
            Name = string.IsNullOrWhiteSpace(npcName) ? title : npcName,
            Id = npcId,
            Icon = icon,
            TargetRow = row
        });
    }

    internal void AddSection(string title)
    {
        var row = NextRow();
        WriteRowStart(row, 28d);
        WriteMergedCells(row, 0, 11, title, 2);
        WriteRowEnd();
    }

    internal void AddFullWidthText(string text, bool alternate = false)
    {
        var row = NextRow();
        var height = CalculateTextHeight(text, 30d, 150d);
        WriteRowStart(row, height);
        WriteMergedCells(row, 0, 11, text, alternate ? 5 : 3);
        WriteRowEnd();
    }

    internal void AddPreview(string leftLabel, byte[]? leftImage, string rightLabel, byte[]? rightImage)
    {
        var labelRow = NextRow();
        WriteRowStart(labelRow, 24d);
        WriteMergedCells(labelRow, 0, 5, leftLabel, 6);
        WriteMergedCells(labelRow, 6, 11, rightLabel, 6);
        WriteRowEnd();

        var imageRow = NextRow();
        WriteRowStart(imageRow, 132d);
        WriteMergedCells(imageRow, 0, 5, "", 3);
        WriteMergedCells(imageRow, 6, 11, "", 3);
        WriteRowEnd();
        AddImage(leftImage, imageRow, 2, 118, 118, 8, 6);
        AddImage(rightImage, imageRow, 8, 118, 118, 8, 6);
    }

    internal void AddGrid(IReadOnlyList<NpcWorkbookGridCell> items, int columns)
    {
        if (items == null || items.Count == 0)
        {
            AddFullWidthText("-");
            return;
        }
        columns = columns == 3 ? 3 : 4;
        var groupWidth = ColumnCount / columns;
        for (var start = 0; start < items.Count; start += columns)
        {
            var row = NextRow();
            var style = (start / columns) % 2 == 0 ? 4 : 5;
            WriteRowStart(row, 34d);
            for (var column = 0; column < columns; column++)
            {
                var firstColumn = column * groupWidth;
                if (start + column >= items.Count)
                {
                    WriteMergedCells(row, firstColumn, firstColumn + groupWidth - 1, "", style);
                    continue;
                }
                var item = items[start + column];
                WriteCell(row, firstColumn, "", style);
                WriteMergedCells(row, firstColumn + 1, firstColumn + groupWidth - 1, item.Text, style);
                AddImage(item.Icon, row, firstColumn, 26, 26, 4, 4);
            }
            WriteRowEnd();
        }
    }

    internal void AddDetail(byte[]? icon, string name, string value, string details, bool alternate)
    {
        var row = NextRow();
        var height = CalculateTextHeight(details, 38d, 210d);
        var style = alternate ? 5 : 4;
        WriteRowStart(row, height);
        WriteCell(row, 0, "", style);
        WriteMergedCells(row, 1, 3, name, style);
        WriteMergedCells(row, 4, 5, value, style);
        WriteMergedCells(row, 6, 11, details, style);
        WriteRowEnd();
        AddImage(icon, row, 0, 28, 28, 4, 5);
    }

    internal void AddSubDetail(byte[]? icon, string label, string value, bool alternate)
    {
        var row = NextRow();
        var height = CalculateTextHeight(value, 32d, 150d);
        var style = alternate ? 8 : 7;
        WriteRowStart(row, height);
        WriteCell(row, 0, "", style);
        WriteMergedCells(row, 1, 3, label, style);
        WriteMergedCells(row, 4, 11, value, style);
        WriteRowEnd();
        AddImage(icon, row, 0, 24, 24, 5, 4);
    }

    private void AddNpcSeparator()
    {
        var row = NextRow();
        WriteRowStart(row, 24d);
        WriteMergedCells(row, 0, 11, new string('-', 64), 9);
        WriteRowEnd();
    }

    internal void Complete()
    {
        if (_completed)
            return;
        _rows.Flush();
        _anchors.Flush();
        _rows.Dispose();
        _anchors.Dispose();

        var temporaryOutput = _outputPath + ".tmp";
        if (File.Exists(temporaryOutput))
            File.Delete(temporaryOutput);
        using (var archive = ZipFile.Open(temporaryOutput, ZipArchiveMode.Create))
        {
            WriteTextEntry(archive, "[Content_Types].xml", BuildContentTypesXml());
            WriteTextEntry(archive, "_rels/.rels", BuildRootRelationshipsXml());
            WriteTextEntry(archive, "docProps/app.xml", BuildAppPropertiesXml());
            WriteTextEntry(archive, "docProps/core.xml", BuildCorePropertiesXml());
            WriteTextEntry(archive, "xl/workbook.xml", BuildWorkbookXml());
            WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationshipsXml());
            WriteTextEntry(archive, "xl/styles.xml", BuildStylesXml());
            WriteSearchWorksheetEntry(archive);
            WriteCompendiumWorksheetEntry(archive);
            WriteSearchDrawingEntry(archive);
            WriteCompendiumDrawingEntry(archive);
            WriteTextEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", BuildWorksheetRelationshipsXml(1));
            WriteTextEntry(archive, "xl/worksheets/_rels/sheet2.xml.rels", BuildWorksheetRelationshipsXml(2));
            WriteTextEntry(archive, "xl/drawings/_rels/drawing1.xml.rels", BuildDrawingRelationshipsXml());
            WriteTextEntry(archive, "xl/drawings/_rels/drawing2.xml.rels", BuildDrawingRelationshipsXml());
            foreach (var image in _images.Values)
                WriteFileEntry(archive, "xl/media/image" + image.Index.ToString(CultureInfo.InvariantCulture) + ".png", image.FilePath);
        }
        if (File.Exists(_outputPath))
            File.Delete(_outputPath);
        File.Move(temporaryOutput, _outputPath);
        _completed = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (!_completed)
        {
            _rows.Dispose();
            _anchors.Dispose();
        }
        try
        {
            if (Directory.Exists(_workDirectory))
                Directory.Delete(_workDirectory, true);
        }
        catch
        {
        }
    }

    private int NextRow()
    {
        _rowIndex++;
        return _rowIndex;
    }

    private void WriteRow(double height, IReadOnlyList<NpcWorkbookCellValue> cells)
    {
        var row = NextRow();
        WriteRowStart(row, height);
        for (var i = 0; i < cells.Count; i++)
            WriteCell(row, cells[i].Column, cells[i].Value, cells[i].Style);
        WriteRowEnd();
    }

    private void WriteRowStart(int row, double height)
    {
        _rows.Write("<row r=\"");
        _rows.Write(row.ToString(CultureInfo.InvariantCulture));
        _rows.Write("\" ht=\"");
        _rows.Write(height.ToString("0.##", CultureInfo.InvariantCulture));
        _rows.Write("\" customHeight=\"1\">");
    }

    private void WriteRowEnd()
    {
        _rows.Write("</row>");
    }

    private void WriteCell(int row, int column, string value, int style)
    {
        WriteCell(_rows, row, column, value, style);
    }

    private static void WriteCell(TextWriter writer, int row, int column, string value, int style)
    {
        writer.Write("<c r=\"");
        writer.Write(GetColumnName(column));
        writer.Write(row.ToString(CultureInfo.InvariantCulture));
        writer.Write("\" s=\"");
        writer.Write(style.ToString(CultureInfo.InvariantCulture));
        writer.Write("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
        writer.Write(EscapeXml(value));
        writer.Write("</t></is></c>");
    }

    private void WriteMergedCells(int row, int firstColumn, int lastColumn, string value, int style)
    {
        WriteCell(row, firstColumn, value, style);
        for (var column = firstColumn + 1; column <= lastColumn; column++)
            WriteCell(row, column, "", style);
        AddMerge(row, firstColumn, lastColumn);
    }

    private void AddMerge(int row, int firstColumn, int lastColumn)
    {
        if (lastColumn <= firstColumn)
            return;
        _merges.Add(GetColumnName(firstColumn) + row.ToString(CultureInfo.InvariantCulture) + ":" +
                    GetColumnName(lastColumn) + row.ToString(CultureInfo.InvariantCulture));
    }

    private void AddImage(byte[]? png, int row, int column, int width, int height, int offsetX, int offsetY)
    {
        if (png == null || png.Length == 0)
            return;
        var image = GetOrCreateImage(png);
        WriteImageAnchor(_anchors, image, row, column, width, height, offsetX, offsetY, ref _shapeIndex, "NPC Image ");
    }

    private static void WriteImageAnchor(
        TextWriter writer,
        WorkbookImage image,
        int row,
        int column,
        int width,
        int height,
        int offsetX,
        int offsetY,
        ref int shapeIndex,
        string namePrefix)
    {
        shapeIndex++;
        const long emuPerPixel = 9525L;
        writer.Write("<xdr:oneCellAnchor><xdr:from><xdr:col>");
        writer.Write(column.ToString(CultureInfo.InvariantCulture));
        writer.Write("</xdr:col><xdr:colOff>");
        writer.Write((offsetX * emuPerPixel).ToString(CultureInfo.InvariantCulture));
        writer.Write("</xdr:colOff><xdr:row>");
        writer.Write((row - 1).ToString(CultureInfo.InvariantCulture));
        writer.Write("</xdr:row><xdr:rowOff>");
        writer.Write((offsetY * emuPerPixel).ToString(CultureInfo.InvariantCulture));
        writer.Write("</xdr:rowOff></xdr:from><xdr:ext cx=\"");
        writer.Write((width * emuPerPixel).ToString(CultureInfo.InvariantCulture));
        writer.Write("\" cy=\"");
        writer.Write((height * emuPerPixel).ToString(CultureInfo.InvariantCulture));
        writer.Write("\"/><xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"");
        writer.Write(shapeIndex.ToString(CultureInfo.InvariantCulture));
        writer.Write("\" name=\"");
        writer.Write(EscapeXml(namePrefix));
        writer.Write(shapeIndex.ToString(CultureInfo.InvariantCulture));
        writer.Write("\"/><xdr:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></xdr:cNvPicPr></xdr:nvPicPr><xdr:blipFill><a:blip r:embed=\"");
        writer.Write(image.RelationshipId);
        writer.Write("\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill><xdr:spPr><a:xfrm/><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:oneCellAnchor>");
    }

    private WorkbookImage GetOrCreateImage(byte[] png)
    {
        string hash;
        using (var sha256 = SHA256.Create())
            hash = Convert.ToBase64String(sha256.ComputeHash(png));
        if (_images.TryGetValue(hash, out var existing))
            return existing;
        var index = _images.Count + 1;
        var image = new WorkbookImage
        {
            Index = index,
            RelationshipId = "rId" + index.ToString(CultureInfo.InvariantCulture),
            FilePath = Path.Combine(_mediaDirectory, "image" + index.ToString(CultureInfo.InvariantCulture) + ".png")
        };
        File.WriteAllBytes(image.FilePath, png);
        _images.Add(hash, image);
        return image;
    }

    private void WriteSearchWorksheetEntry(ZipArchive archive)
    {
        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        var lastRow = Math.Max(4, _indexEntries.Count + 4);
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><dimension ref=\"A1:D");
        writer.Write(lastRow.ToString(CultureInfo.InvariantCulture));
        writer.Write("\"/><sheetViews><sheetView showGridLines=\"0\" workbookViewId=\"0\"><pane ySplit=\"4\" topLeftCell=\"A5\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><sheetFormatPr defaultRowHeight=\"24\"/><cols><col min=\"1\" max=\"1\" width=\"8\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"40\" customWidth=\"1\"/><col min=\"3\" max=\"3\" width=\"30\" customWidth=\"1\"/><col min=\"4\" max=\"4\" width=\"22\" customWidth=\"1\"/></cols><sheetData><row r=\"1\" ht=\"28\" customHeight=\"1\">");
        WriteCell(writer, 1, 0, _searchLabels.DataSource, 7);
        WriteCell(writer, 1, 1, "", 7);
        WriteCell(writer, 1, 2, "", 7);
        WriteCell(writer, 1, 3, "", 7);
        writer.Write("</row><row r=\"2\" ht=\"32\" customHeight=\"1\">");
        WriteCell(writer, 2, 0, _searchLabels.ModificationRestriction, 2);
        WriteCell(writer, 2, 1, "", 2);
        WriteCell(writer, 2, 2, "", 2);
        WriteCell(writer, 2, 3, "", 2);
        writer.Write("</row><row r=\"3\" ht=\"42\" customHeight=\"1\">");
        WriteCell(writer, 3, 0, _searchLabels.SheetName, 1);
        WriteCell(writer, 3, 1, "", 1);
        WriteCell(writer, 3, 2, "", 1);
        WriteCell(writer, 3, 3, "", 1);
        writer.Write("</row><row r=\"4\" ht=\"28\" customHeight=\"1\">");
        WriteCell(writer, 4, 0, _searchLabels.Icon, 10);
        WriteCell(writer, 4, 1, _searchLabels.Name, 10);
        WriteCell(writer, 4, 2, _searchLabels.Id, 10);
        WriteCell(writer, 4, 3, _searchLabels.Open, 10);
        writer.Write("</row>");
        for (var i = 0; i < _indexEntries.Count; i++)
        {
            var indexEntry = _indexEntries[i];
            var row = i + 5;
            var alternate = i % 2 != 0;
            var style = alternate ? 12 : 11;
            var linkStyle = alternate ? 14 : 13;
            writer.Write("<row r=\"");
            writer.Write(row.ToString(CultureInfo.InvariantCulture));
            writer.Write("\" ht=\"32\" customHeight=\"1\">");
            WriteCell(writer, row, 0, "", style);
            WriteCell(writer, row, 1, indexEntry.Name, style);
            WriteCell(writer, row, 2, indexEntry.Id, style);
            WriteCell(writer, row, 3, _searchLabels.Open, linkStyle);
            writer.Write("</row>");
        }
        writer.Write("</sheetData><mergeCells count=\"3\"><mergeCell ref=\"A1:D1\"/><mergeCell ref=\"A2:D2\"/><mergeCell ref=\"A3:D3\"/></mergeCells><autoFilter ref=\"A4:D");
        writer.Write(lastRow.ToString(CultureInfo.InvariantCulture));
        writer.Write("\"/>");
        if (_indexEntries.Count > 0)
        {
            writer.Write("<hyperlinks>");
            for (var i = 0; i < _indexEntries.Count; i++)
            {
                var row = i + 5;
                writer.Write("<hyperlink ref=\"D");
                writer.Write(row.ToString(CultureInfo.InvariantCulture));
                writer.Write("\" location=\"");
                writer.Write(EscapeXml("'" + _sheetName.Replace("'", "''") + "'!A" +
                    _indexEntries[i].TargetRow.ToString(CultureInfo.InvariantCulture)));
                writer.Write("\" display=\"");
                writer.Write(EscapeXml(_searchLabels.Open));
                writer.Write("\"/>");
            }
            writer.Write("</hyperlinks>");
        }
        writer.Write("<pageMargins left=\"0.25\" right=\"0.25\" top=\"0.5\" bottom=\"0.5\" header=\"0.2\" footer=\"0.2\"/><drawing r:id=\"rId1\"/></worksheet>");
    }

    private void WriteCompendiumWorksheetEntry(ZipArchive archive)
    {
        var entry = archive.CreateEntry("xl/worksheets/sheet2.xml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><dimension ref=\"A1:L");
        writer.Write(Math.Max(1, _rowIndex).ToString(CultureInfo.InvariantCulture));
        writer.Write("\"/><sheetViews><sheetView showGridLines=\"0\" workbookViewId=\"0\"/></sheetViews><sheetFormatPr defaultRowHeight=\"18\"/><cols>");
        for (var i = 1; i <= ColumnCount; i++)
        {
            writer.Write("<col min=\"");
            writer.Write(i.ToString(CultureInfo.InvariantCulture));
            writer.Write("\" max=\"");
            writer.Write(i.ToString(CultureInfo.InvariantCulture));
            writer.Write("\" width=\"12\" customWidth=\"1\"/>");
        }
        writer.Write("</cols><sheetData>");
        writer.Flush();
        CopyTextFile(_rowsPath, stream);
        writer.Write("</sheetData>");
        if (_merges.Count > 0)
        {
            writer.Write("<mergeCells count=\"");
            writer.Write(_merges.Count.ToString(CultureInfo.InvariantCulture));
            writer.Write("\">");
            for (var i = 0; i < _merges.Count; i++)
            {
                writer.Write("<mergeCell ref=\"");
                writer.Write(_merges[i]);
                writer.Write("\"/>");
            }
            writer.Write("</mergeCells>");
        }
        writer.Write("<pageMargins left=\"0.25\" right=\"0.25\" top=\"0.5\" bottom=\"0.5\" header=\"0.2\" footer=\"0.2\"/><drawing r:id=\"rId1\"/></worksheet>");
    }

    private void WriteSearchDrawingEntry(ZipArchive archive)
    {
        var entry = archive.CreateEntry("xl/drawings/drawing1.xml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        var shapeIndex = 0;
        for (var i = 0; i < _indexEntries.Count; i++)
        {
            var icon = _indexEntries[i].Icon;
            if (icon == null || icon.Length == 0)
                continue;
            var image = GetOrCreateImage(icon);
            WriteImageAnchor(writer, image, i + 5, 0, 24, 24, 5, 4, ref shapeIndex, "NPC Search Image ");
        }
        writer.Write("</xdr:wsDr>");
    }

    private void WriteCompendiumDrawingEntry(ZipArchive archive)
    {
        var entry = archive.CreateEntry("xl/drawings/drawing2.xml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        writer.Flush();
        CopyTextFile(_anchorsPath, stream);
        writer.Write("</xdr:wsDr>");
    }

    private string BuildContentTypesXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/><Override PartName=\"/xl/drawings/drawing1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/><Override PartName=\"/xl/drawings/drawing2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/><Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/><Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/></Types>";
    }

    private static string BuildRootRelationshipsXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/></Relationships>";
    }

    private string BuildAppPropertiesXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><Application>Elin Modifier</Application><TitlesOfParts><vt:vector size=\"2\" baseType=\"lpstr\"><vt:lpstr>" + EscapeXml(_searchLabels.SheetName) + "</vt:lpstr><vt:lpstr>" + EscapeXml(_sheetName) + "</vt:lpstr></vt:vector></TitlesOfParts></Properties>";
    }

    private static string BuildCorePropertiesXml()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><dc:creator>Liset</dc:creator><cp:lastModifiedBy>Elin Modifier</cp:lastModifiedBy><dcterms:created xsi:type=\"dcterms:W3CDTF\">" + timestamp + "</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">" + timestamp + "</dcterms:modified></cp:coreProperties>";
    }

    private string BuildWorkbookXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><bookViews><workbookView activeTab=\"0\"/></bookViews><sheets><sheet name=\"" + EscapeXml(_searchLabels.SheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"" + EscapeXml(_sheetName) + "\" sheetId=\"2\" r:id=\"rId2\"/></sheets><calcPr calcId=\"191029\"/></workbook>";
    }

    private static string BuildWorkbookRelationshipsXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";
    }

    private static string BuildWorksheetRelationshipsXml(int drawingIndex)
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing" + drawingIndex.ToString(CultureInfo.InvariantCulture) + ".xml\"/></Relationships>";
    }

    private string BuildDrawingRelationshipsXml()
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        foreach (var image in _images.Values)
        {
            builder.Append("<Relationship Id=\"");
            builder.Append(image.RelationshipId);
            builder.Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/image");
            builder.Append(image.Index.ToString(CultureInfo.InvariantCulture));
            builder.Append(".png\"/>");
        }
        builder.Append("</Relationships>");
        return builder.ToString();
    }

    private static string BuildStylesXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"5\"><font><sz val=\"11\"/><name val=\"Microsoft YaHei UI\"/><family val=\"2\"/></font><font><b/><sz val=\"18\"/><name val=\"Microsoft YaHei UI\"/><family val=\"2\"/></font><font><b/><sz val=\"14\"/><name val=\"Microsoft YaHei UI\"/><family val=\"2\"/><color rgb=\"FF2E332D\"/></font><font><sz val=\"10\"/><name val=\"Microsoft YaHei UI\"/><family val=\"2\"/><color rgb=\"FF5F665D\"/></font><font><u/><sz val=\"11\"/><name val=\"Microsoft YaHei UI\"/><family val=\"2\"/><color rgb=\"FF2F6F9F\"/></font></fonts><fills count=\"5\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFFFFF\"/><bgColor indexed=\"64\"/></patternFill></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE5E9DF\"/><bgColor indexed=\"64\"/></patternFill></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF8F8F1\"/><bgColor indexed=\"64\"/></patternFill></fill></fills><borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style=\"thin\"><color rgb=\"FF2E332D\"/></left><right style=\"thin\"><color rgb=\"FF2E332D\"/></right><top style=\"thin\"><color rgb=\"FF2E332D\"/></top><bottom style=\"thin\"><color rgb=\"FF2E332D\"/></bottom><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"15\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf><xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf><xf numFmtId=\"0\" fontId=\"0\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"0\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"2\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf><xf numFmtId=\"0\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"3\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"3\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf><xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf><xf numFmtId=\"0\" fontId=\"0\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf><xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf><xf numFmtId=\"0\" fontId=\"4\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf><xf numFmtId=\"0\" fontId=\"4\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";
    }

    private static StreamWriter CreateXmlFragmentWriter(string path)
    {
        return new StreamWriter(path, false, new UTF8Encoding(false), 65536);
    }

    private static void WriteTextEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void WriteFileEntry(ZipArchive archive, string path, string sourcePath)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var target = entry.Open();
        using var source = File.OpenRead(sourcePath);
        source.CopyTo(target);
    }

    private static void CopyTextFile(string path, Stream target)
    {
        using var source = File.OpenRead(path);
        source.CopyTo(target);
    }

    private static double CalculateTextHeight(string text, double minimum, double maximum)
    {
        var lines = 1;
        if (!string.IsNullOrEmpty(text))
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                    lines++;
            }
            lines += text.Length / 90;
        }
        return Math.Max(minimum, Math.Min(maximum, 12d + lines * 15d));
    }

    private static string GetColumnName(int zeroBasedColumn)
    {
        var value = zeroBasedColumn + 1;
        var result = "";
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }

    private static string EscapeXml(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private readonly struct NpcWorkbookCellValue
    {
        internal NpcWorkbookCellValue(int column, string value, int style)
        {
            Column = column;
            Value = value;
            Style = style;
        }

        internal int Column { get; }
        internal string Value { get; }
        internal int Style { get; }
    }
}
