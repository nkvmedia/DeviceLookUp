using System.Globalization;
using System.Text;
using DeviceLookup.Core.Models;

namespace DeviceLookup.Core.Services;

public sealed class CsvImport
{
    public List<DeviceResult> Rows { get; init; } = new();
    public string[] Headers { get; init; } = Array.Empty<string>();
    public int SerialColumn { get; init; }
    public char Delimiter { get; init; }
}

public static class CsvService
{
    private static readonly string[] SerialHeaders =
    {
        "serial", "serialnumber", "serialno", "servicetag", "sn", "s/n", "serial/servicetag",
        "serialservicetag", "seriennummer", "сериен номер", "серийный номер"
    };

    public static CsvImport Import(string path)
    {
        var text = ReadText(path);
        var delimiter = DetectDelimiter(text);
        var table = Parse(text, delimiter);
        if (table.Count == 0) throw new InvalidDataException("The file is empty.");

        var headers = table[0].Select(h => h.Trim()).ToArray();
        var col = FindSerialColumn(headers);
        if (col < 0)
            throw new InvalidDataException(
                "Couldn't find a serial number column. Name the column \"Serial\", \"Serial Number\" or \"Service Tag\".");

        var rows = new List<DeviceResult>();
        foreach (var line in table.Skip(1))
        {
            var serial = SerialNormalizer.Normalize(col < line.Count ? line[col] : "");
            if (serial.Length == 0) continue;
            var r = new DeviceResult { SerialNumber = serial };
            for (var i = 0; i < headers.Length; i++)
                if (i != col && headers[i].Length > 0 && !r.OriginalColumns.ContainsKey(headers[i]))
                    r.OriginalColumns[headers[i]] = i < line.Count ? line[i] : "";
            rows.Add(r);
        }
        return new CsvImport { Rows = rows, Headers = headers, SerialColumn = col, Delimiter = delimiter };
    }

    public static void Export(IEnumerable<DeviceResult> rows, string path)
    {
        var list = rows.ToList();
        var delimiter = CultureInfo.CurrentCulture.TextInfo.ListSeparator is { Length: 1 } sep ? sep[0] : ',';
        var extra = list.SelectMany(r => r.OriginalColumns.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var header = new List<string>
        {
            "Serial", "Manufacturer", "Product", "Model", "MTM", "Warranty", "WarrantyPlan",
            "WarrantyStart", "WarrantyEnd", "DaysRemaining", "Status", "Error", "LookupTime"
        };
        header.AddRange(extra);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(delimiter, header.Select(h => Quote(h, delimiter))));
        foreach (var r in list)
        {
            var cells = new List<string>
            {
                r.SerialNumber, r.Manufacturer, r.Product, r.Model, r.Mtm, r.WarrantyLabel, r.WarrantyName,
                r.WarrantyStart?.ToString("yyyy-MM-dd") ?? "", r.WarrantyEnd?.ToString("yyyy-MM-dd") ?? "",
                r.DaysRemaining?.ToString(CultureInfo.InvariantCulture) ?? "", r.StatusText, r.Error,
                r.LookupTime?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? ""
            };
            cells.AddRange(extra.Select(k => r.OriginalColumns.TryGetValue(k, out var v) ? v : ""));
            sb.AppendLine(string.Join(delimiter, cells.Select(c => Quote(c, delimiter))));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));   // BOM so Excel reads Cyrillic correctly
    }

    /// <summary>UTF-8 when valid, otherwise Windows-1251 (common for Bulgarian Excel exports).</summary>
    public static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            text = Encoding.GetEncoding(1251).GetString(bytes);
        }
        return text.TrimStart('\uFEFF');
    }

    public static char DetectDelimiter(string text)
    {
        int comma = 0, semi = 0, tab = 0;
        var inQuotes = false;
        foreach (var c in text)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (!inQuotes)
            {
                if (c == '\n') break;
                if (c == ',') comma++;
                else if (c == ';') semi++;
                else if (c == '\t') tab++;
            }
        }
        if (semi > comma && semi >= tab) return ';';
        if (tab > comma && tab > semi) return '\t';
        return ',';
    }

    public static int FindSerialColumn(string[] headers)
    {
        static string Clean(string h) =>
            new string(h.Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-').ToArray()).ToLowerInvariant();
        for (var i = 0; i < headers.Length; i++)
            if (SerialHeaders.Contains(Clean(headers[i]))) return i;
        return -1;
    }

    /// <summary>RFC 4180 style parser: quoted fields, doubled quotes, line breaks inside quotes.</summary>
    public static List<List<string>> Parse(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else inQuotes = false;
                }
                else cell.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == delimiter) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows.Where(r => r.Any(x => x.Length > 0)).ToList();
    }

    private static string Quote(string value, char delimiter)
    {
        // Stop spreadsheet formulas from running if a value starts with = + - @
        if (value.Length > 0 && "=+-@".Contains(value[0]) && !double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        var needs = value.Contains(delimiter) || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return needs ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
