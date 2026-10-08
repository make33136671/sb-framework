using System.Text;
using System.Text.RegularExpressions;
using NPOI.XWPF.UserModel;

namespace SB.Framework.Services
{
    /// <summary>
    /// 基于 NPOI XWPF 的 <see cref="IDocumentReader"/> 实现（Word .docx/.docm）。
    ///
    /// 要点：
    /// * 段落保序读取（含标题层级），表格单独抽取——不丢 Word 里常见的"角色表/分集表"；
    /// * 表格单元格文本递归取（单元格里还能嵌段落与表格）；
    /// * 中文剧本常直接把"第N集/第N章"写成正文而非 Word 标题样式，故额外按文本特征识别标题；
    /// * 接口绑定在框架管线 AddSbFramework 里显式注册（非 [AutoDependencyInjection]，后者只注册具体类型不映射接口）。
    ///
    /// 限制：只支持 OOXML（docx/docm）。.doc 是 Word 97-2003 二进制格式，NPOI 的 HWPF 未随包发布，
    /// 遇到时给出明确的"请另存为 .docx"提示，而不是抛晦涩的压缩包异常。
    /// </summary>
    public sealed class NpoiDocumentReader : IDocumentReader
    {
        /// <summary>支持的扩展名（.docm 与 .docx 同为 OOXML 且 NPOI 可直接打开）。</summary>
        private static readonly string[] SupportedExtensions = { ".docx", ".docm" };

        /// <summary>纯文本格式：不依赖外部库，直接按编码读出（剧本常见 .txt 导出）。</summary>
        private static readonly string[] TextExtensions = { ".txt", ".md" };

        /// <summary>中文剧本标题特征：第N集 / 第N章 / 第N幕 / 第N场（N 可为中文数字或阿拉伯数字）。</summary>
        private static readonly Regex HeadingPattern = new(
            @"^\s*第\s*[0-9一二三四五六七八九十百零两]+\s*[集章节幕场回](?:\s|$|[：:、.．])",
            RegexOptions.Compiled);

        public bool CanRead(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            var ext = Path.GetExtension(fileName);
            return SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)
                || TextExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        public DocumentContent Read(Stream stream, string? fileName = null)
        {
            var ext = string.IsNullOrWhiteSpace(fileName)
                ? ""
                : Path.GetExtension(fileName).ToLowerInvariant();

            if (TextExtensions.Contains(ext, StringComparer.Ordinal))
                return ReadPlainText(stream);

            if (ext == ".doc")
                throw new NotSupportedException(
                    "不支持 .doc（Word 97-2003 二进制格式），请在 Word 中另存为 .docx 后重新上传。");

            // NPOI 需要可 seek 的流；IFormFile.OpenReadStream 不可 seek，统一复制到 MemoryStream。
            using var ms = ToSeekable(stream);

            if (ms.Length == 0)
                throw new InvalidDataException("文件为空。");

            // 非 zip 魔数 → 几乎可以断定不是 docx（docx/docm 都是 ZIP 容器）。
            if (!LooksLikeZip(ms))
                throw new NotSupportedException(
                    "文件不是有效的 .docx（Office Open XML）文档，请确认文件格式后重新上传。");

            ms.Position = 0;
            using var doc = new XWPFDocument(ms);

            var content = new DocumentContent();
            var text = new StringBuilder();

            // ★ 保序遍历 body：段落与表格按文档中的真实先后顺序处理。
            //   若分别走 doc.Paragraphs / doc.Tables 会丢掉相对位置，剧本里"分集表格穿插在正文中"就会错位。
            foreach (var element in doc.BodyElements)
            {
                switch (element.ElementType)
                {
                    case BodyElementType.PARAGRAPH:
                        AppendParagraph((XWPFParagraph)element, content, text);
                        break;

                    case BodyElementType.TABLE:
                        AppendTable((XWPFTable)element, content, text);
                        break;

                    default:
                        // 其它 body 元素（如 SDT 内容控件包装块）直接跳过，不影响正文完整性。
                        break;
                }
            }

            content.Text = text.ToString().TrimEnd();
            return content;
        }

        // ───────────────────────────── 内部实现 ─────────────────────────────

        private static void AppendParagraph(XWPFParagraph paragraph, DocumentContent content, StringBuilder text)
        {
            var value = Normalize(paragraph.Text);
            if (value.Length == 0) return;

            var (isHeading, level) = ClassifyHeading(paragraph, value);
            content.Paragraphs.Add(new DocumentParagraph
            {
                Text = value,
                IsHeading = isHeading,
                HeadingLevel = level,
            });
            text.Append(value).Append('\n');
        }

        private static void AppendTable(XWPFTable table, DocumentContent content, StringBuilder text)
        {
            var parsed = new DocumentTable();
            for (int r = 0; r < table.NumberOfRows; r++)
            {
                var row = table.GetRow(r);
                var cells = new List<string>();
                if (row != null)
                {
                    // 一次取出再遍历：原写法在循环条件与循环体里各调一次
                    // GetTableICells()（每次都新建 List），既浪费又易读性差。
                    var iCells = row.GetTableICells();
                    foreach (var ic in iCells)
                        cells.Add(Normalize(CellText((IBody)ic)));
                }
                // 整行全空则不入表（Word 表格尾部常见空行）
                if (cells.Any(v => v.Length > 0)) parsed.Rows.Add(cells);
            }

            if (parsed.Rows.Count == 0) return;

            content.Tables.Add(parsed);
            // 表格以制表符分隔并入纯文本，保持"能一眼看懂谁跟谁一列"。
            foreach (var row in parsed.Rows)
                text.Append(string.Join('\t', row)).Append('\n');
            text.Append('\n');
        }

