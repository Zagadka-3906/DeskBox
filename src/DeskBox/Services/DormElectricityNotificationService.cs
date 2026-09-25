using System.Globalization;
using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>Checks the saved dorm independently of widget visibility and sends at most one daily alert of each kind.</summary>
internal sealed class DormElectricityNotificationService : IDisposable
{
    internal const string CredentialKey = "DormElectricity:ServerChanSendKey";

    private readonly SettingsService _settings;
    private readonly ICredentialStore _credentials;
    private readonly DormElectricityService _electricity = new();
    private readonly ServerChanService _serverChan = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationToken _stopToken;
    private DateTime _lastLowCheckAt;
    private string _lastCheckedLocation = string.Empty;
    private bool _disposed;

    public DormElectricityNotificationService(SettingsService settings, ICredentialStore credentials)
    {
        _settings = settings;
        _credentials = credentials;
        _stopToken = _stopping.Token;
    }

    private static string LocationKey(DormElectricityLocation location) =>
        location.Client + "|" + location.BuildingId + "|" + location.FloorId + "|" +
        location.RoomId + "|" + location.RoomName;

    internal static string Stamp(DateTime date, DormElectricityLocation location) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + LocationKey(location);

    internal static (bool Daily, bool CheckLow) Due(
        DormElectricitySettingsSlice options, DateTime now,
        DormElectricityLocation location, DateTime lastLowCheckAt)
    {
        string stamp = Stamp(now, location);
        bool daily = options.DormElectricityServerChanDailyEnabled &&
            now.TimeOfDay >= new TimeSpan(
                Math.Clamp(options.DormElectricityServerChanHour, 0, 23),
                Math.Clamp(options.DormElectricityServerChanMinute, 0, 59), 0) &&
            options.DormElectricityServerChanLastDailyStamp != stamp;
        bool low = options.DormElectricityServerChanLowEnabled &&
            options.DormElectricityServerChanLastLowStamp != stamp &&
            now - lastLowCheckAt >= TimeSpan.FromMinutes(30);
        return (daily, low);
    }

    public async Task CheckAsync(DateTime now)
    {
        if (_disposed || !await _gate.WaitAsync(0)) return;
        try
        {
            DormElectricitySettingsSlice options = _settings.Settings.DormElectricity;
            if ((!options.DormElectricityServerChanDailyEnabled &&
                 !options.DormElectricityServerChanLowEnabled) ||
                string.IsNullOrWhiteSpace(options.DormElectricityBuildingId) ||
                string.IsNullOrWhiteSpace(options.DormElectricityRoomName)) return;

            var location = new DormElectricityLocation(
                options.DormElectricityClient, options.DormElectricityBuildingId,
                options.DormElectricityBuildingName, options.DormElectricityRoomName,
                options.DormElectricityFloorId, options.DormElectricityRoomId);
            string stamp = Stamp(now, location);
            string locationKey = LocationKey(location);
            if (_lastCheckedLocation != locationKey)
            {
                _lastCheckedLocation = locationKey;
                _lastLowCheckAt = default;
            }
            (bool daily, bool checkLow) = Due(options, now, location, _lastLowCheckAt);
            if (!daily && !checkLow) return;

            string? sendKey = await _credentials.GetSecretAsync(CredentialKey, _stopToken);
            if (!ServerChanService.IsValidSendKey(sendKey)) return;

            if (checkLow) _lastLowCheckAt = now;
            DormElectricitySnapshot snapshot = await _electricity.GetSnapshotAsync(
                location, now, DormElectricityPeriods.SevenDays,
                DormElectricityPeriods.ThreeDays, _stopToken);
            if (Stamp(now, new DormElectricityLocation(
                    options.DormElectricityClient, options.DormElectricityBuildingId,
                    options.DormElectricityBuildingName, options.DormElectricityRoomName,
                    options.DormElectricityFloorId, options.DormElectricityRoomId)) != stamp) return;

            DormElectricityDay? latest = snapshot.Days.LastOrDefault();
            decimal? balance = snapshot.RemainingKwh ?? latest?.RemainingKwh;
            if (balance is null) return;
            daily &= options.DormElectricityServerChanDailyEnabled;
            bool low = checkLow && options.DormElectricityServerChanLowEnabled &&
                balance <= (decimal)options.DormElectricityServerChanLowThresholdKwh;
            if (!daily && !low) return;
            if (await _credentials.GetSecretAsync(CredentialKey, _stopToken) != sendKey) return;

            string room = location.Client == DormElectricityService.LihuPhaseTwoClient
                ? location.RoomName : location.BuildingName + " " + location.RoomName;
            string balanceText = balance.Value.ToString("0.##", CultureInfo.CurrentCulture);
            string title = low
                ? $"电量不足：剩余{balanceText}度｜{room}"
                : $"宿舍电量：剩余{balanceText}度｜{room}";
            if (title.Length > 32) title = title[..32];
            string details = $"宿舍：{room}\n\n剩余电量：{balanceText} 度";
            if (snapshot.Days.Count > 0)
            {
                details += "\n\n近 7 天用电记录：";
                foreach (DormElectricityDay day in snapshot.Days.TakeLast(7).Reverse())
                {
                    details += $"\n\n- {day.RecordedAt:MM-dd}：" +
                        (day.UsedKwh is decimal usage ? $"{usage:0.##} 度" : "暂无数据");
                }
            }
            if (snapshot.Payments.FirstOrDefault() is DormElectricityPayment payment)
            {
                details += $"\n\n最近购电：{payment.PaidAt:MM-dd HH:mm}，" +
                    $"{payment.PurchasedKwh:0.##} 度 / {payment.AmountYuan:0.##} 元";
            }
            if (low)
            {
                details += $"\n\n已低于设定的 {options.DormElectricityServerChanLowThresholdKwh:0.##} 度提醒值。";
            }
            details += $"\n\n查询时间：{now:yyyy-MM-dd HH:mm}";
            await _serverChan.SendAsync(sendKey!, title, details, _stopToken);

            if (daily) options.DormElectricityServerChanLastDailyStamp = stamp;
            if (low) options.DormElectricityServerChanLastLowStamp = stamp;
            if (!await _settings.SaveCheckedAsync(notifySubscribers: false))
            {
                App.Log("[DormElectricity] Notification sent, but delivery state could not be saved.");
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (Exception ex)
        {
            App.Log($"[DormElectricity] Notification check failed: {ex.GetType().Name}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopping.Cancel();
        _stopping.Dispose();
    }
}
