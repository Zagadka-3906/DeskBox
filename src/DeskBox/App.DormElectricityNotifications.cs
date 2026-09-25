using DeskBox.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace DeskBox;

public partial class App
{
    private DispatcherTimer? _dormNotificationTimer;
    private DormElectricityNotificationService? _dormNotificationService;

    private void StartDormNotificationTimer()
    {
        _dormNotificationService = new DormElectricityNotificationService(
            SettingsService, Services.GetRequiredService<ICredentialStore>());
        _dormNotificationTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _dormNotificationTimer.Tick += (_, _) => _ = CheckDormNotificationNowAsync();
        _dormNotificationTimer.Start();
        _ = CheckDormNotificationNowAsync();
    }

    internal Task CheckDormNotificationNowAsync() =>
        _dormNotificationService?.CheckAsync(DateTime.Now) ?? Task.CompletedTask;

    private void StopDormNotificationTimer()
    {
        _dormNotificationTimer?.Stop();
        _dormNotificationTimer = null;
        _dormNotificationService?.Dispose();
        _dormNotificationService = null;
    }
}
