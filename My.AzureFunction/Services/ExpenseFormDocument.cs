using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using My.Shared.Rules;

namespace My.Functions.Services;

public static class ExpenseFormDocument
{
    public static byte[] Generate(ExpenseForm8743Model model) => BuildDocument(model).GeneratePdf();

    internal static IDocument BuildDocument(ExpenseForm8743Model model)
    {
        var totals = ExpenseForm8743Rules.Totals(model.Lines, model.MileageRate);
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter.Landscape());
                page.Margin(22);
                page.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken4));
                page.Header().Element(c => DrawHeader(c, model));
                page.Content().Element(c => DrawBody(c, model, totals));
                page.Footer().DefaultTextStyle(x => x.FontSize(7).FontColor(Colors.Grey.Darken1)).Row(row =>
                {
                    row.RelativeItem().Text(ExpenseForm8743Rules.FormNumber);
                    row.ConstantItem(80).AlignRight().Text(text =>
                    {
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                    });
                });
            });
        });
    }

    private static void DrawHeader(IContainer container, ExpenseForm8743Model model)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Text("TRAVEL EXPENSE STATEMENT").SemiBold().FontSize(13);
                row.RelativeItem().AlignRight().Text(model.CompanyName).SemiBold().FontSize(13);
            });
            col.Item().PaddingTop(8).Border(0.6f).BorderColor(Colors.Grey.Darken1).Padding(8).Column(meta =>
            {
                meta.Spacing(4);
                meta.Item().Row(r =>
                {
                    r.RelativeItem(2).Element(c => Labeled(c, "Name", model.EmployeeName));
                    r.RelativeItem(2).Element(c => Labeled(c, "Plant or location",
                        string.IsNullOrWhiteSpace(model.PlantOrLocation)
                            ? ExpenseForm8743Rules.DefaultPlant
                            : model.PlantOrLocation));
                    r.RelativeItem(2).Element(c => Labeled(c, "Charge to", model.ChargeTo));
                });
                meta.Item().Row(r =>
                {
                    r.RelativeItem(2).Element(c => Labeled(c, "Address", model.Address ?? ""));
                    r.RelativeItem(2).Element(c => Labeled(c, "Covers period",
                        $"{model.CoverStart:MM/dd/yyyy}  –  {model.CoverEnd:MM/dd/yyyy}"));
                    r.RelativeItem(2).Element(c => Labeled(c, "Report date", model.ReportDate.ToString("MM/dd/yyyy")));
                });
            });
        });
    }

    private static void DrawBody(IContainer container, ExpenseForm8743Model model, ExpenseForm8743Totals totals)
    {
        container.PaddingTop(10).Column(col =>
        {
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(58);   // Date
                    columns.RelativeColumn(2.2f);  // Description
                    columns.ConstantColumn(40);   // Miles
                    columns.ConstantColumn(38);   // T. code
                    columns.ConstantColumn(52);   // Transport $
                    columns.ConstantColumn(46);   // Hotel
                    columns.ConstantColumn(18);   // B
                    columns.ConstantColumn(18);   // L
                    columns.ConstantColumn(18);   // D
                    columns.ConstantColumn(46);   // Meals $
                    columns.ConstantColumn(50);   // Entertain.
                    columns.ConstantColumn(50);   // Telephone
                    columns.ConstantColumn(38);   // M. code
                    columns.ConstantColumn(52);
                });

                table.Header(header =>
                {
                    header.Cell().Element(c => HeaderCell(c, "Date"));
                    header.Cell().Element(c => HeaderCell(c, "Description"));
                    header.Cell().Element(c => HeaderCell(c, "Miles"));
                    header.Cell().Element(c => HeaderCell(c, "T. code"));
                    header.Cell().Element(c => HeaderCell(c, "Transport $"));
                    header.Cell().Element(c => HeaderCell(c, "Hotel"));
                    header.Cell().Element(c => HeaderCell(c, "B"));
                    header.Cell().Element(c => HeaderCell(c, "L"));
                    header.Cell().Element(c => HeaderCell(c, "D"));
                    header.Cell().Element(c => HeaderCell(c, "Meals $"));
                    header.Cell().Element(c => HeaderCell(c, "Entertain."));
                    header.Cell().Element(c => HeaderCell(c, "Telephone"));
                    header.Cell().Element(c => HeaderCell(c, "M. code"));
                    header.Cell().Element(c => HeaderCell(c, "Misc $"));
                });

                foreach (var line in model.Lines)
                {
                    Cell(table, line.Date.ToString("MM/dd/yyyy"));
                    Cell(table, line.Description, alignLeft: true);
                    Cell(table, MoneyOrNumber(line.MileageMiles));
                    Cell(table, line.TransportCode ?? "");
                    Cell(table, Money(line.TransportAmount));
                    Cell(table, Money(line.HotelAmount));
                    Cell(table, Flag(line.MealBreakfast));
                    Cell(table, Flag(line.MealLunch));
                    Cell(table, Flag(line.MealDinner));
                    Cell(table, Money(line.MealsAmount));
                    Cell(table, Money(line.EntertainmentAmount));
                    Cell(table, Money(line.TelephoneAmount));
                    Cell(table, line.MiscCode ?? "");
                    Cell(table, Money(line.MiscAmount));
                }

                TotalCell(table, "Totals", 2);
                TotalCell(table, MoneyOrNumber(totals.MileageMiles));
                TotalCell(table, "");
                TotalCell(table, Money(totals.TransportAmount));
                TotalCell(table, Money(totals.HotelAmount));
                TotalCell(table, "");
                TotalCell(table, "");
                TotalCell(table, "");
                TotalCell(table, Money(totals.MealsAmount));
                TotalCell(table, Money(totals.EntertainmentAmount));
                TotalCell(table, Money(totals.TelephoneAmount));
                TotalCell(table, "");
                TotalCell(table, Money(totals.MiscAmount));
            });

            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Text($"Personal-car mileage @ {model.MileageRate:0.###} / mile  =  {totals.MileageAmount:C}")
                    .FontSize(8);
                row.RelativeItem().AlignRight().Text($"Reimbursement  {totals.GrandTotal:C}").SemiBold().FontSize(11);
            });

            col.Item().PaddingTop(6).Text(
                    "Transport codes: M personal car · A rented car · B plane · C local fares · F tolls · P parking · R rail · T airport tax · W gas · X excess weight · Y other.   Misc codes: G tips · H postage · L laundry · V valet · Z other (software / subscriptions use Z).")
                .FontSize(7).FontColor(Colors.Grey.Darken1);

            col.Item().PaddingTop(14).Row(row =>
            {
                row.RelativeItem().Element(c => SignatureBox(c, "Employee's signature", model.EmployeeSignature));
                row.ConstantItem(24);
                row.RelativeItem().Element(c => SignatureBox(c, "Authorized approval", model.ManagerSignature));
            });
        });
    }

    private static void SignatureBox(IContainer container, string label, byte[]? image)
    {
        container.Column(col =>
        {
            col.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken2);
            col.Item().Height(52).BorderBottom(0.8f).BorderColor(Colors.Grey.Darken2).AlignBottom().AlignLeft()
                .Element(inner =>
                {
                    if (image is { Length: > 0 })
                        inner.Height(48).Image(image).FitHeight();
                    else
                        inner.Height(48);
                });
        });
    }

    private static void Labeled(IContainer container, string label, string value)
    {
        container.Column(c =>
        {
            c.Item().Text(label).FontSize(7).FontColor(Colors.Grey.Darken1);
            c.Item().Text(string.IsNullOrWhiteSpace(value) ? "—" : value).FontSize(9).SemiBold();
        });
    }

    private static void HeaderCell(IContainer container, string text) =>
        container.Background(Colors.Grey.Lighten3).Border(0.4f).BorderColor(Colors.Grey.Medium)
            .Padding(3).AlignCenter().Text(text).SemiBold().FontSize(7);

    private static void Cell(TableDescriptor table, string text, bool alignLeft = false)
    {
        var cell = table.Cell().Border(0.4f).BorderColor(Colors.Grey.Lighten1).Padding(3);
        if (alignLeft)
            cell.AlignLeft().Text(text).FontSize(8);
        else
            cell.AlignCenter().Text(text).FontSize(8);
    }

    private static void TotalCell(TableDescriptor table, string text, int extraColumns = 0)
    {
        var cell = extraColumns > 0
            ? table.Cell().ColumnSpan((uint)extraColumns)
            : table.Cell();
        cell.Background(Colors.Grey.Lighten4).Border(0.4f).BorderColor(Colors.Grey.Medium)
            .Padding(3).AlignCenter().Text(text).SemiBold().FontSize(8);
    }

    private static string Money(decimal? value) =>
        value is null ? "" : value.Value.ToString("0.00");

    private static string MoneyOrNumber(decimal? value) =>
        value is null ? "" : value.Value.ToString("0.###");

    private static string Flag(bool value) => value ? "X" : "";
}
