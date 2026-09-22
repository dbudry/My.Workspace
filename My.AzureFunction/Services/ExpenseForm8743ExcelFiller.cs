using ClosedXML.Excel;
using My.Shared.Rules;

namespace My.Functions.Services;

public static class ExpenseForm8743ExcelFiller
{
    public static byte[] Fill(byte[] template, ExpenseForm8743Model model)
    {
        using var input = new MemoryStream(template);
        using var workbook = new XLWorkbook(input);
        var pages = ExpenseForm8743ExcelLayout.ChunkLines(model.Lines);
        var templateSheet = workbook.Worksheets.First();

        for (var i = 1; i < pages.Count; i++)
            templateSheet.CopyTo($"{ExpenseForm8743ExcelLayout.SheetName} {i + 1}");

        var pictureStreams = new List<MemoryStream>();
        try
        {
            for (var i = 0; i < pages.Count; i++)
            {
                var sheet = workbook.Worksheets.ElementAt(i);
                FillHeader(sheet, model);
                FillLines(sheet, pages[i]);
                PlaceSignature(sheet, model.EmployeeSignature, ExpenseForm8743ExcelLayout.EmployeeSignatureCell,
                    pictureStreams);
                PlaceSignature(sheet, model.ManagerSignature, ExpenseForm8743ExcelLayout.ApprovalSignatureCell,
                    pictureStreams);
            }

            using var output = new MemoryStream();
            workbook.SaveAs(output);
            return output.ToArray();
        }
        finally
        {
            foreach (var stream in pictureStreams)
                stream.Dispose();
        }
    }

    private static void FillHeader(IXLWorksheet sheet, ExpenseForm8743Model model)
    {
        sheet.Cell(ExpenseForm8743ExcelLayout.CompanyCell).Value = model.CompanyName;
        sheet.Cell(ExpenseForm8743ExcelLayout.NameCell).Value = model.EmployeeName;
        sheet.Cell(ExpenseForm8743ExcelLayout.PlantCell).Value = string.IsNullOrWhiteSpace(model.PlantOrLocation)
            ? ExpenseForm8743Rules.DefaultPlant
            : model.PlantOrLocation;
        sheet.Cell(ExpenseForm8743ExcelLayout.ChargeToCell).Value = model.ChargeTo;
        var (line1, line2) = SplitAddress(model.Address);
        sheet.Cell(ExpenseForm8743ExcelLayout.AddressLine1Cell).Value = line1;
        sheet.Cell(ExpenseForm8743ExcelLayout.AddressLine2Cell).Value = line2;
        sheet.Cell(ExpenseForm8743ExcelLayout.CoverStartCell).Value = model.CoverStart.Date;
        sheet.Cell(ExpenseForm8743ExcelLayout.CoverEndCell).Value = model.CoverEnd.Date;
        sheet.Cell(ExpenseForm8743ExcelLayout.ReportDateCell).Value = model.ReportDate.Date;
        sheet.Cell(ExpenseForm8743ExcelLayout.MileageRateCell).Value = model.MileageRate;
        if (!string.IsNullOrWhiteSpace(model.Purpose))
            sheet.Cell(ExpenseForm8743ExcelLayout.PurposeCell).Value = model.Purpose;
    }

    private static void FillLines(IXLWorksheet sheet, IReadOnlyList<ExpenseForm8743Line> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var row = ExpenseForm8743ExcelLayout.FirstLineRow + i;
            var line = lines[i];
            sheet.Cell(row, 1).Value = line.Date.Date;
            sheet.Cell(row, 2).Value = line.Description;
            if (line.MileageMiles is > 0)
                sheet.Cell(row, 3).Value = line.MileageMiles.Value;
            if (!string.IsNullOrWhiteSpace(line.TransportCode))
                sheet.Cell(row, 4).Value = line.TransportCode;
            if (line.TransportAmount is > 0)
                sheet.Cell(row, 5).Value = line.TransportAmount.Value;
            if (line.HotelAmount is > 0)
                sheet.Cell(row, 6).Value = line.HotelAmount.Value;
            if (line.MealBreakfast)
                sheet.Cell(row, 7).Value = "X";
            if (line.MealLunch)
                sheet.Cell(row, 8).Value = "X";
            if (line.MealDinner)
                sheet.Cell(row, 9).Value = "X";
            if (line.MealsAmount is > 0)
                sheet.Cell(row, 10).Value = line.MealsAmount.Value;
            if (line.EntertainmentAmount is > 0)
                sheet.Cell(row, 11).Value = line.EntertainmentAmount.Value;
            if (line.TelephoneAmount is > 0)
                sheet.Cell(row, 12).Value = line.TelephoneAmount.Value;
            if (!string.IsNullOrWhiteSpace(line.MiscCode))
                sheet.Cell(row, 13).Value = line.MiscCode;
            if (line.MiscAmount is > 0)
                sheet.Cell(row, 14).Value = line.MiscAmount.Value;
        }
    }

    private static void PlaceSignature(
        IXLWorksheet sheet,
        byte[]? image,
        string cell,
        List<MemoryStream> hold)
    {
        if (image is not { Length: > 0 })
            return;

        var stream = new MemoryStream(image);
        hold.Add(stream);
        var picture = sheet.AddPicture(stream);
        picture.MoveTo(sheet.Cell(cell), 6, 2);
        picture.WithSize(
            ExpenseForm8743ExcelLayout.SignatureWidthPx,
            ExpenseForm8743ExcelLayout.SignatureHeightPx);
    }

    private static (string Line1, string Line2) SplitAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return ("", "");
        var parts = address.Replace("\r\n", "\n").Split('\n', 2, StringSplitOptions.None);
        if (parts.Length == 1)
            return (parts[0].Trim(), "");
        return (parts[0].Trim(), parts[1].Trim());
    }
}
