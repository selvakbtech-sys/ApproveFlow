using System.IO.Compression;
using System.Text;

namespace ApproveFlow.MVC.Services;

// Minimal .xlsx writer with no external dependency (an .xlsx is a zip of OpenXML parts).
// Produces one sheet with a styled, frozen, filterable header row, fixed column widths and
// typed cells (text wraps, numbers keep #,##0.00, dates keep dd-MMM-yyyy), so the download
// opens in Excel already laid out — no "########" columns, no text-that-looks-like-a-number.
public enum XlsxKind { Text, Number, Date }

public sealed record XlsxColumn(string Header, double Width, XlsxKind Kind = XlsxKind.Text, bool Wrap = false);

public static class XlsxWriter
{
    // Style indexes into cellXfs below.
    private const int StyleHeader = 1, StyleText = 2, StyleWrap = 3, StyleNumber = 4, StyleDate = 5;

    public static byte[] Build(string sheetName, IReadOnlyList<XlsxColumn> columns, IEnumerable<object?[]> rows)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", ContentTypes);
            Add(zip, "_rels/.rels", Rels);
            Add(zip, "xl/workbook.xml", Workbook(sheetName));
            Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
            Add(zip, "xl/styles.xml", Styles);
            Add(zip, "xl/worksheets/sheet1.xml", Sheet(columns, rows));
        }
        return ms.ToArray();
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        var e = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private static string Sheet(IReadOnlyList<XlsxColumn> cols, IEnumerable<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        // Freeze the header row.
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        sb.Append("<cols>");
        for (int i = 0; i < cols.Count; i++)
            sb.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{cols[i].Width.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
        sb.Append("</cols><sheetData>");

        sb.Append("<row r=\"1\" ht=\"22\" customHeight=\"1\">");
        for (int i = 0; i < cols.Count; i++)
            sb.Append($"<c r=\"{Ref(i, 1)}\" s=\"{StyleHeader}\" t=\"inlineStr\"><is><t>{Esc(cols[i].Header)}</t></is></c>");
        sb.Append("</row>");

        int r = 1;
        foreach (var row in rows)
        {
            r++;
            sb.Append($"<row r=\"{r}\">");
            for (int i = 0; i < cols.Count; i++)
            {
                var v = i < row.Length ? row[i] : null;
                var col = cols[i];
                var cell = Ref(i, r);
                switch (col.Kind)
                {
                    case XlsxKind.Number when v is decimal or double or int or long:
                        sb.Append($"<c r=\"{cell}\" s=\"{StyleNumber}\"><v>{Convert.ToDecimal(v).ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    case XlsxKind.Date when v is DateTimeOffset dto:
                        sb.Append($"<c r=\"{cell}\" s=\"{StyleDate}\"><v>{Serial(dto.DateTime)}</v></c>");
                        break;
                    case XlsxKind.Date when v is DateTime dt:
                        sb.Append($"<c r=\"{cell}\" s=\"{StyleDate}\"><v>{Serial(dt)}</v></c>");
                        break;
                    default:
                    {
                        var text = v?.ToString();
                        var style = col.Kind == XlsxKind.Number ? StyleNumber : col.Kind == XlsxKind.Date ? StyleDate : col.Wrap ? StyleWrap : StyleText;
                        if (string.IsNullOrEmpty(text)) sb.Append($"<c r=\"{cell}\" s=\"{style}\"/>");
                        else sb.Append($"<c r=\"{cell}\" s=\"{style}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(text)}</t></is></c>");
                        break;
                    }
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");
        sb.Append($"<autoFilter ref=\"A1:{Ref(cols.Count - 1, Math.Max(r, 1))}\"/>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    // Excel date serial (days since 1899-12-30).
    private static string Serial(DateTime dt) =>
        (dt - new DateTime(1899, 12, 30)).TotalDays.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Ref(int colIndex, int row)
    {
        var s = "";
        colIndex++;
        while (colIndex > 0) { int m = (colIndex - 1) % 26; s = (char)('A' + m) + s; colIndex = (colIndex - m) / 26; }
        return s + row;
    }

    // XML-escape and drop control characters Excel refuses (keeps tab / newline).
    private static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') continue;
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static string Workbook(string sheetName) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        $"<sheets><sheet name=\"{Esc(sheetName.Length > 31 ? sheetName[..31] : sheetName)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

    private const string ContentTypes =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "</Types>";

    private const string Rels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private const string WorkbookRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    // cellXfs: 0 default · 1 header (bold white on navy, centred) · 2 text · 3 wrapped text
    // (top-aligned) · 4 number #,##0.00 · 5 date dd-MMM-yyyy. All data cells carry a thin border.
    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.00\"/><numFmt numFmtId=\"165\" formatCode=\"dd-mmm-yyyy\"/></numFmts>" +
        "<fonts count=\"2\"><font><sz val=\"10\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF071B46\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
        "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
        "<border><left style=\"thin\"><color rgb=\"FFD0D5DD\"/></left><right style=\"thin\"><color rgb=\"FFD0D5DD\"/></right><top style=\"thin\"><color rgb=\"FFD0D5DD\"/></top><bottom style=\"thin\"><color rgb=\"FFD0D5DD\"/></bottom><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"6\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"top\"/></xf>" +
        "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"top\"/></xf>" +
        "</cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";
}
