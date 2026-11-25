using System.Globalization;
using DeskBox.Models;
using DeskBox.Services;

namespace DormElectricityBridge;

internal sealed record UsageRow(string Id, string Date, string Value);
internal sealed record StatusView(
    string Room, string Balance, string RecordedAt, string Status,
    IReadOnlyList<UsageRow> Rows, int Page, int PageCount, string Period);

internal sealed class BridgeQueries(DormElectricityService electricity)
{
    private readonly SemaphoreSlim _fetchGate = new(1, 1);
    private CacheEntry? _cache;

    private sealed record CacheEntry(BridgeSettings Settings, DateTime CheckedAt, DormElectricitySnapshot Snapshot);

    public async Task<DormElectricitySnapshot> SnapshotAsync(BridgeSettings settings, CancellationToken token)
    {
        await _fetchGate.WaitAsync(token);
        try
        {
            DateTime now = DateTime.Now;
            if (_cache is { } hit && hit.Settings.Location == settings.Location &&
                hit.Settings.UsagePeriod == settings.UsagePeriod && hit.Settings.PaymentPeriod == settings.PaymentPeriod &&
                hit.CheckedAt.Date == now.Date && now - hit.CheckedAt < TimeSpan.FromMinutes(4))
                return hit.Snapshot;

            var data = await electricity.GetSnapshotAsync(
                settings.Location, now, settings.UsagePeriod, settings.PaymentPeriod, token);
            _cache = new(settings, DateTime.Now, data);
            return data;
        }
        finally { _fetchGate.Release(); }
    }

    public static string RoomLabel(DormElectricityLocation location) =>
        location.Client == DormElectricityService.LihuPhaseTwoClient
            ? location.RoomName : (location.BuildingName + " " + location.RoomName).Trim();

    public static string Format(decimal kwh) => kwh.ToString("0.##", CultureInfo.InvariantCulture);

    private static string PeriodLabel(string period) => period switch
    {
        "ThreeDays" => "近 3 天",
        "SevenDays" => "近 7 天",
        "OneMonth" => "近 1 个月",
        "SixMonths" => "近半年",
        _ => "近 1 年"
    };

    public async Task<StatusView> GetViewAsync(BridgeSettings settings, bool payments, int page, CancellationToken token)
    {
        string room = RoomLabel(settings.Location);
        string period = PeriodLabel(payments ? settings.PaymentPeriod : settings.UsagePeriod);
        if (string.IsNullOrWhiteSpace(settings.Location.RoomName))
            return new(room, "—", "", "请先选择宿舍", [], 1, 1, period);

        try
        {
            var snapshot = await SnapshotAsync(settings, token);
            var latest = snapshot.Days.LastOrDefault();
            decimal? balance = snapshot.RemainingKwh ?? latest?.RemainingKwh;
            DateTime? recordedAt = snapshot.BalanceRecordedAt ?? latest?.RecordedAt;
            var rows = payments
                ? snapshot.Payments.Select((p, index) => new UsageRow(
                    index.ToString(CultureInfo.InvariantCulture), p.PaidAt.ToString("MM-dd HH:mm"),
                    $"{Format(p.PurchasedKwh)} 度 · ¥{Format(p.AmountYuan)}")).ToList()
                : DailyRows(snapshot, settings.UsagePeriod, DateTime.Today);
            int pageCount = Math.Max(1, (rows.Count + 59) / 60);
            int currentPage = Math.Clamp(page, 1, pageCount);
            return new(room, balance is decimal kwh ? Format(kwh) + " 度" : "—",
                recordedAt is DateTime at ? $"截至 {at:MM-dd HH:mm}" : "",
                rows.Count == 0 ? (payments ? "该时段暂无缴费" : "该时段暂无用电记录") : "",
                rows.Skip((currentPage - 1) * 60).Take(60).ToArray(), currentPage, pageCount, period);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or ArgumentException)
        {
            return new(room, "—", "", "校园用电查询失败，请检查校园网络或宿舍设置", [], 1, 1, period);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return new(room, "—", "", "校园用电查询超时", [], 1, 1, period);
        }
    }

    internal static List<UsageRow> DailyRows(DormElectricitySnapshot snapshot, string period, DateTime today)
    {
        DateTime start = DormElectricityPeriods.StartDate(period, today);
        var days = snapshot.Days.ToDictionary(d => d.RecordedAt.Date);
        List<UsageRow> rows = [];
        for (DateTime day = today.Date.AddDays(-1); day >= start; day = day.AddDays(-1))
        {
            days.TryGetValue(day, out var reading);
            var interval = reading?.IntervalUsage;
            rows.Add(new(day.ToString("yyyy-MM-dd"),
                interval is null ? day.ToString("MM-dd") : $"{interval.FirstDay:MM-dd}–{day:MM-dd}",
                interval is not null ? $"合计 {Format(interval.UsedKwh)} 度" :
                    reading?.UsedKwh is decimal used ? Format(used) + " 度" : "无单日数据"));
        }
        return rows;
    }
}
