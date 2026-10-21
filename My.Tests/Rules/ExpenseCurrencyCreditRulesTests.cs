using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseCurrencyCreditRulesTests
{
    [Fact]
    public void Usd_rate_is_always_one_and_eur_color_uses_dark_ink()
    {
        Assert.Equal(1m, ExpenseCurrencyRules.NormalizeRate("usd", 1.25m));
        Assert.Equal("#F9A825", ExpenseCurrencyRules.NormalizeColor("f9a825", ExpenseCurrencyRules.DefaultUsdColor));
        Assert.Equal("#1A1A1A", ExpenseCurrencyRules.Ink(ExpenseCurrencyRules.DefaultEurColor));
        Assert.Equal("#FFFFFF", ExpenseCurrencyRules.Ink(ExpenseCurrencyRules.DefaultUsdColor));
        Assert.Equal(ExpenseCurrencyRules.EditorForm, ExpenseCurrencyRules.NormalizeView("form"));
        Assert.Equal(ExpenseCurrencyRules.LayoutGrouped, ExpenseCurrencyRules.NormalizeLayout("GROUPED"));
        Assert.False(ExpenseCurrencyRules.IsCodeShape("US"));
        Assert.True(ExpenseCurrencyRules.IsCodeShape("EUR"));
    }

    [Fact]
    public void Saved_rate_stays_until_the_currency_changes()
    {
        var kept = ExpenseMoneyRules.Apply(
            ExpenseCategoryRules.Hotel, null, null, 0.555m,
            spentAmount: 100m, currencyCode: "EUR", catalogRate: 1.5m,
            savedRate: 1.2m, savedCurrency: "EUR");
        Assert.Equal("EUR", kept.CurrencyCode);
        Assert.Equal(1.2m, kept.ExchangeRateToUsd);
        Assert.Equal(100m, kept.SpentAmount);
        Assert.Equal(120m, kept.AmountUsd);

        var changed = ExpenseMoneyRules.Apply(
            ExpenseCategoryRules.Hotel, null, null, 0.555m,
            spentAmount: 100m, currencyCode: "GBP", catalogRate: 1.3m,
            savedRate: 1.2m, savedCurrency: "EUR");
        Assert.Equal("GBP", changed.CurrencyCode);
        Assert.Equal(1.3m, changed.ExchangeRateToUsd);
        Assert.Equal(130m, changed.AmountUsd);
    }

    [Fact]
    public void Entered_amount_stays_in_its_currency()
    {
        Assert.Equal(4m, ExpenseMoneyRules.EnteredAmount(4m, 4.54m, "EUR"));
        Assert.Equal(2m, ExpenseMoneyRules.EnteredAmount(0m, 2m, "USD"));
        Assert.Equal(4m, ExpenseMoneyRules.EnteredAmount(4m, 4.54m, "USD"));
    }

    [Fact]
    public void Personal_car_stays_usd_miles_times_rate()
    {
        var money = ExpenseMoneyRules.Apply(
            ExpenseCategoryRules.Transportation, "M", miles: 20m, mileageRatePerMile: 0.555m,
            spentAmount: 80m, currencyCode: "EUR", catalogRate: 1.2m,
            savedRate: 1.2m, savedCurrency: "EUR");
        Assert.Equal("USD", money.CurrencyCode);
        Assert.Equal(1m, money.ExchangeRateToUsd);
        Assert.Equal(11.10m, money.SpentAmount);
        Assert.Equal(11.10m, money.AmountUsd);
    }

    [Fact]
    public void Credit_reduces_an_unpaid_expense_with_the_same_charge_to()
    {
        var sources = new[]
        {
            new ExpenseCreditSource
            {
                LineId = "ret",
                ReportId = "paid",
                ChargeToOrganizationId = "profit-point",
                ReturnDate = new DateTime(2026, 10, 21),
                AmountUsd = 1m
            }
        };
        var lines = new[]
        {
            Line("ret", "paid", "profit-point", ExpenseStatusRules.Reimbursed, new DateTime(2026, 8, 10), 1m, 0, returned: true),
            Line("phone", "draft", "profit-point", ExpenseStatusRules.Draft, new DateTime(2026, 9, 1), 284m, 0, returned: false),
            Line("other", "other", "other-org", ExpenseStatusRules.Draft, new DateTime(2026, 9, 1), 284m, 0, returned: false),
            Line("paid-extra", "paid", "profit-point", ExpenseStatusRules.Reimbursed, new DateTime(2026, 8, 11), 50m, 1, returned: false)
        };

        var applied = ExpenseCreditRules.AppliedByLine(sources, lines);
        Assert.Equal(1m, applied["phone"]);
        Assert.False(ExpenseCreditRules.HasBeenUsed("ret", sources, lines));
        Assert.False(applied.ContainsKey("other"));
        Assert.False(applied.ContainsKey("paid-extra"));
        Assert.False(applied.ContainsKey("ret"));
        Assert.Equal(283m, ExpenseReportMoneyRules.NetForReport("draft", sources, lines, creditsEnabled: true));
        Assert.Equal(284m, ExpenseReportMoneyRules.NetForReport("other", sources, lines, creditsEnabled: true));
        Assert.Equal(51m, ExpenseReportMoneyRules.NetForReport("paid", sources, lines, creditsEnabled: true));
        Assert.False(ExpenseCreditRules.HasBeenUsed("ret", sources, lines));
    }

    [Fact]
    public void Submitted_expense_locks_the_credit()
    {
        var sources = new[]
        {
            new ExpenseCreditSource
            {
                LineId = "ret",
                ReportId = "paid",
                ChargeToOrganizationId = "profit-point",
                ReturnDate = new DateTime(2026, 10, 21),
                AmountUsd = 1m
            }
        };
        var lines = new[]
        {
            Line("phone", "later", "profit-point", ExpenseStatusRules.Submitted, new DateTime(2026, 9, 1), 284m, 0, returned: false)
        };

        Assert.Equal(1m, ExpenseCreditRules.AppliedByLine(sources, lines)["phone"]);
        Assert.True(ExpenseCreditRules.HasBeenUsed("ret", sources, lines));
    }

    [Fact]
    public void Unused_credit_can_still_be_undone()
    {
        var sources = new[]
        {
            new ExpenseCreditSource
            {
                LineId = "ret",
                ReportId = "paid",
                ChargeToOrganizationId = "profit-point",
                ReturnDate = new DateTime(2026, 10, 21),
                AmountUsd = 1m
            }
        };
        var lines = new[]
        {
            Line("ret", "paid", "profit-point", ExpenseStatusRules.Reimbursed, new DateTime(2026, 8, 10), 1m, 0, returned: true)
        };

        Assert.False(ExpenseCreditRules.HasBeenUsed("ret", sources, lines));
    }

    [Fact]
    public void Same_currency_credit_uses_the_later_lines_rate()
    {
        var sources = new[]
        {
            new ExpenseCreditSource
            {
                LineId = "ret",
                ReportId = "paid",
                ChargeToOrganizationId = "profit-point",
                ReturnDate = new DateTime(2026, 10, 21),
                CurrencyCode = "EUR",
                SpentAmount = 10m,
                ExchangeRateToUsd = 1.2m,
                AmountUsd = 12m
            }
        };
        var lines = new[]
        {
            new ExpenseCreditLine
            {
                LineId = "hotel",
                ReportId = "draft",
                ChargeToOrganizationId = "profit-point",
                ReportStatus = ExpenseStatusRules.Draft,
                Date = new DateTime(2026, 11, 1),
                CurrencyCode = "EUR",
                SpentAmount = 100m,
                ExchangeRateToUsd = 1.1m,
                AmountUsd = 110m
            }
        };

        var applied = ExpenseCreditRules.AppliedByLine(sources, lines);
        Assert.Equal(11m, applied["hotel"]);
    }

    [Fact]
    public void Different_currency_credit_converts_at_the_credit_rate()
    {
        var sources = new[]
        {
            new ExpenseCreditSource
            {
                LineId = "ret",
                ReportId = "paid",
                ChargeToOrganizationId = "profit-point",
                ReturnDate = new DateTime(2026, 10, 21),
                CurrencyCode = "EUR",
                SpentAmount = 10m,
                ExchangeRateToUsd = 1.2m,
                AmountUsd = 12m
            }
        };
        var lines = new[]
        {
            new ExpenseCreditLine
            {
                LineId = "phone",
                ReportId = "draft",
                ChargeToOrganizationId = "profit-point",
                ReportStatus = ExpenseStatusRules.Draft,
                Date = new DateTime(2026, 11, 1),
                CurrencyCode = "USD",
                SpentAmount = 50m,
                ExchangeRateToUsd = 1m,
                AmountUsd = 50m
            }
        };

        var applied = ExpenseCreditRules.AppliedByLine(sources, lines);
        Assert.Equal(12m, applied["phone"]);
    }

    [Fact]
    public void Older_credit_is_applied_before_a_later_return()
    {
        var sources = new[]
        {
            new ExpenseCreditSource
            {
                LineId = "c1", ReportId = "A", ChargeToOrganizationId = "profit-point",
                ReturnDate = new DateTime(2026, 10, 1), AmountUsd = 10m
            },
            new ExpenseCreditSource
            {
                LineId = "c2", ReportId = "A", ChargeToOrganizationId = "profit-point",
                ReturnDate = new DateTime(2026, 10, 10), AmountUsd = 50m
            }
        };
        var lines = new[]
        {
            Line("c1", "A", "profit-point", ExpenseStatusRules.Reimbursed, new DateTime(2026, 9, 1), 10m, 0, returned: true),
            Line("c2", "A", "profit-point", ExpenseStatusRules.Reimbursed, new DateTime(2026, 9, 2), 50m, 1, returned: true),
            Line("mid", "B", "profit-point", ExpenseStatusRules.Submitted, new DateTime(2026, 9, 1), 30m, 0, returned: false),
            Line("after", "B", "profit-point", ExpenseStatusRules.Draft, new DateTime(2026, 10, 11), 20m, 1, returned: false)
        };

        var applied = ExpenseCreditRules.AppliedByLine(sources, lines);
        Assert.Equal(30m, applied["mid"]);
        Assert.Equal(20m, applied["after"]);
        Assert.Equal(0m, ExpenseReportMoneyRules.NetForReport("B", sources, lines, creditsEnabled: true));
        Assert.Equal(50m, ExpenseReportMoneyRules.NetForReport("B", sources, lines, creditsEnabled: false));
    }

    [Fact]
    public void Summary_shows_gross_credit_and_net()
    {
        var applied = new Dictionary<string, decimal> { ["hotel"] = 25m };
        var money = ExpenseReportMoneyRules.Summarize(
            [
                new ExpenseMoneyLine
                {
                    LineId = "hotel",
                    CurrencyCode = "EUR",
                    Color = "#F9A825",
                    SpentAmount = 100m,
                    AmountUsd = 120m
                },
                new ExpenseMoneyLine
                {
                    LineId = "ret",
                    CurrencyCode = "USD",
                    SpentAmount = 40m,
                    AmountUsd = 40m,
                    IsReturned = true
                }
            ],
            applied,
            creditsEnabled: true);

        Assert.Equal(160m, money.GrossUsd);
        Assert.Equal(25m, money.CreditUsd);
        Assert.Equal(135m, money.NetUsd);
        Assert.Equal(40m, money.CreditCreatedUsd);
        Assert.Equal("USD", money.Currencies[0].CurrencyCode);
        Assert.Equal(100m, money.Currencies[1].SpentAmount);
        Assert.Equal(120m, money.Currencies[1].UsdAmount);
    }

    [Fact]
    public void Pay_words_follow_the_audience_and_the_stored_status_stays()
    {
        Assert.Equal("Paid", ExpensePayLabelRules.StatusWord(ExpenseStatusRules.Reimbursed));
        Assert.Equal("Submitted", ExpensePayLabelRules.StatusWord(ExpenseStatusRules.Submitted));
        Assert.Equal("Mark paid", ExpensePayLabelRules.Mark);
        Assert.Equal("Undo payment", ExpensePayLabelRules.Undo);
    }

    [Fact]
    public void Payment_date_is_the_start_of_that_day_in_the_owner_zone()
    {
        var winter = ExpensePaymentDateRules.ToStoredUtc(new DateTime(2026, 1, 15), "America/New_York");
        var summer = ExpensePaymentDateRules.ToStoredUtc(new DateTime(2026, 7, 15), "America/New_York");
        Assert.Equal(new DateTime(2026, 1, 15, 5, 0, 0, DateTimeKind.Utc), winter);
        Assert.Equal(new DateTime(2026, 7, 15, 4, 0, 0, DateTimeKind.Utc), summer);
        Assert.Equal(new DateTime(2026, 1, 15), ExpensePaymentDateRules.ToCalendarDate(winter, "America/New_York"));
        Assert.Equal(new DateTime(2026, 7, 15), ExpensePaymentDateRules.ToCalendarDate(summer, "America/New_York"));
        var october = new DateTime(2026, 10, 19, 4, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 10, 19), ExpensePaymentDateRules.CalendarDay(october));
        Assert.Null(ExpensePaymentDateRules.CalendarDay(null));
        Assert.Equal(
            new DateTime(2026, 10, 19),
            ExpensePaymentDateRules.ShownDay(hasCalendarDate: true, new DateTime(2026, 10, 19), october));
        Assert.Equal(
            new DateTime(2026, 10, 19),
            ExpensePaymentDateRules.ShownDay(hasCalendarDate: false, default, october));
    }

    [Fact]
    public void Company_default_currency_is_rate_one()
    {
        Assert.Equal(1m, ExpenseCurrencyRules.NormalizeRate("EUR", 1.25m, "EUR"));
        Assert.Equal(1.1m, ExpenseCurrencyRules.NormalizeRate("USD", 1.1m, "EUR"));

        var home = ExpenseMoneyRules.Apply(
            ExpenseCategoryRules.Hotel, null, null, 0.555m,
            spentAmount: 10m, currencyCode: "EUR", catalogRate: 1.4m,
            savedRate: 1.4m, savedCurrency: "EUR", homeCode: "EUR",
            userEntersRate: true, enteredRate: 1.4m);
        Assert.Equal(1m, home.ExchangeRateToUsd);
        Assert.Equal(10m, home.AmountUsd);

        var entered = ExpenseMoneyRules.Apply(
            ExpenseCategoryRules.Hotel, null, null, 0.555m,
            spentAmount: 100m, currencyCode: "EUR", catalogRate: 1.5m,
            savedRate: 1.2m, savedCurrency: "EUR", homeCode: "USD",
            userEntersRate: true, enteredRate: 1.4m);
        Assert.Equal(1.4m, entered.ExchangeRateToUsd);
        Assert.Equal(140m, entered.AmountUsd);
    }

    [Fact]
    public void Partial_credit_cannot_exceed_the_line()
    {
        Assert.Null(ExpenseCreditEntryRules.RejectAmount(25m, "USD", 50m, "USD", 1m, 50m));
        Assert.Equal(
            "Amount cannot be more than the line.",
            ExpenseCreditEntryRules.RejectAmount(51m, "USD", 50m, "USD", 1m, 50m));
        Assert.Equal(
            "Amount must be greater than zero.",
            ExpenseCreditEntryRules.RejectAmount(0m, "USD", 50m, "USD", 1m, 50m));
        Assert.Equal(
            "Amount cannot be more than the line.",
            ExpenseCreditEntryRules.RejectAmount(40m, "EUR", 50m, "USD", 1.5m, 50m));
    }

    [Fact]
    public void Form_credit_reduces_only_the_named_amount()
    {
        var money = ExpenseReportMoneyRules.Summarize(
            [
                new ExpenseMoneyLine
                {
                    LineId = "hotel",
                    CurrencyCode = "USD",
                    SpentAmount = 100m,
                    AmountUsd = 100m
                }
            ],
            appliedByLine: null,
            creditsEnabled: true,
            formCreditUsd: 25m);

        Assert.Equal(25m, money.CreditUsd);
        Assert.Equal(75m, money.NetUsd);
    }

    private static ExpenseCreditLine Line(
        string id, string reportId, string chargeTo, string status, DateTime date, decimal amount, int sort, bool returned) =>
        new()
        {
            LineId = id,
            ReportId = reportId,
            ChargeToOrganizationId = chargeTo,
            ReportStatus = status,
            Date = date,
            AmountUsd = amount,
            SortOrder = sort,
            IsReturned = returned
        };
}
