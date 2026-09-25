using System.Globalization;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Views.SettingsSections;

public sealed partial class DormElectricitySettingsSection
{
    private readonly TextBlock _pushTitle = new() { FontSize = 18 };
    private readonly TextBlock _pushDescription = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.72 };
    private readonly TextBlock _pushKeyLabel = new();
    private readonly TextBlock _pushKeyState = new() { Opacity = 0.7 };
    private readonly PasswordBox _pushKey = new() { MaxWidth = 400, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox _pushDaily = new();
    private readonly TextBlock _pushTimeLabel = new();
    private readonly TextBox _pushTime = new() { MaxLength = 5, Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox _pushLow = new();
    private readonly TextBlock _pushThresholdLabel = new();
    private readonly NumberBox _pushThreshold = new()
    {
        Minimum = 0, Maximum = 10000, SmallChange = 1,
        Width = 140, HorizontalAlignment = HorizontalAlignment.Left
    };
    private readonly Button _pushSave = new();
    private readonly Button _pushTest = new();
    private readonly Button _pushClear = new();
    private readonly TextBlock _pushStatus = new() { TextWrapping = TextWrapping.Wrap };
    private ICredentialStore? _pushCredentials;
    private readonly ServerChanService _pushSender = new();

    private FrameworkElement CreateNotificationSettingsView()
    {
        var root = new StackPanel { Spacing = 9, Margin = new Thickness(0, 18, 0, 0) };
        root.Children.Add(_pushTitle);
        root.Children.Add(_pushDescription);
        root.Children.Add(CreateField(_pushKeyLabel, _pushKey));
        root.Children.Add(_pushKeyState);
        root.Children.Add(_pushDaily);
        root.Children.Add(CreateField(_pushTimeLabel, _pushTime));
        root.Children.Add(_pushLow);
        root.Children.Add(CreateField(_pushThresholdLabel, _pushThreshold));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(_pushSave);
        actions.Children.Add(_pushTest);
        actions.Children.Add(_pushClear);
        root.Children.Add(actions);
        root.Children.Add(_pushStatus);
        _pushSave.Click += SaveNotification_Click;
        _pushTest.Click += TestNotification_Click;
        _pushClear.Click += ClearNotification_Click;
        return root;
    }

    private void InitializeNotificationSettings() =>
        _pushCredentials = App.Current.Services.GetRequiredService<ICredentialStore>();

    private async Task RefreshNotificationSettingsAsync(DormElectricitySettingsSlice options)
    {
        _pushDaily.IsChecked = options.DormElectricityServerChanDailyEnabled;
        _pushLow.IsChecked = options.DormElectricityServerChanLowEnabled;
        _pushTime.Text = $"{options.DormElectricityServerChanHour:00}:{options.DormElectricityServerChanMinute:00}";
        _pushThreshold.Value = (double)options.DormElectricityServerChanLowThresholdKwh;
        _pushKey.Password = string.Empty;
        if (_pushCredentials is null) return;
        try
        {
            string? key = await _pushCredentials.GetSecretAsync(DormElectricityNotificationService.CredentialKey);
            _pushKeyState.Text = T(string.IsNullOrWhiteSpace(key)
                ? "DormElectricity.Push.KeyMissing" : "DormElectricity.Push.KeySaved");
        }
        catch (Exception ex)
        {
            App.Log($"[DormElectricity] Credential status failed: {ex.GetType().Name}");
            _pushKeyState.Text = T("DormElectricity.Push.Failed");
        }
    }

    private void UpdateNotificationLabels()
    {
        _pushTitle.Text = T("DormElectricity.Push.Title");
        _pushDescription.Text = T("DormElectricity.Push.Description");
        _pushKeyLabel.Text = T("DormElectricity.Push.SendKey");
        _pushKey.PlaceholderText = T("DormElectricity.Push.KeyPlaceholder");
        _pushDaily.Content = T("DormElectricity.Push.Daily");
        _pushTimeLabel.Text = T("DormElectricity.Push.Time");
        _pushLow.Content = T("DormElectricity.Push.Low");
        _pushThresholdLabel.Text = T("DormElectricity.Push.Threshold");
        _pushSave.Content = T("DormElectricity.Push.Save");
        _pushTest.Content = T("DormElectricity.Push.Test");
        _pushClear.Content = T("DormElectricity.Push.Clear");
    }

    private bool TryReadNotificationOptions(out TimeOnly time, out double threshold)
    {
        threshold = 0;
        if (!TimeOnly.TryParseExact(_pushTime.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out time))
        {
            _pushStatus.Text = T("DormElectricity.Push.InvalidTime");
            return false;
        }
        if (!double.IsFinite(_pushThreshold.Value) || _pushThreshold.Value < 0 ||
            _pushThreshold.Value > 10000)
        {
            _pushStatus.Text = T("DormElectricity.Push.InvalidThreshold");
            return false;
        }
        threshold = _pushThreshold.Value;
        return true;
    }

    private async void SaveNotification_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null || _pushCredentials is null ||
            !TryReadNotificationOptions(out TimeOnly time, out double threshold)) return;

        bool daily = _pushDaily.IsChecked == true;
        bool low = _pushLow.IsChecked == true;
        string typedKey = _pushKey.Password.Trim();
        if (typedKey.Length > 0 && !ServerChanService.IsValidSendKey(typedKey))
        {
            _pushStatus.Text = T("DormElectricity.Push.InvalidKey");
            return;
        }
        _pushSave.IsEnabled = false;
        try
        {
            string? savedKey = typedKey.Length > 0 ? typedKey :
                await _pushCredentials.GetSecretAsync(DormElectricityNotificationService.CredentialKey);
            if ((daily || low) && !ServerChanService.IsValidSendKey(savedKey))
            {
                _pushStatus.Text = T("DormElectricity.Push.KeyMissing");
                return;
            }
            if (typedKey.Length > 0)
            {
                await _pushCredentials.SetSecretAsync(DormElectricityNotificationService.CredentialKey, typedKey);
            }
            DormElectricitySettingsSlice options = _settings.Settings.DormElectricity;
            options.DormElectricityServerChanDailyEnabled = daily;
            options.DormElectricityServerChanLowEnabled = low;
            options.DormElectricityServerChanHour = time.Hour;
            options.DormElectricityServerChanMinute = time.Minute;
            options.DormElectricityServerChanLowThresholdKwh = threshold;
            bool saved = await _settings.SaveCheckedAsync();
            _pushStatus.Text = T(saved ? "DormElectricity.Push.Saved" : "DormElectricity.Push.Failed");
            if (saved)
            {
                _pushKey.Password = string.Empty;
                _pushKeyState.Text = T(ServerChanService.IsValidSendKey(savedKey)
                    ? "DormElectricity.Push.KeySaved" : "DormElectricity.Push.KeyMissing");
                _ = App.Current.CheckDormNotificationNowAsync();
            }
        }
        catch (Exception ex)
        {
            App.Log($"[DormElectricity] Notification settings save failed: {ex.GetType().Name}");
            _pushStatus.Text = T("DormElectricity.Push.Failed");
        }
        finally
        {
            _pushSave.IsEnabled = true;
        }
    }

    private async void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        if (_pushCredentials is null) return;
        _pushTest.IsEnabled = false;
        try
        {
            string key = _pushKey.Password.Trim();
            if (key.Length == 0)
            {
                key = await _pushCredentials.GetSecretAsync(
                    DormElectricityNotificationService.CredentialKey) ?? string.Empty;
            }
            if (!ServerChanService.IsValidSendKey(key))
            {
                _pushStatus.Text = T("DormElectricity.Push.InvalidKey");
                return;
            }
            await _pushSender.SendAsync(key, "DeskBox 宿舍用电推送测试",
                "Server 酱已收到 DeskBox 的测试消息。宿舍电量提醒可以在格子设置中启用。");
            _pushStatus.Text = T("DormElectricity.Push.TestSent");
        }
        catch (Exception ex)
        {
            App.Log($"[DormElectricity] Notification test failed: {ex.GetType().Name}");
            _pushStatus.Text = T("DormElectricity.Push.SendFailed");
        }
        finally
        {
            _pushTest.IsEnabled = true;
        }
    }

    private async void ClearNotification_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null || _pushCredentials is null) return;
        _pushClear.IsEnabled = false;
        try
        {
            await _pushCredentials.RemoveSecretAsync(DormElectricityNotificationService.CredentialKey);
            DormElectricitySettingsSlice options = _settings.Settings.DormElectricity;
            options.DormElectricityServerChanDailyEnabled = false;
            options.DormElectricityServerChanLowEnabled = false;
            bool saved = await _settings.SaveCheckedAsync();
            _pushDaily.IsChecked = false;
            _pushLow.IsChecked = false;
            _pushKey.Password = string.Empty;
            _pushKeyState.Text = T("DormElectricity.Push.KeyMissing");
            _pushStatus.Text = T(saved ? "DormElectricity.Push.Cleared" : "DormElectricity.Push.Failed");
        }
        catch (Exception ex)
        {
            App.Log($"[DormElectricity] Notification credential removal failed: {ex.GetType().Name}");
            _pushStatus.Text = T("DormElectricity.Push.Failed");
        }
        finally
        {
            _pushClear.IsEnabled = true;
        }
    }
}