        /// <summary>
        /// 单元格文本：递归取（单元格内可嵌段落与嵌套表格），多段落用空格连接。
        ///
        /// ⚠️ 形参用 <see cref="IBody"/>，调用处需**显式转型**，原因如下（NPOI 2.7.1）：
        ///
        /// * `XWPFTableRow.GetTableICells()` 的静态返回类型是 `List&lt;ICell&gt;`；
        /// * 而 `ICell` 是个**完全空的标记接口**（0 个方法、0 个属性，**不继承 IBody**）；
        /// * 真正的成员（`Paragraphs` / `Tables`）在 `IBody` 上；
        /// * 运行时对象其实是 `XWPFTableCell`，它**同时实现了** `IBody` 与 `ICell`
        ///   （实测 `as IBody` 与 `as XWPFTableCell` 都为 true）。
        ///
        /// 所以静态类型 `ICell` 无法直接传给任何一个能用的形参 ——
        /// 必须先转成 `IBody`（拿成员）或 `XWPFTableCell`。
        /// 这里选 `IBody`：它是**接口**，语义上只要求"能取段落与表格"，
        /// 不绑死具体实现类（将来 NPOI 换实现也不会断）。
        /// </summary>
        private static string CellText(IBody cell)
        {
            if (cell == null) return "";
            var parts = new List<string>();
            foreach (var p in cell.Paragraphs)
            {
                var t = Normalize(p.Text);
                if (t.Length > 0) parts.Add(t);
            }
            foreach (var nested in cell.Tables)
            {
                for (int r = 0; r < nested.NumberOfRows; r++)
                {
                    var row = nested.GetRow(r);
                    if (row == null) continue;
                    var cells = row.GetTableICells()
                        .Select(c => CellText((IBody)c))
                        .Where(v => v.Length > 0)
                        .ToList();
                    if (cells.Count > 0) parts.Add(string.Join(' ', cells));
                }
            }
            return string.Join(' ', parts);
        }

        /// <summary>
        /// 标题判定：优先 Word 内置 Heading 样式（Heading1..9 / 标题1..9），
        /// 其次按中文剧本常见的"第N集/第N章"文本特征兜底。
        /// </summary>
        private static (bool isHeading, int level) ClassifyHeading(XWPFParagraph paragraph, string value)
        {
            var style = paragraph.StyleID ?? "";
            if (style.Length > 0)
            {
                var m = Regex.Match(style, @"^(?:Heading|heading|标题)\s*([1-9])$");
                if (m.Success)
                    return (true, int.Parse(m.Groups[1].Value));
                if (style.Equals("Title", StringComparison.OrdinalIgnoreCase) ||
                    style.Equals("标题", StringComparison.Ordinal))
                    return (true, 1);
            }

            if (value.Length <= 40 && HeadingPattern.IsMatch(value))
                return (true, 1);

            return (false, 0);
        }

        /// <summary>纯文本读取（.txt/.md）：UTF-8 优先，带 BOM 自动识别。</summary>
        private static DocumentContent ReadPlainText(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var raw = reader.ReadToEnd();

            var content = new DocumentContent();
            var text = new StringBuilder();
            foreach (var line in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var value = Normalize(line);
                if (value.Length == 0) continue;
                var (isHeading, level) = HeadingPattern.IsMatch(value) && value.Length <= 40
                    ? (true, 1)
                    : (false, 0);
                content.Paragraphs.Add(new DocumentParagraph { Text = value, IsHeading = isHeading, HeadingLevel = level });
                text.Append(value).Append('\n');
            }
            content.Text = text.ToString().TrimEnd();
            return content;
        }

        private static bool LooksLikeZip(Stream stream)
        {
            var pos = stream.Position;
            Span<byte> head = stackalloc byte[4];
            var read = stream.Read(head);
            stream.Position = pos;
            // ZIP local file header: 50 4B 03 04；空归档为 50 4B 05 06
            return read >= 4
                && head[0] == 0x50 && head[1] == 0x4B
                && ((head[2] == 0x03 && head[3] == 0x04) || (head[2] == 0x05 && head[3] == 0x06));
        }

        private static MemoryStream ToSeekable(Stream stream)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }

        /// <summary>统一空白：全角空格/NBSP 归一为普通空格，压缩连续空白并 Trim（Word 里全是零宽字符与软换行）。</summary>
        private static string Normalize(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                sb.Append(ch switch
                {
                    '\u00A0' => ' ',   // NBSP
                    '\u3000' => ' ',   // 全角空格
                    '\u200B' => ' ',   // 零宽空格
                    '\uFEFF' => ' ',   // BOM 混入正文
                    '\t' => ' ',
                    _ => ch,
                });
            }
            return Regex.Replace(sb.ToString(), @"[ \t]{2,}", " ").Trim();
        }
    }
}
