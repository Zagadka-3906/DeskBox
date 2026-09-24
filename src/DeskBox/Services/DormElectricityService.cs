using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DeskBox.Models;

namespace DeskBox.Services;

public sealed record DormElectricityCampus(string Client, string Name);
public sealed record DormElectricityBuilding(string Id, string Name);
public sealed record DormElectricityFloor(string Id, string Name);
public sealed record DormElectricityRoom(string Id, string Name);
public sealed record DormElectricityLocation(string Client, string BuildingId, string BuildingName, string RoomName,
    string FloorId = "", string RoomId = "");
public sealed record DormElectricityDay(DateTime RecordedAt, decimal RemainingKwh, decimal TotalUsedKwh, decimal? UsedKwh);
public sealed record DormElectricityPayment(DateTime PaidAt, decimal PurchasedKwh, decimal AmountYuan, string Method);
public sealed record DormElectricitySnapshot(
    IReadOnlyList<DormElectricityDay> Days,
    IReadOnlyList<DormElectricityPayment> Payments,
    decimal? RemainingKwh = null,
    DateTime? BalanceRecordedAt = null);

/// <summary>Reads the university SIMS query pages through their normal read-only forms.</summary>
public sealed class DormElectricityService
{
    public const string LihuPhaseTwoClient = "172.25.100.105";
    private static readonly Uri BaseUri = new("http://192.168.84.3:9090");
    private static readonly RegexOptions HtmlRegexOptions =
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;

    public static IReadOnlyList<DormElectricityCampus> Campuses { get; } =
    [
        new("192.168.84.1", "北校区"),
        new("192.168.84.110", "南校区"),
        new("172.21.101.11", "西丽校区"),
        new(LihuPhaseTwoClient, "丽湖二期"),
        new("192.168.84.87", "深大新斋区")
    ];

    static DormElectricityService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<IReadOnlyList<DormElectricityBuilding>> GetBuildingsAsync(
        string client, CancellationToken cancellationToken = default)
    {
        ValidateClient(client);
        if (client == LihuPhaseTwoClient)
        {
            return await LihuElectricityService.GetBuildingsAsync(cancellationToken);
        }
        using HttpClient http = CreateClient();
        string html = await GetHtmlAsync(http, LoginUri(client), cancellationToken);
        string options = MatchGroup(html, "<select\\b[^>]*name=[\"']buildingId[\"'][^>]*>(.*?)</select>");
        if (string.IsNullOrWhiteSpace(options))
        {
            throw new InvalidOperationException("无法读取校区楼栋列表。站点页面可能已更新。");
        }

        return Regex.Matches(options, "<option\\b[^>]*value=[\"']([^\"']*)[\"'][^>]*>(.*?)</option>", HtmlRegexOptions)
            .Select(match => new DormElectricityBuilding(
                WebUtility.HtmlDecode(match.Groups[1].Value).Trim(),
                CleanText(match.Groups[2].Value)))
            .Where(building => !string.IsNullOrWhiteSpace(building.Id))
            .ToArray();
    }

    public Task<IReadOnlyList<DormElectricityFloor>> GetFloorsAsync(
        string client, string buildingId, CancellationToken cancellationToken = default)
    {
        ValidateClient(client);
        if (client != LihuPhaseTwoClient)
        {
            return Task.FromResult<IReadOnlyList<DormElectricityFloor>>([]);
        }
        return LihuElectricityService.GetFloorsAsync(buildingId, cancellationToken);
    }

    public Task<IReadOnlyList<DormElectricityRoom>> GetRoomsAsync(
        string client, string buildingId, string floorId, CancellationToken cancellationToken = default)
    {
        ValidateClient(client);
        if (client != LihuPhaseTwoClient)
        {
            return Task.FromResult<IReadOnlyList<DormElectricityRoom>>([]);
        }
        return LihuElectricityService.GetRoomsAsync(buildingId, floorId, cancellationToken);
    }

