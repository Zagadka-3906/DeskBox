using System.Net;
using System.Text;
using System.Text.Json;
using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class DormElectricityNotificationTests
{
    [Fact]
    public void NotificationOptions_RoundTripWithoutSendKey()
    {
        var settings = new AppSettings
        {
            DormElectricityServerChanDailyEnabled = true,
            DormElectricityServerChanLowEnabled = true,
            DormElectricityServerChanHour = 21,
            DormElectricityServerChanMinute = 30,
            DormElectricityServerChanLowThresholdKwh = 12.5
        };

        string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        AppSettings? restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);

        Assert.NotNull(restored);
        Assert.Equal(21, restored.DormElectricityServerChanHour);
        Assert.Equal(30, restored.DormElectricityServerChanMinute);
        Assert.Equal(12.5, restored.DormElectricityServerChanLowThresholdKwh);
        Assert.DoesNotContain("SendKey", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Due_MergesDailyAndLowAlertsAndPreventsDuplicates()
    {
        var options = new DormElectricitySettingsSlice();
        var room = new DormElectricityLocation("192.168.84.87", "18120", "红豆斋", "333");
        DateTime now = new(2026, 9, 26, 9, 5, 0);

        Assert.Equal((true, true), DormElectricityNotificationService.Due(options, now, room, default));
        options.DormElectricityServerChanLastDailyStamp = DormElectricityNotificationService.Stamp(now, room);
        options.DormElectricityServerChanLastLowStamp = DormElectricityNotificationService.Stamp(now, room);
        Assert.Equal((false, false), DormElectricityNotificationService.Due(options, now, room, default));
        Assert.Equal((true, true), DormElectricityNotificationService.Due(options, now.AddDays(1), room, default));
    }

    [Fact]
    public void Due_RespectsReportTimeAndLowCheckInterval()
    {
        var options = new DormElectricitySettingsSlice();
        var room = new DormElectricityLocation("192.168.84.87", "18120", "红豆斋", "333");
        DateTime early = new(2026, 9, 26, 8, 30, 0);

        Assert.Equal((false, true), DormElectricityNotificationService.Due(options, early, room, default));
        Assert.Equal((false, false), DormElectricityNotificationService.Due(
            options, early.AddMinutes(5), room, early));
        Assert.Equal((true, false), DormElectricityNotificationService.Due(
            options, early.AddMinutes(30), room, early.AddMinutes(5)));
    }

    [Fact]
    public async Task ServerChan_PostsFormDataAndChecksBusinessCode()
    {
        var handler = new StubHandler("{\"code\":0,\"message\":\"success\"}");
        var sender = new ServerChanService(new HttpClient(handler));

        await sender.SendAsync("SCT1234567890", "用电测试", "剩余 12 度");

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://sctapi.ftqq.com/SCT1234567890.send", handler.Url);
        Assert.Contains("title=", handler.Body, StringComparison.Ordinal);
        Assert.Contains("desp=", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("SCT1234567890", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerChan_RejectsInvalidKeyAndBusinessFailure()
    {
        var sender = new ServerChanService(new HttpClient(new StubHandler("{\"code\":1001}")));

        await Assert.ThrowsAsync<ArgumentException>(() => sender.SendAsync("wrong", "test", "body"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync("SCT1234567890", "test", "body"));
    }

    private sealed class StubHandler(string responseJson) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Url { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Url = request.RequestUri?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
