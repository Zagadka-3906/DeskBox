using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>Reads the Lihu phase two electricity site's cascading ASP.NET forms.</summary>
internal static class LihuElectricityService
{
    private static readonly Uri BaseUri = new("http://172.25.100.105:8010/");
    private static readonly RegexOptions HtmlOptions =
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;

    static LihuElectricityService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static async Task<IReadOnlyList<DormElectricityBuilding>> GetBuildingsAsync(CancellationToken token)
    {
        using HttpClient http = CreateClient();
        string html = await ReadAsync(http, BaseUri, null, token);
        return ReadOptions(html, "drlouming")
            .Select(option => new DormElectricityBuilding(option.Id, option.Name)).ToArray();
    }

    public static async Task<IReadOnlyList<DormElectricityFloor>> GetFloorsAsync(
        string buildingId, CancellationToken token)
    {
        using HttpClient http = CreateClient();
        string html = await ReadAsync(http, BaseUri, null, token);
        RequireOption(html, "drlouming", buildingId);
        html = await PostBackAsync(http, html, "drlouming", buildingId, "", "", token);
        return ReadOptions(html, "drceng")
            .Select(option => new DormElectricityFloor(option.Id, option.Name)).ToArray();
    }

    public static async Task<IReadOnlyList<DormElectricityRoom>> GetRoomsAsync(
        string buildingId, string floorId, CancellationToken token)
    {
        using HttpClient http = CreateClient();
        string html = await SelectFloorAsync(http, buildingId, floorId, token);
        return ReadOptions(html, "drfangjian")
            .Select(option => new DormElectricityRoom(option.Id, option.Name)).ToArray();
    }

    public static async Task<DormElectricitySnapshot> GetSnapshotAsync(
        DormElectricityLocation location, DateTime today,
        string usagePeriod, string paymentPeriod, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(location.BuildingId) ||
            string.IsNullOrWhiteSpace(location.FloorId) ||
            string.IsNullOrWhiteSpace(location.RoomId))
        {
            throw new ArgumentException("请在格子设置中选择丽湖二期的楼栋、楼层和房间。", nameof(location));
        }

