using System.Globalization;
using System.Text;
using NPOI.SS.UserModel;
using SB.Framework.Core.Attribute;

namespace SB.Framework.Services
{
    /// <summary>
    /// 基于 NPOI 的 <see cref="ISpreadsheetReader"/> 实现。
    /// Excel(xls/xlsx)经 NPOI WorkbookFactory 自动识别二进制格式;csv 走内置文本解析(支持引号包裹的逗号)。
    /// </summary>
    [AutoDependencyInjection]
    public sealed class NpoiSpreadsheetReader : ISpreadsheetReader
    {
        public IReadOnlyList<SpreadsheetSheet> Read(Stream stream, string? fileName = null)
        {
            if (fileName != null && fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                return new[] { ReadCsv(stream) };

            // NPOI 需要可 seek 的流;IFormFile.OpenReadStream 不可 seek,统一复制到 MemoryStream。
            using var ms = ToSeekable(stream);
            var workbook = WorkbookFactory.Create(ms);
            var formatter = new DataFormatter(CultureInfo.InvariantCulture);
            var result = new List<SpreadsheetSheet>();

            for (int s = 0; s < workbook.NumberOfSheets; s++)
            {
                var sheet = workbook.GetSheetAt(s);
                var data = new SpreadsheetSheet { Name = sheet.SheetName };
                for (int r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
                {
                    var row = sheet.GetRow(r);
                    var cells = new List<string>();
                    if (row != null)
                    {
                        for (int c = 0; c < row.LastCellNum; c++)
                            cells.Add(CellToString(row.GetCell(c), formatter));
                    }
                    data.Rows.Add(cells);
                }
                result.Add(data);
            }
            return result;
        }

        public IReadOnlyList<string> ReadCells(Stream stream, string? fileName = null)
        {
            var sheets = Read(stream, fileName);
            var first = sheets.Count > 0 ? sheets[0] : null;
            if (first == null) return Array.Empty<string>();
            return first.Rows
                .SelectMany(row => row)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .ToList();
        }

        private static MemoryStream ToSeekable(Stream stream)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }

        /// <summary>单元格 → 字符串。数值整数去掉科学计数法/小数尾(避免手机号失真),日期格式化。</summary>
        private static string CellToString(ICell? cell, DataFormatter formatter)
        {
            if (cell == null) return "";
            switch (cell.CellType)
            {
                case CellType.String:
                    return cell.StringCellValue ?? "";
                case CellType.Numeric:
                    if (DateUtil.IsCellDateFormatted(cell))
                        return cell.DateCellValue?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
                    var d = cell.NumericCellValue;
                    if (!double.IsInfinity(d) && !double.IsNaN(d) && d == Math.Floor(d)
                        && d is >= long.MinValue and <= long.MaxValue)
                        return ((long)d).ToString(CultureInfo.InvariantCulture);
                    return d.ToString(CultureInfo.InvariantCulture);
                case CellType.Boolean:
                    return cell.BooleanCellValue ? "TRUE" : "FALSE";
                case CellType.Formula:
                    try { return formatter.FormatCellValue(cell); }
                    catch { return ""; }
                default:
                    return "";
            }
        }

        /// <summary>极简 CSV 解析:支持逗号分隔、双引号包裹(含转义 "")、CRLF/LF 换行。</summary>
        private static SpreadsheetSheet ReadCsv(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            var sheet = new SpreadsheetSheet { Name = "csv" };

            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;

            void EndField() { row.Add(field.ToString()); field.Clear(); }
            void EndRow() { EndField(); sheet.Rows.Add(row.ToList()); row.Clear(); }

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(ch);
                }
                else
                {
                    switch (ch)
                    {
                        case '"': inQuotes = true; break;
                        case ',': EndField(); break;
                        case '\r': break;
                        case '\n': EndRow(); break;
                        default: field.Append(ch); break;
                    }
                }
            }
            // 收尾最后一行(无尾随换行时)
            if (field.Length > 0 || row.Count > 0) EndRow();
            return sheet;
        }
    }
}
