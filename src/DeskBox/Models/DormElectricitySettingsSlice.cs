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
}