        using HttpClient http = CreateClient();
        string login = await SelectFloorAsync(http, location.BuildingId, location.FloorId, token);
        RequireOption(login, "drfangjian", location.RoomId);
        var fields = HiddenFields(login);
        fields.Add(Pair("__EVENTTARGET", ""));
        fields.Add(Pair("__EVENTARGUMENT", ""));
        fields.Add(Pair("drlouming", location.BuildingId));
        fields.Add(Pair("drceng", location.FloorId));
        fields.Add(Pair("drfangjian", location.RoomId));
        fields.Add(Pair("radio", "usedR"));
        fields.Add(Pair("ImageButton1.x", "20"));
        fields.Add(Pair("ImageButton1.y", "10"));
        string usagePage = await ReadAsync(http, BaseUri, fields, token);
        if (!usagePage.Contains("usedRecord.aspx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("无法进入丽湖二期用电记录页。站点页面可能已更新。");
        }

        DateTime end = today.Date;
        DateTime usageStart = DormElectricityPeriods.StartDate(
            DormElectricityPeriods.Normalize(usagePeriod, DormElectricityPeriods.SevenDays), end);
        DateTime paymentStart = DormElectricityPeriods.StartDate(
            DormElectricityPeriods.Normalize(paymentPeriod, DormElectricityPeriods.OneYear), end);
        decimal? balance = ParseBalance(usagePage);
        if (balance is null)
        {
            throw new InvalidOperationException("无法读取丽湖二期剩余电量。站点页面可能已更新。");
        }

        string usageHtml = await SearchAsync(http, "usedRecord.aspx", usagePage, usageStart, end, token);
        var days = new List<DormElectricityDay>(ParseUsageRows(usageHtml, balance.Value));
        int usagePages = ParsePageCount(usageHtml);
        for (int page = 2; page <= usagePages; page++)
        {
            string html = await ReadAsync(http, new Uri(BaseUri, $"usedRecord.aspx?p={page}"), null, token);
            days.AddRange(ParseUsageRows(html, balance.Value));
        }

        string paymentPage = await ReadAsync(http, new Uri(BaseUri, "buyRecord.aspx"), null, token);
        if (!paymentPage.Contains("buyRecord.aspx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("无法进入丽湖二期购电记录页。站点页面可能已更新。");
        }
        string paymentHtml = await SearchAsync(http, "buyRecord.aspx", paymentPage, paymentStart, end, token);
        var payments = new List<DormElectricityPayment>(ParsePaymentRows(paymentHtml));
        int paymentPages = ParsePageCount(paymentHtml);
        for (int page = 2; page <= paymentPages; page++)
        {
            string html = await ReadAsync(http, new Uri(BaseUri, $"buyRecord.aspx?p={page}"), null, token);
            payments.AddRange(ParsePaymentRows(html));
        }

        return new DormElectricitySnapshot(
            days.Where(day => day.RecordedAt.Date >= usageStart && day.RecordedAt.Date < end)
                .GroupBy(day => day.RecordedAt.Date)
                .Select(group => group.Last())
                .OrderBy(day => day.RecordedAt).ToArray(),
            payments.Where(payment => payment.PaidAt.Date >= paymentStart && payment.PaidAt.Date <= end)
                .OrderByDescending(payment => payment.PaidAt).ToArray(),
            balance,
            end);
    }

    internal static IReadOnlyList<(string Id, string Name)> ReadOptions(string html, string selectName)
    {
        string select = Regex.Match(html,
            $"<select\\b[^>]*name=[\"']{Regex.Escape(selectName)}[\"'][^>]*>(.*?)</select>",
            HtmlOptions).Groups[1].Value;
        if (string.IsNullOrWhiteSpace(select))
        {
            throw new InvalidOperationException("无法读取丽湖二期宿舍选项。站点页面可能已更新。");
        }
        return Regex.Matches(select, "<option\\b[^>]*value=[\"']([^\"']*)[\"'][^>]*>(.*?)</option>", HtmlOptions)
            .Select(match => (Id: WebUtility.HtmlDecode(match.Groups[1].Value).Trim(),
                Name: CleanText(match.Groups[2].Value)))
            .Where(option => !string.IsNullOrWhiteSpace(option.Id)).ToArray();
    }

    internal static decimal? ParseBalance(string html)
    {
        string value = Regex.Match(html,
            "剩余电量\\s*[:：]\\s*<span\\b[^>]*>([-+]?\\d+(?:\\.\\d+)?)</span>", HtmlOptions)
            .Groups[1].Value;
        return ParseDecimal(value);
    }

    internal static IReadOnlyList<DormElectricityDay> ParseUsageRows(string html, decimal balance)
    {
        return ReadRows(html, 4)
            .Select(cells => (Date: ParseDate(cells[0]), Used: ParseDecimal(cells[2])))
            .Where(row => row.Date is not null && row.Used is not null)
            .Select(row => new DormElectricityDay(row.Date!.Value, balance, 0, row.Used))
            .ToArray();
    }

    internal static IReadOnlyList<DormElectricityPayment> ParsePaymentRows(string html)
    {
        return ReadRows(html, 5)
            .Select(cells => (Date: ParseDate(cells[0]), Purchased: ParseDecimal(cells[2]),
                Amount: ParseDecimal(cells[3]), Person: cells[4]))
            .Where(row => row.Date is not null && row.Purchased is not null && row.Amount is not null)
            .Select(row => new DormElectricityPayment(row.Date!.Value, row.Purchased!.Value,
                row.Amount!.Value, row.Person)).ToArray();
    }

    internal static int ParsePageCount(string html)
    {
        string value = Regex.Match(CleanText(html), "第\\s*\\d+\\s*页\\s*/\\s*共\\s*(\\d+)\\s*页",
            HtmlOptions).Groups[1].Value;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            ? Math.Clamp(count, 0, 100) : 0;
    }

    private static IReadOnlyList<string[]> ReadRows(string html, int expectedCells)
    {
        string table = Regex.Match(html, "<table\\b[^>]*class=[\"']dataTable[\"'][^>]*>(.*?)</table>",
            HtmlOptions).Groups[1].Value;
        if (string.IsNullOrWhiteSpace(table))
        {
            throw new InvalidOperationException("无法读取丽湖二期电费记录。站点页面可能已更新。");
        }
        return Regex.Matches(table, "<tr\\b[^>]*class=[\"']contentLine[\"'][^>]*>(.*?)</tr>", HtmlOptions)
            .Select(row => Regex.Matches(row.Groups[1].Value, "<td\\b[^>]*>(.*?)</td>", HtmlOptions)
                .Select(cell => CleanText(cell.Groups[1].Value)).ToArray())
            .Where(cells => cells.Length == expectedCells).ToArray();
    }

    private static async Task<string> SelectFloorAsync(
        HttpClient http, string buildingId, string floorId, CancellationToken token)
    {
        string html = await ReadAsync(http, BaseUri, null, token);
        RequireOption(html, "drlouming", buildingId);
        html = await PostBackAsync(http, html, "drlouming", buildingId, "", "", token);
        RequireOption(html, "drceng", floorId);
        return await PostBackAsync(http, html, "drceng", buildingId, floorId, "", token);
    }

    private static async Task<string> PostBackAsync(HttpClient http, string html, string target,
        string buildingId, string floorId, string roomId, CancellationToken token)
    {
        var fields = HiddenFields(html);
        fields.Add(Pair("__EVENTTARGET", target));
        fields.Add(Pair("__EVENTARGUMENT", ""));
        fields.Add(Pair("drlouming", buildingId));
        fields.Add(Pair("drceng", floorId));
        fields.Add(Pair("drfangjian", roomId));
        return await ReadAsync(http, BaseUri, fields, token);
    }

    private static void RequireOption(string html, string selectName, string id)
    {
        if (!ReadOptions(html, selectName).Any(option => option.Id == id))
        {
            throw new InvalidOperationException("未找到所选楼栋、楼层或房间，请重新选择。");
        }
    }

    private static async Task<string> SearchAsync(HttpClient http, string pageName,
        string html, DateTime start, DateTime end, CancellationToken token)
    {
        var fields = HiddenFields(html);
        fields.Add(Pair("txtstart", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        fields.Add(Pair("txtend", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        fields.Add(Pair("btnser", "查询"));
        return await ReadAsync(http, new Uri(BaseUri, pageName), fields, token);
    }

    private static List<KeyValuePair<string, string>> HiddenFields(string html)
    {
        var fields = new List<KeyValuePair<string, string>>();
        foreach (Match input in Regex.Matches(html, "<input\\b[^>]*>", HtmlOptions))
        {
            string tag = input.Value;
            if (!Regex.IsMatch(tag, "type=[\"']hidden[\"']", HtmlOptions))
            {
                continue;
            }
            string name = Regex.Match(tag, "name=[\"']([^\"']+)[\"']", HtmlOptions).Groups[1].Value;
            string value = Regex.Match(tag, "value=[\"']([^\"']*)[\"']", HtmlOptions).Groups[1].Value;
            if (!string.IsNullOrEmpty(name) && name is not "__EVENTTARGET" and not "__EVENTARGUMENT")
            {
                fields.Add(Pair(WebUtility.HtmlDecode(name), WebUtility.HtmlDecode(value)));
            }
        }
        return fields;
    }

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

    private static async Task<string> ReadAsync(HttpClient http, Uri uri,
        IEnumerable<KeyValuePair<string, string>>? fields, CancellationToken token)
    {
        using HttpResponseMessage response = fields is null
            ? await http.GetAsync(uri, token)
            : await http.PostAsync(uri, new FormUrlEncodedContent(fields), token);
        response.EnsureSuccessStatusCode();
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(token);
        string charset = response.Content.Headers.ContentType?.CharSet?.Trim('"') ?? "utf-8";
        return Encoding.GetEncoding(charset).GetString(bytes);
    }

    private static decimal? ParseDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number)
            ? number : null;

    private static DateTime? ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
            ? date : null;

    private static string CleanText(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), "\\s+", " ").Trim();

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);
}
