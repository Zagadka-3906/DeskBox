using System.Globalization;
using DeskBox.Models;
using DeskBox.Services;

namespace DormElectricityBridge;

internal sealed class PushWorker(
    BridgeStore store, ICredentialStore credentials, BridgeQueries queries, ServerChanService sender) : BackgroundService
{
    internal const string CredentialKey = "DormElectricity:ServerChanSendKey";
    private DormElectricityLocation? _checkedLocation;
    private DateTime _lastLowCheck;
    private string _sentDaily = "";
    private string _sentLow = "";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try { await CheckAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { Console.Error.WriteLine($"推送检查失败 ({ex.GetType().Name})"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckAsync(CancellationToken token)
    {
        var (settings, history) = store.Read();
        if ((!settings.Daily && !settings.Low) || string.IsNullOrWhiteSpace(settings.Location.RoomName)) return;
        string? key = await credentials.GetSecretAsync(CredentialKey, token);
        if (!ServerChanService.IsValidSendKey(key)) return;

        var location = settings.Location;
        DateTime now = DateTime.Now;
        if (location != _checkedLocation)
        {
            _checkedLocation = location;
            _lastLowCheck = default;
        }

        string stamp = string.Join("|", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            location.Client, location.BuildingId, location.FloorId, location.RoomId, location.RoomName);
        bool daily = settings.Daily && TimeOnly.FromDateTime(now) >= settings.DailyTime &&
            stamp != history.DailyStamp && stamp != _sentDaily;
        bool checkLow = settings.Low && stamp != history.LowStamp && stamp != _sentLow &&
            now - _lastLowCheck >= TimeSpan.FromMinutes(30);
        if (!daily && !checkLow) return;

        var snapshot = await queries.SnapshotAsync(settings with
        {
            UsagePeriod = DormElectricityPeriods.SevenDays, PaymentPeriod = DormElectricityPeriods.ThreeDays
        }, token);
        if (checkLow) _lastLowCheck = now;
        decimal? balance = snapshot.RemainingKwh ?? snapshot.Days.LastOrDefault()?.RemainingKwh;
        if (balance is null) return;
        bool low = checkLow && balance.Value <= settings.LowThreshold;
        if (!daily && !low) return;
        if (store.Read().Settings != settings || await credentials.GetSecretAsync(CredentialKey, token) != key) return;

        string room = BridgeQueries.RoomLabel(location);
        string title = $"{(low ? "电量不足" : "宿舍电量")}：剩余{BridgeQueries.Format(balance.Value)}度｜{room}";
        if (title.Length > 32) title = title[..32];
        string records = string.Join("\n\n", BridgeQueries.DailyRows(snapshot, DormElectricityPeriods.SevenDays, now)
            .Select(row => $"- {row.Date}：{row.Value}"));
        string payment = snapshot.Payments.FirstOrDefault() is { } last
            ? $"\n\n最近购电：{last.PaidAt:MM-dd HH:mm}，{BridgeQueries.Format(last.PurchasedKwh)} 度 / {BridgeQueries.Format(last.AmountYuan)} 元"
            : "";
        string body = $"宿舍：{room}\n\n剩余电量：{BridgeQueries.Format(balance.Value)} 度\n\n近 7 天用电记录\n\n{records}{payment}";
        if (low) body += $"\n\n提醒阈值：{BridgeQueries.Format(settings.LowThreshold)} 度";
        await sender.SendAsync(key!, title, body, token);
        if (daily) _sentDaily = stamp;
        if (low) _sentLow = stamp;
        store.RecordPush(stamp, daily, low);
    }
}
