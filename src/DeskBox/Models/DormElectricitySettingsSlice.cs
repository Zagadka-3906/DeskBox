namespace DeskBox.Models;

/// <summary>Location used by the dormitory electricity widget.</summary>
public sealed class DormElectricitySettingsSlice
{
    public string DormElectricityClient { get; set; } = "192.168.84.87";
    public string DormElectricityBuildingId { get; set; } = string.Empty;
    public string DormElectricityBuildingName { get; set; } = string.Empty;
    public string DormElectricityFloorId { get; set; } = string.Empty;
    public string DormElectricityRoomId { get; set; } = string.Empty;
    public string DormElectricityRoomName { get; set; } = string.Empty;
    public string DormElectricityUsagePeriod { get; set; } = DormElectricityPeriods.SevenDays;
    public string DormElectricityPaymentPeriod { get; set; } = DormElectricityPeriods.OneYear;
    public bool DormElectricityServerChanDailyEnabled { get; set; } = true;
    public bool DormElectricityServerChanLowEnabled { get; set; } = true;
    public int DormElectricityServerChanHour { get; set; } = 9;
    public int DormElectricityServerChanMinute { get; set; }
    public double DormElectricityServerChanLowThresholdKwh { get; set; } = 20;
    public string DormElectricityServerChanLastDailyStamp { get; set; } = string.Empty;
    public string DormElectricityServerChanLastLowStamp { get; set; } = string.Empty;
}
