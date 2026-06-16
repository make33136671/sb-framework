namespace SB.Framework.Services
{
    /// <summary>一个工作表的文本矩阵(单元格统一转字符串)。</summary>
    public sealed class SpreadsheetSheet
    {
        public string Name { get; set; } = "";
        /// <summary>行 → 列单元格文本。空单元格为 ""。</summary>
        public List<List<string>> Rows { get; set; } = new();
    }

    /// <summary>
    /// 通用电子表格读取能力(xlsx / xls / csv)。框架层抽象,业务层注入即用——
    /// 把表格文件读成"行列文本矩阵",由业务自行按列提取需要的数据(手机号/邮箱/姓名等)。
    /// 单元格统一转字符串,数值整数自动去科学计数法(避免手机号被 Excel 当数字后失真)。
    /// </summary>
    public interface ISpreadsheetReader
    {
        /// <summary>
        /// 读取电子表格全部工作表为文本矩阵。
        /// <paramref name="fileName"/> 用于按扩展名区分 csv / excel(可空,默认按 excel 二进制识别 xls/xlsx)。
        /// </summary>
        IReadOnlyList<SpreadsheetSheet> Read(Stream stream, string? fileName = null);

        /// <summary>读取首个工作表的全部非空单元格,按行优先拍平为一维列表(提取"任意列数据"的便捷入口)。</summary>
        IReadOnlyList<string> ReadCells(Stream stream, string? fileName = null);
    }
}