    public async Task<DormElectricitySnapshot> GetSnapshotAsync(
        DormElectricityLocation location,
        DateTime today,
        string usagePeriod,
        string paymentPeriod,
        CancellationToken cancellationToken = default)
    {
        ValidateClient(location.Client);
        if (location.Client == LihuPhaseTwoClient)
        {
            return await LihuElectricityService.GetSnapshotAsync(
                location, today, usagePeriod, paymentPeriod, cancellationToken);
        }
        if (string.IsNullOrWhiteSpace(location.BuildingId) ||
            string.IsNullOrWhiteSpace(location.RoomName))
        {
            throw new ArgumentException("请先在格子设置中选择楼栋并填写房间号。", nameof(location));
        }

        using HttpClient http = CreateClient();
        string login = await GetHtmlAsync(http, LoginUri(location.Client), cancellationToken);
        string action = MatchGroup(login, "<form\\b[^>]*name=[\"']loginForm[\"'][^>]*action=[\"']([^\"']+)[\"']");
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new InvalidOperationException("无法读取电量查询入口。站点页面可能已更新。");
        }

        string roomPage = await PostHtmlAsync(http, new Uri(BaseUri, action),
        [
            Pair("client", location.Client),
            Pair("buildingId", location.BuildingId),
            Pair("buildingName", string.Empty),
            Pair("roomName", location.RoomName.Trim()),
            Pair("select", " 查询 ")
        ], cancellationToken);
        string roomId = MatchGroup(roomPage, "<input\\b[^>]*name=[\"']roomId[\"'][^>]*value=[\"']([^\"']+)[\"']");
        if (string.IsNullOrWhiteSpace(roomId) ||
            !roomPage.Contains("selectListForm", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("未找到这个房间。请检查校区、楼栋和房间号。");
        }

        DateTime end = today.Date;
        DateTime usageStart = DormElectricityPeriods.StartDate(
            DormElectricityPeriods.Normalize(usagePeriod, DormElectricityPeriods.SevenDays), end);
        DateTime baselineStart = usageStart.AddDays(-1);
        string usageHtml = await QueryAsync(
            http, location, roomId, "2", baselineStart, end, 1, cancellationToken);
        var usage = new List<DormElectricityDay>(ParseUsageRows(usageHtml));
        int usagePageCount = ParsePageCount(usageHtml);
        for (int page = 2; page <= usagePageCount; page++)
        {
            string html = await QueryAsync(
                http, location, roomId, "2", baselineStart, end, page, cancellationToken);
            usage.AddRange(ParseUsageRows(html));
        }
        IReadOnlyList<DormElectricityDay> days = BuildRecentDays(usage, usageStart, end);

        DateTime paymentStart = DormElectricityPeriods.StartDate(
            DormElectricityPeriods.Normalize(paymentPeriod, DormElectricityPeriods.OneYear), end);
        string paymentHtml = await QueryAsync(
            http, location, roomId, "1", paymentStart, end, 1, cancellationToken);
        int pageCount = ParsePageCount(paymentHtml);
        var payments = new List<DormElectricityPayment>(ParsePaymentRows(paymentHtml));
        for (int page = 2; page <= pageCount; page++)
        {
            string html = await QueryAsync(
                http, location, roomId, "1", paymentStart, end, page, cancellationToken);
            payments.AddRange(ParsePaymentRows(html));
        }
        return new DormElectricitySnapshot(
            days,
            payments.Where(payment => payment.PaidAt >= paymentStart &&
                    payment.PaidAt.Date <= end)
                .OrderByDescending(payment => payment.PaidAt).ToArray());
    }

