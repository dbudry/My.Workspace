using ClosedXML.Excel;
using My.Functions.Services;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseForm8743ExcelFillerTests
{
    [Fact]
    public void Fill_writes_header_and_software_line()
    {
        var template = File.ReadAllBytes(FindTemplate());
        var bytes = ExpenseForm8743ExcelFiller.Fill(template, new ExpenseForm8743Model
        {
            EmployeeName = "Derek Budry",
            ChargeTo = "Home Company",
            CoverStart = new DateTime(2026, 8, 1),
            CoverEnd = new DateTime(2026, 8, 31),
            ReportDate = new DateTime(2026, 8, 10),
            MileageRate = 0.555m,
            Lines =
            [
                ExpenseForm8743Rules.MapLine(
                    new DateTime(2026, 8, 10), "GitHub", ExpenseCategoryRules.Software,
                    24m, null, null, null, false, false, false)
            ]
        });

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        Assert.Equal("Derek Budry", sheet.Cell("B4").GetString());
        Assert.Equal(new DateTime(2026, 8, 10), sheet.Cell("A10").GetDateTime().Date);
        Assert.Equal("GitHub", sheet.Cell("B10").GetString());
        Assert.Equal("Z", sheet.Cell("M10").GetString());
        Assert.Equal(24m, sheet.Cell("N10").GetValue<decimal>());
    }

    [Fact]
    public void Fill_places_signature_pictures()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var template = File.ReadAllBytes(FindTemplate());
        var bytes = ExpenseForm8743ExcelFiller.Fill(template, new ExpenseForm8743Model
        {
            EmployeeName = "Derek Budry",
            EmployeeSignature = png,
            ManagerSignature = png,
            CoverStart = new DateTime(2026, 8, 1),
            CoverEnd = new DateTime(2026, 8, 31),
            ReportDate = new DateTime(2026, 8, 10),
            MileageRate = 0.555m
        });

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(2, workbook.Worksheet(1).Pictures.Count());
    }

    private static string FindTemplate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "My.AzureFunction", "Templates",
                ExpenseForm8743ExcelLayout.TemplateFileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Form 87-43 template not found.");
    }
}
