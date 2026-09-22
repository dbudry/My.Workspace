namespace My.Shared.Rules;

/// <summary>
/// Maps modern expense lines onto Form 87-43 columns (cleaned layout, same buckets).
/// Software / subscriptions → Miscellaneous Z. Telephone category → Telephone amount.
/// Personal-car miles stay in the mileage column; dollar amount is miles × rate on the total row.
/// </summary>
public static class ExpenseForm8743Rules
{
    public const string FormNumber = "FORM 87-43";
    public const string DefaultPlant = "Home Office";
    public const string MiscSoftwareCode = "Z";

    public static ExpenseForm8743Line MapLine(
        DateTime date,
        string description,
        string category,
        decimal amount,
        decimal? miles,
        string? transportationCode,
        string? miscellaneousCode,
        bool mealBreakfast,
        bool mealLunch,
        bool mealDinner)
    {
        var (storedCategory, transport) = ExpenseCategoryRules.ForStorage(category, transportationCode);
        var line = new ExpenseForm8743Line
        {
            Date = date.Date,
            Description = description.Trim()
        };

        if (ExpenseCategoryRules.IsPersonalCar(storedCategory, transport))
        {
            line.MileageMiles = miles;
            return line;
        }

        if (string.Equals(storedCategory, ExpenseCategoryRules.Transportation, StringComparison.Ordinal))
        {
            line.TransportCode = transport;
            line.TransportAmount = amount;
            return line;
        }

        if (string.Equals(storedCategory, ExpenseCategoryRules.Hotel, StringComparison.Ordinal))
        {
            line.HotelAmount = amount;
            return line;
        }

        if (string.Equals(storedCategory, ExpenseCategoryRules.Meals, StringComparison.Ordinal))
        {
            line.MealBreakfast = mealBreakfast;
            line.MealLunch = mealLunch;
            line.MealDinner = mealDinner;
            line.MealsAmount = amount;
            return line;
        }

        if (string.Equals(storedCategory, ExpenseCategoryRules.Entertainment, StringComparison.Ordinal))
        {
            line.EntertainmentAmount = amount;
            return line;
        }

        if (string.Equals(storedCategory, ExpenseCategoryRules.Telephone, StringComparison.Ordinal))
        {
            line.TelephoneAmount = amount;
            return line;
        }

        if (string.Equals(storedCategory, ExpenseCategoryRules.Software, StringComparison.Ordinal))
        {
            line.MiscCode = MiscSoftwareCode;
            line.MiscAmount = amount;
            return line;
        }

        line.MiscCode = ExpenseCategoryRules.NormalizeCode(miscellaneousCode) ?? MiscSoftwareCode;
        line.MiscAmount = amount;
        return line;
    }

    public static ExpenseForm8743Totals Totals(IReadOnlyList<ExpenseForm8743Line> lines, decimal mileageRate)
    {
        var miles = lines.Sum(l => l.MileageMiles ?? 0m);
        var mileageDollars = decimal.Round(miles * mileageRate, 2, MidpointRounding.AwayFromZero);
        var transport = lines.Sum(l => l.TransportAmount ?? 0m);
        var hotel = lines.Sum(l => l.HotelAmount ?? 0m);
        var meals = lines.Sum(l => l.MealsAmount ?? 0m);
        var entertainment = lines.Sum(l => l.EntertainmentAmount ?? 0m);
        var telephone = lines.Sum(l => l.TelephoneAmount ?? 0m);
        var misc = lines.Sum(l => l.MiscAmount ?? 0m);
        return new ExpenseForm8743Totals
        {
            MileageMiles = miles,
            MileageAmount = mileageDollars,
            TransportAmount = transport,
            HotelAmount = hotel,
            MealsAmount = meals,
            EntertainmentAmount = entertainment,
            TelephoneAmount = telephone,
            MiscAmount = misc,
            GrandTotal = mileageDollars + transport + hotel + meals + entertainment + telephone + misc
        };
    }
}

public sealed class ExpenseForm8743Line
{
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public decimal? MileageMiles { get; set; }
    public string? TransportCode { get; set; }
    public decimal? TransportAmount { get; set; }
    public decimal? HotelAmount { get; set; }
    public bool MealBreakfast { get; set; }
    public bool MealLunch { get; set; }
    public bool MealDinner { get; set; }
    public decimal? MealsAmount { get; set; }
    public decimal? EntertainmentAmount { get; set; }
    public decimal? TelephoneAmount { get; set; }
    public string? MiscCode { get; set; }
    public decimal? MiscAmount { get; set; }
}

public sealed class ExpenseForm8743Totals
{
    public decimal MileageMiles { get; set; }
    public decimal MileageAmount { get; set; }
    public decimal TransportAmount { get; set; }
    public decimal HotelAmount { get; set; }
    public decimal MealsAmount { get; set; }
    public decimal EntertainmentAmount { get; set; }
    public decimal TelephoneAmount { get; set; }
    public decimal MiscAmount { get; set; }
    public decimal GrandTotal { get; set; }
}

public sealed class ExpenseForm8743Model
{
    public string CompanyName { get; set; } = "Home Company";
    public string EmployeeName { get; set; } = "";
    public string PlantOrLocation { get; set; } = ExpenseForm8743Rules.DefaultPlant;
    public string ChargeTo { get; set; } = "";
    public string? Address { get; set; }
    public DateTime CoverStart { get; set; }
    public DateTime CoverEnd { get; set; }
    public DateTime ReportDate { get; set; }
    public decimal MileageRate { get; set; }
    public string? Purpose { get; set; }
    public List<ExpenseForm8743Line> Lines { get; set; } = [];
    public byte[]? EmployeeSignature { get; set; }
    public byte[]? ManagerSignature { get; set; }
    public string FileName { get; set; } = "Expenses.pdf";
}
