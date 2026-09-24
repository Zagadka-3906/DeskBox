using DeskBox.Services;
using DeskBox.Models;
using System.Text.Json;

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

    [Fact]
    public void LihuOptions_KeepDistinctBuildingFloorAndRoomIds()
    {
        string html = "<select name=\"drlouming\"><option value=\"\">楼栋</option>" +
            "<option value=\"01\">梧桐树#</option></select>" +
            "<select name=\"drceng\"><option value=\"0105\">梧桐树#5层</option></select>" +
            "<select name=\"drfangjian\"><option value=\"010501\">梧桐树#501</option></select>";

        Assert.Equal(("01", "梧桐树#"), Assert.Single(LihuElectricityService.ReadOptions(html, "drlouming")));
        Assert.Equal(("0105", "梧桐树#5层"), Assert.Single(LihuElectricityService.ReadOptions(html, "drceng")));
        Assert.Equal(("010501", "梧桐树#501"), Assert.Single(LihuElectricityService.ReadOptions(html, "drfangjian")));
    }

    [Fact]
    public void LihuRecords_ReadDailyUsagePaymentsAndBalance()
    {
        string usageHtml = "<h6>梧桐树#501剩余电量：<span class=\"number orange\">-503.67</span> 度</h6>" +
            "<table class=\"dataTable\"><tr class=\"titleLine\"><td>日期</td></tr>" +
            "<tr class=\"contentLine\"><td>2026-09-24</td><td>梧桐树#501</td><td>8.78</td><td>0.6998</td></tr></table>" +
            "<div class=\"pageer\">第 1 页 / 共 3 页</div>";
        string paymentHtml = "<table class=\"dataTable\"><tr class=\"contentLine\">" +
            "<td>2026/9/15 8:58:19</td><td>梧桐树#501</td><td>428.69</td>" +
            "<td>300.00</td><td>充值人</td></tr></table>";

        Assert.Equal(-503.67m, LihuElectricityService.ParseBalance(usageHtml));
        DormElectricityDay day = Assert.Single(LihuElectricityService.ParseUsageRows(usageHtml, -503.67m));
        Assert.Equal(new DateTime(2026, 9, 24), day.RecordedAt);
        Assert.Equal(8.78m, day.UsedKwh);
        DormElectricityPayment payment = Assert.Single(LihuElectricityService.ParsePaymentRows(paymentHtml));
        Assert.Equal(428.69m, payment.PurchasedKwh);
        Assert.Equal(300m, payment.AmountYuan);
        Assert.Equal(3, LihuElectricityService.ParsePageCount(usageHtml));
    }

    [Fact]
    public void LihuLocationSettings_RoundTripThroughAppSettings()
    {
        var settings = new AppSettings
        {
            DormElectricityClient = DormElectricityService.LihuPhaseTwoClient,
            DormElectricityBuildingId = "01",
            DormElectricityFloorId = "0105",
            DormElectricityRoomId = "010501",
            DormElectricityRoomName = "梧桐树#501"
        };

        string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        AppSettings? restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);

        Assert.NotNull(restored);
        Assert.Equal(settings.DormElectricityFloorId, restored.DormElectricityFloorId);
        Assert.Equal(settings.DormElectricityRoomId, restored.DormElectricityRoomId);
    }

    private static string Table(params string[] rows) =>
        "<table id=\"oTable\">" + string.Join(string.Empty, rows) + "</table>";

    private static string UsageRow(int number, string date, string remaining, string totalUsed) =>
        $"<tr><td>{number}</td><td>333</td><td>{remaining}</td>" +
        $"<td>{totalUsed}</td><td>150</td><td>{date}</td></tr>";
}
