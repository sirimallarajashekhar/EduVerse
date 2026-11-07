using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace EduVerse.Application.Reporting;

/// <summary>Fixed-schema numeric reports without PII. No template or untrusted markup evaluation.</summary>
public static class ReportDocumentWriter
{
    private static string Num(decimal number) => number.ToString("F2", CultureInfo.InvariantCulture);
    private static string[][] Rows(InstitutionAnalytics report)
    {
        var data = new List<string[]>
        {
            ["Year", "Month", "Enrollments", "Payments", "Collected INR"]
        };
        data.AddRange(report.Months.Select(x => new[]
        {
            x.Year.ToString(CultureInfo.InvariantCulture),
            x.Month.ToString(CultureInfo.InvariantCulture),
            x.Enrollments.ToString(CultureInfo.InvariantCulture),
            x.Payments.ToString(CultureInfo.InvariantCulture),
            Num(x.CollectedInr)
        }));
        return data.ToArray();
    }

    public static byte[] Xlsx(InstitutionAnalytics report)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            Write(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Write(zip, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                 xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <sheets><sheet name="Monthly report" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            Write(zip, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            var sheet = new StringBuilder();
            sheet.Append("""
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                <cols><col min="1" max="5" width="19" customWidth="1"/></cols><sheetData>
                """);
            var rows = Rows(report);
            for (var i = 0; i < rows.Length; i++)
            {
                sheet.Append("<row r=\"").Append(i + 1).Append("\">");
                for (var j = 0; j < rows[i].Length; j++)
                {
                    var refId = ((char)('A' + j)).ToString() + (i + 1).ToString(CultureInfo.InvariantCulture);
                    if (i == 0)
                    {
                        sheet.Append("<c r=\"").Append(refId).Append("\" t=\"inlineStr\"><is><t>")
                            .Append(rows[i][j]).Append("</t></is></c>");
                    }
                    else
                    {
                        sheet.Append("<c r=\"").Append(refId).Append("\"><v>")
                            .Append(rows[i][j]).Append("</v></c>");
                    }
                }
                sheet.Append("</row>");
            }
            sheet.Append("</sheetData></worksheet>");
            Write(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return memory.ToArray();
    }
    private static void Write(ZipArchive archive, string path, string contents)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }

    /// <summary>Small standards-based, one-page PDF summary; max 12 monthly rows.</summary>
    public static byte[] Pdf(InstitutionAnalytics report)
    {
        var lines = new List<string>
        {
            "EduVerse - Monthly Institution Report",
            "Campus ID: " + report.Kpis.CampusId.ToString("D"),
            "Generated UTC: " + report.GeneratedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            "Month       Enrollments       Payments       Collected INR"
        };
        foreach (var row in report.Months.Take(13))
            lines.Add($"{row.Year:D4}-{row.Month:D2}             {row.Enrollments,5}                 {row.Payments,5}             {Num(row.CollectedInr),14}");
        if (report.Months.Count > 13)
            throw new ArgumentException("PDF output requires a range of at most 13 calendar months.");
        var stream = new StringBuilder("BT /F1 11 Tf 50 785 Td 15 TL\n");
        foreach (var line in lines)
            stream.Append('(').Append(line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)"))
                .Append(") Tj T*\n");
        stream.Append("ET");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>",
            "<< /Length " + Encoding.ASCII.GetByteCount(stream.ToString()) + " >>\nstream\n" + stream + "\nendstream"
        };
        using var memory = new MemoryStream();
        void Text(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            memory.Write(bytes, 0, bytes.Length);
        }
        Text("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(memory.Position);
            Text($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = memory.Position;
        Text($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            Text($"{offset:0000000000} 00000 n \n");
        Text($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return memory.ToArray();
    }
}
