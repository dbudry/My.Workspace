namespace My.Shared.Rules;

/// <summary>
/// Cell map for the legacy Form 87-43 workbook (sheet "US Currency", rows 10–17).
/// </summary>
public static class ExpenseForm8743ExcelLayout
{
    public const string SheetName = "US Currency";
    public const string TemplateFileName = "Form_87-43.xlsx";
    public const int FirstLineRow = 10;
    public const int LastLineRow = 17;
    public const int LineCapacity = LastLineRow - FirstLineRow + 1;

    public const string CompanyCell = "J1";
    public const string NameCell = "B4";
    public const string PlantCell = "D4";
    public const string ChargeToCell = "K4";
    public const string AddressLine1Cell = "B5";
    public const string AddressLine2Cell = "B6";
    public const string CoverStartCell = "J6";
    public const string CoverEndCell = "L6";
    public const string ReportDateCell = "N6";
    public const string MileageRateCell = "M18";
    public const string PurposeCell = "B35";
    public const string EmployeeSignatureCell = "B42";
    public const string ApprovalSignatureCell = "D42";
    public const int SignatureWidthPx = 220;
    public const int SignatureHeightPx = 48;

    public static IReadOnlyList<IReadOnlyList<T>> ChunkLines<T>(IReadOnlyList<T> lines)
    {
        if (lines.Count == 0)
            return [[]];

        var pages = new List<IReadOnlyList<T>>();
        for (var i = 0; i < lines.Count; i += LineCapacity)
        {
            var take = Math.Min(LineCapacity, lines.Count - i);
            var page = new List<T>(take);
            for (var j = 0; j < take; j++)
                page.Add(lines[i + j]);
            pages.Add(page);
        }

        return pages;
    }
}