    private static async Task<string> QueryAsync(
        HttpClient http, DormElectricityLocation location, string roomId,
        string type, DateTime begin, DateTime end, int page, CancellationToken cancellationToken)
    {
        var uri = new Uri(BaseUri, "/cgcSims/selectList.do" +
            (page > 1 ? "?pageNo=" + page.ToString(CultureInfo.InvariantCulture) : string.Empty));
        return await PostHtmlAsync(http, uri,
        [
            Pair("hiddenType", "0"),
            Pair("isHost", "0"),
            Pair("beginTime", begin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Pair("endTime", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Pair("type", type),
            Pair("client", location.Client),
            Pair("roomId", roomId),
            Pair("roomName", location.RoomName.Trim()),
            Pair("building", string.Empty)
        ], cancellationToken);
    }

    internal static IReadOnlyList<DormElectricityDay> ParseUsageRows(string html)
    {
        return ReadRows(html, 6)
            .Select(cells => new
            {
                Remaining = ParseDecimal(cells[2]),
                TotalUsed = ParseDecimal(cells[3]),
                RecordedAt = ParseDate(cells[5])
            })
            .Where(row => row.Remaining is not null && row.TotalUsed is not null && row.RecordedAt is not null)
            .Select(row => new DormElectricityDay(row.RecordedAt!.Value, row.Remaining!.Value, row.TotalUsed!.Value, null))
            .OrderBy(row => row.RecordedAt)
            .ToArray();
    }

    internal static IReadOnlyList<DormElectricityDay> BuildRecentDays(
        IReadOnlyList<DormElectricityDay> snapshots, DateTime startInclusive, DateTime endExclusive)
    {
        var byDay = snapshots
            .GroupBy(day => day.RecordedAt.Date)
            .Select(group => group.MaxBy(day => day.RecordedAt)!)
            .OrderBy(day => day.RecordedAt.Date)
            .ToArray();
        var result = new List<DormElectricityDay>();
        for (int index = 0; index < byDay.Length; index++)
        {
            DormElectricityDay current = byDay[index];
            if (current.RecordedAt.Date < startInclusive.Date ||
                current.RecordedAt.Date >= endExclusive.Date)
            {
                continue;
            }
            decimal? used = index > 0 &&
                byDay[index - 1].RecordedAt.Date == current.RecordedAt.Date.AddDays(-1)
                    ? current.TotalUsedKwh - byDay[index - 1].TotalUsedKwh
                    : null;
            result.Add(current with { UsedKwh = used >= 0 ? used : null });
        }
        return result;
    }

    internal static IReadOnlyList<DormElectricityPayment> ParsePaymentRows(string html)
    {
        return ReadRows(html, 7)
            .Select(cells => new
            {
                Purchased = ParseDecimal(cells[4]),
                Amount = ParseDecimal(cells[5]),
                PaidAt = ParseDate(cells[6]),
                Method = cells[3]
            })
            .Where(row => row.Purchased is not null && row.Amount is not null && row.PaidAt is not null)
            .Select(row => new DormElectricityPayment(
                row.PaidAt!.Value, row.Purchased!.Value, row.Amount!.Value, row.Method))
            .ToArray();
    }

    internal static int ParsePageCount(string html)
    {
        string text = CleanText(html);
        string count = MatchGroup(text, "当前页\\s*[:：]\\s*\\d+\\s*/\\s*(\\d+)");
        return int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out int pages)
            ? Math.Clamp(pages, 1, 100)
            : 1;
    }

    private static IReadOnlyList<string[]> ReadRows(string html, int expectedCells)
    {
        string table = MatchGroup(html, "<table\\b[^>]*id=[\"']oTable[\"'][^>]*>(.*?)</table>");
        if (string.IsNullOrWhiteSpace(table))
        {
            throw new InvalidOperationException("无法读取查询结果。站点页面可能已更新。");
        }
        return Regex.Matches(table, "<tr\\b[^>]*>(.*?)</tr>", HtmlRegexOptions)
            .Select(row => Regex.Matches(row.Groups[1].Value, "<td\\b[^>]*>(.*?)</td>", HtmlRegexOptions)
                .Select(cell => CleanText(cell.Groups[1].Value)).ToArray())
            .Where(cells => cells.Length == expectedCells &&
                int.TryParse(cells[0], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .ToArray();
    }

    private static decimal? ParseDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number)
            ? number : null;

    private static DateTime? ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
            ? date : null;

    private static string MatchGroup(string text, string pattern) =>
        Regex.Match(text, pattern, HtmlRegexOptions).Groups[1].Value;

    private static string CleanText(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), "\\s+", " ").Trim();

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);

    private static void ValidateClient(string client)
    {
        if (!Campuses.Any(campus => campus.Client == client))
        {
            throw new ArgumentException("请选择受支持的校区。", nameof(client));
        }
    }

    private static Uri LoginUri(string client) =>
        new(BaseUri, "/cgcSims/login.do?task=station&client=" + Uri.EscapeDataString(client));

    private static HttpClient CreateClient() => new(new HttpClientHandler
    {
        CookieContainer = new CookieContainer(),
        UseCookies = true,
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    })
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private static async Task<string> GetHtmlAsync(
        HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Decode(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    private static async Task<string> PostHtmlAsync(
        HttpClient http, Uri uri, IEnumerable<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        using var body = new FormUrlEncodedContent(fields);
        using HttpResponseMessage response = await http.PostAsync(uri, body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Decode(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    private static string Decode(byte[] bytes) => Encoding.GetEncoding("GB18030").GetString(bytes);
}
