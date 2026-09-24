using DeskBox.Services;
using DeskBox.Models;

namespace DeskBox.Tests;

public sealed class DormElectricityServiceTests
{
    [Fact]
    public void DailyUsage_UsesAdjacentCumulativeReadingsAndMarksMissingDay()
    {
        string html = Table(
            UsageRow(1, "2026-09-20 23:59:00", "14.5", "120.2"),
            UsageRow(2, "2026-09-21 23:59:00", "12.8", "121.9"),
            UsageRow(3, "2026-09-23 23:59:00", "9.7", "125.0"));

        IReadOnlyList<DormElectricityDay> days = DormElectricityService.BuildRecentDays(
            DormElectricityService.ParseUsageRows(html),
            new DateTime(2026, 9, 20), new DateTime(2026, 9, 24));

        Assert.Equal(3, days.Count);
        Assert.Null(days[0].UsedKwh);
        Assert.Equal(1.7m, days[1].UsedKwh);
        Assert.Null(days[2].UsedKwh);
        Assert.Equal(9.7m, days[2].RemainingKwh);
    }

    [Fact]
    public void DailyUsage_FiltersToSelectedPeriodButUsesPreviousDayAsBaseline()
    {
        string html = Table(
            UsageRow(1, "2026-09-21 23:59:00", "12.8", "121.9"),
            UsageRow(2, "2026-09-22 23:59:00", "11.0", "123.7"),
            UsageRow(3, "2026-09-24 23:59:00", "9.7", "125.0"),
            UsageRow(4, "2026-09-25 08:00:00", "9.5", "125.2"));

        IReadOnlyList<DormElectricityDay> days = DormElectricityService.BuildRecentDays(
            DormElectricityService.ParseUsageRows(html),
            DormElectricityPeriods.StartDate(DormElectricityPeriods.ThreeDays, new DateTime(2026, 9, 25)),
            new DateTime(2026, 9, 25));

        Assert.Equal(2, days.Count);
        Assert.Equal(new DateTime(2026, 9, 22), days[0].RecordedAt.Date);
        Assert.Equal(1.8m, days[0].UsedKwh);
        Assert.Null(days[1].UsedKwh);
    }

    [Theory]
    [InlineData(DormElectricityPeriods.ThreeDays, "2026-09-22")]
    [InlineData(DormElectricityPeriods.SevenDays, "2026-09-18")]
    [InlineData(DormElectricityPeriods.OneMonth, "2026-08-25")]
    [InlineData(DormElectricityPeriods.SixMonths, "2026-03-25")]
    [InlineData(DormElectricityPeriods.OneYear, "2025-09-25")]
    public void PeriodStart_UsesCalendarMonthsAndYears(string period, string expected)
    {
        Assert.Equal(DateTime.Parse(expected),
            DormElectricityPeriods.StartDate(period, new DateTime(2026, 9, 25)));
    }

    [Fact]
    public void PaymentParser_ReadsAmountsWithoutIncludingPurchaser()
    {
        string html = Table(
            "<tr><td>1</td><td>333</td><td>private name</td><td>支付宝</td>" +
            "<td>12.50</td><td>10.00</td><td>2026-09-24 09:12:00</td></tr>");

        DormElectricityPayment payment = Assert.Single(DormElectricityService.ParsePaymentRows(html));

        Assert.Equal(12.50m, payment.PurchasedKwh);
        Assert.Equal(10m, payment.AmountYuan);
        Assert.Equal("支付宝", payment.Method);
        Assert.Equal(new DateTime(2026, 9, 24, 9, 12, 0), payment.PaidAt);
    }

    [Theory]
    [InlineData("当前页： 1 / 3", 3)]
    [InlineData("当前页: 2/2", 2)]
    public void PageCount_ParsesPagination(string html, int expected)
    {
        Assert.Equal(expected, DormElectricityService.ParsePageCount(html));
    }

    private static string Table(params string[] rows) =>
        "<table id=\"oTable\">" + string.Join(string.Empty, rows) + "</table>";

    private static string UsageRow(int number, string date, string remaining, string totalUsed) =>
        $"<tr><td>{number}</td><td>333</td><td>{remaining}</td>" +
        $"<td>{totalUsed}</td><td>150</td><td>{date}</td></tr>";
}
