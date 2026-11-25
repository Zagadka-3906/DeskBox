using System.Text.Json;
using DeskBox.Models;
using DeskBox.Services;

namespace DormElectricityBridge;

internal sealed record BridgeSettings(
    DormElectricityLocation Location, string UsagePeriod, string PaymentPeriod,
    bool Daily, bool Low, TimeOnly DailyTime, decimal LowThreshold)
{
    internal static BridgeSettings FromLegacy(DormElectricitySettingsSlice old) => new(
        new(old.DormElectricityClient, old.DormElectricityBuildingId, old.DormElectricityBuildingName,
            old.DormElectricityRoomName, old.DormElectricityFloorId, old.DormElectricityRoomId),
        DormElectricityPeriods.Normalize(old.DormElectricityUsagePeriod, DormElectricityPeriods.SevenDays),
        DormElectricityPeriods.Normalize(old.DormElectricityPaymentPeriod, DormElectricityPeriods.OneYear),
        old.DormElectricityServerChanDailyEnabled,
        old.DormElectricityServerChanLowEnabled,
        new TimeOnly(Math.Clamp(old.DormElectricityServerChanHour, 0, 23),
            Math.Clamp(old.DormElectricityServerChanMinute, 0, 59)),
        double.IsFinite(old.DormElectricityServerChanLowThresholdKwh)
            ? (decimal)Math.Clamp(old.DormElectricityServerChanLowThresholdKwh, 0, 10000) : 20);
}

internal sealed record PushHistory(string DailyStamp = "", string LowStamp = "");
internal sealed record BridgeState(BridgeSettings Settings, PushHistory History);

internal sealed class BridgeStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _file;
    private readonly object _sync = new();
    private BridgeState _state;

    public BridgeStore(string root, string? legacyFile)
    {
        Directory.CreateDirectory(root);
        _file = Path.Combine(root, "settings.json");
        if (File.Exists(_file))
        {
            _state = JsonSerializer.Deserialize<BridgeState>(File.ReadAllText(_file), Json)
                ?? throw new InvalidDataException("本机服务设置文件为空。");
            return;
        }

        var legacy = legacyFile is not null
            ? JsonSerializer.Deserialize<DormElectricitySettingsSlice>(File.ReadAllText(legacyFile), Json)
            : null;
        legacy ??= new DormElectricitySettingsSlice();
        _state = new BridgeState(BridgeSettings.FromLegacy(legacy),
            new PushHistory(legacy.DormElectricityServerChanLastDailyStamp, legacy.DormElectricityServerChanLastLowStamp));
        Save(_state);
    }

    public BridgeState Read() { lock (_sync) return _state; }

    public void UpdateSettings(BridgeSettings settings)
    {
        lock (_sync) Save(_state with { Settings = settings });
    }

    public void RecordPush(string stamp, bool daily, bool low)
    {
        lock (_sync)
        {
            var history = _state.History;
            Save(_state with
            {
                History = new PushHistory(daily ? stamp : history.DailyStamp, low ? stamp : history.LowStamp)
            });
        }
    }

    private void Save(BridgeState state)
    {
        string temp = _file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
        File.Move(temp, _file, overwrite: true);
        _state = state;
    }
}
