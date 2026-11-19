namespace medxbackend.Models;

/// <summary>结果摘要，表示已发送到打印机的标签页数。</summary>
public sealed record PrintCode128PdfResponse(int PagesPrinted);