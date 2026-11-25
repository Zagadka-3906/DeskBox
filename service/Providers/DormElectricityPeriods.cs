namespace DeskBox.Models;

public static class DormElectricityPeriods
{
    public const string ThreeDays = nameof(ThreeDays);
    public const string SevenDays = nameof(SevenDays);
    public const string OneMonth = nameof(OneMonth);
    public const string SixMonths = nameof(SixMonths);
    public const string OneYear = nameof(OneYear);

    public static IReadOnlyList<string> Options { get; } =
        [ThreeDays, SevenDays, OneMonth, SixMonths, OneYear];

    public static string Normalize(string? value, string fallback) =>
        Options.Contains(value, StringComparer.Ordinal) ? value! : fallback;

    public static DateTime StartDate(string period, DateTime today)
    {
        DateTime date = today.Date;
        return period switch
        {
            ThreeDays => date.AddDays(-3),
            SevenDays => date.AddDays(-7),
            OneMonth => date.AddMonths(-1),
            SixMonths => date.AddMonths(-6),
            OneYear => date.AddYears(-1),
            _ => throw new ArgumentOutOfRangeException(nameof(period))
        };
    }

    public static string LabelKey(string period) =>
        "DormElectricity.Period." + period;
}
