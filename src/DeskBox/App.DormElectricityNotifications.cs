using DeskBox.Services;
using Microsoft.UI.Xaml;

namespace DeskBox;

public partial class App
{
    private DispatcherTimer? _dormNotificationTimer;
    private DormElectricityNotificationService? _dormNotificationService;

    private void StartDormNotificationTimer()
    {
        _dormNotificationService = new DormElectricityNotificationService(
            SettingsService, Services.CredentialStore);
        _dormNotificationTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _dormNotificationTimer.Tick += (_, _) => _ = CheckDormNotificationNowAsync();
        _dormNotificationTimer.Start();
        SettingsService.SettingsChanged += OnDormNotificationSettingsChanged;
        _ = CheckDormNotificationNowAsync();
    }

    private void OnDormNotificationSettingsChanged()
    {
        UiDispatcherQueue.TryEnqueue(() => _ = CheckDormNotificationNowAsync());
    }

    private Task CheckDormNotificationNowAsync() =>
        _dormNotificationService?.CheckAsync(DateTime.Now) ?? Task.CompletedTask;

    private void StopNotificationServices()
    {
        SettingsService.SettingsChanged -= OnDormNotificationSettingsChanged;
        _dormNotificationTimer?.Stop();
        _dormNotificationTimer = null;
        _dormNotificationService?.Dispose();
        _dormNotificationService = null;
        _nativeNotificationService?.Dispose();
        _nativeNotificationService = null;
    }
}
