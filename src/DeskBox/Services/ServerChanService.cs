using System.Text.Json;
using System.Text.RegularExpressions;

namespace DeskBox.Services;

/// <summary>Sends a notification through ServerChan Turbo without logging its SendKey.</summary>
internal sealed class ServerChanService
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient _http;

    public ServerChanService(HttpClient? http = null) => _http = http ?? SharedHttp;

    public static bool IsValidSendKey(string? sendKey) =>
        sendKey is not null && Regex.IsMatch(sendKey, @"^SCT[A-Za-z0-9_-]{6,}$", RegexOptions.CultureInvariant);

    public async Task SendAsync(string sendKey, string title, string description,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidSendKey(sendKey))
        {
            throw new ArgumentException("请填写 Server 酱 Turbo 的 SendKey（以 SCT 开头）。", nameof(sendKey));
        }

        if (string.IsNullOrWhiteSpace(title) || title.Contains('\n') || title.Contains('\r'))
        {
            throw new ArgumentException("推送标题不能为空或包含换行。", nameof(title));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri($"https://sctapi.ftqq.com/{Uri.EscapeDataString(sendKey)}.send"))
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("title", title),
                new KeyValuePair<string, string>("desp", description)
            ])
        };
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Server 酱未接受推送请求，请检查网络和 SendKey。");
        }

        try
        {
            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            if (!json.RootElement.TryGetProperty("code", out JsonElement code) ||
                code.ValueKind != JsonValueKind.Number || code.GetInt32() != 0)
            {
                throw new InvalidOperationException("Server 酱推送失败，请检查 SendKey、消息通道或当日额度。");
            }
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Server 酱返回了无法识别的结果，请稍后重试。");
        }
    }
}
