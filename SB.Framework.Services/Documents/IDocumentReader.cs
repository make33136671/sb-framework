namespace SB.Framework.Services
{
    /// <summary>解析出的文档段落（保留"标题/正文"这一层结构，供业务按标题切分集/场）。</summary>
    public sealed class DocumentParagraph
    {
        /// <summary>段落文本（已 Trim；空段落不产出）。</summary>
        public string Text { get; set; } = "";

        /// <summary>是否为标题段落（Word 内置 Heading 1-9 样式，或"第N集/第N章"这类常见剧本标题）。</summary>
        public bool IsHeading { get; set; }

        /// <summary>标题级别 1-9；非标题为 0。</summary>
        public int HeadingLevel { get; set; }
    }

    /// <summary>解析出的文档表格（Word 里的表格，例如角色表/分集表）。</summary>
    public sealed class DocumentTable
    {
        /// <summary>行 → 列单元格文本。空单元格为 ""。</summary>
        public List<List<string>> Rows { get; set; } = new();
    }

    /// <summary>一份文档的解析结果：纯文本 + 结构化段落 + 表格。</summary>
    public sealed class DocumentContent
    {
        /// <summary>全文纯文本（段落按序换行拼接；表格以制表符分隔追加在后）。</summary>
        public string Text { get; set; } = "";

        /// <summary>段落序列（保留标题层级；供按标题切分集/场）。</summary>
        public List<DocumentParagraph> Paragraphs { get; set; } = new();

        /// <summary>文档内的表格。</summary>
        public List<DocumentTable> Tables { get; set; } = new();

        /// <summary>字符数（校验/展示用）。</summary>
        public int Length => Text.Length;

        /// <summary>是否没解析出任何正文（扫描件/图片型 docx 会出现这种情况）。</summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
    }

    /// <summary>
    /// 通用办公文档读取能力（Word docx 为主）。
    /// 框架层抽象，业务层注入即用——把 .docx 读成"纯文本 + 段落(带标题层级) + 表格"，
    /// 业务据此落库（如剧本原始稿）或做结构切分。
    ///
    /// 支持的格式：.docx（Office Open XML，含 .docm）。
    /// 不支持：.doc（Word 97-2003 二进制格式）——NPOI 的 HWPF 未随包发布；
    ///         此类文件请先另存为 .docx。.txt / .md 走框架内置纯文本读取（无需外部库）。
    /// </summary>
    public interface IDocumentReader
    {
        /// <summary>该文件名是否为本读取器支持的格式（大小写不敏感；fileName 可为空）。</summary>
        bool CanRead(string? fileName);

        /// <summary>
        /// 读取文档内容。
        /// <paramref name="fileName"/> 用于按扩展名分派（可为空，此时按 docx 二进制尝试）。
        /// </summary>
        DocumentContent Read(Stream stream, string? fileName = null);
    }
}
