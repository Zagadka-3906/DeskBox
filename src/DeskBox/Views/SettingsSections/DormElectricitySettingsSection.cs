using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Text;

namespace DeskBox.Views.SettingsSections;

public sealed partial class DormElectricitySettingsSection : UserControl
{
    private readonly DormElectricityService _service = new();
    private readonly ComboBox _campus = new() { MinWidth = 220 };
    private readonly ComboBox _building = new() { MinWidth = 220 };
    private readonly TextBox _room = new() { MaxLength = 20, MinWidth = 220 };
    private readonly Button _save = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
    private readonly TextBlock _title = new() { FontSize = 22, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.72 };
    private readonly TextBlock _campusLabel = new();
    private readonly TextBlock _buildingLabel = new();
    private readonly TextBlock _roomLabel = new();
    private SettingsService? _settings;
    private LocalizationService? _localization;
    private CancellationTokenSource? _buildingLoad;
    private bool _loading;

    public DormElectricitySettingsSection()
    {
        var root = new StackPanel { Spacing = 12, Margin = new Thickness(4, 10, 4, 0) };
        root.Children.Add(_title);
        root.Children.Add(_description);
        root.Children.Add(CreateField(_campusLabel, _campus));
        root.Children.Add(CreateField(_buildingLabel, _building));
        root.Children.Add(CreateField(_roomLabel, _room));
        root.Children.Add(_save);
        root.Children.Add(_status);
        Content = root;
        _campus.SelectionChanged += Campus_SelectionChanged;
        _save.Click += Save_Click;
    }

    public void Initialize(SettingsService settings, LocalizationService localization)
    {
        _settings = settings;
        _localization = localization;
        foreach (DormElectricityCampus campus in DormElectricityService.Campuses)
        {
            _campus.Items.Add(new ComboBoxItem { Content = campus.Name, Tag = campus.Client });
        }
        _localization.LanguageChanged += Localization_LanguageChanged;
        UpdateLabels();
    }

    public async Task RefreshFromSettingsAsync()
    {
        if (_settings is null)
        {
            return;
        }

        AppSettings settings = _settings.Settings;
        _loading = true;
        try
        {
            _campus.SelectedItem = _campus.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => Equals(item.Tag, settings.DormElectricity.DormElectricityClient))
                ?? _campus.Items.OfType<ComboBoxItem>().LastOrDefault();
            _room.Text = settings.DormElectricity.DormElectricityRoomName;
        }
        finally
        {
            _loading = false;
        }

        await LoadBuildingsAsync(settings.DormElectricity.DormElectricityBuildingId);
    }

    private static StackPanel CreateField(TextBlock label, Control input)
    {
        var field = new StackPanel { Spacing = 5 };
        field.Children.Add(label);
        field.Children.Add(input);
        return field;
    }

    private string T(string key) => _localization?.T(key) ?? key;

    private void UpdateLabels()
    {
        _title.Text = T("Settings.DormElectricity.Title");
        _description.Text = T("Settings.DormElectricity.Description");
        _campusLabel.Text = T("DormElectricity.Campus");
        _buildingLabel.Text = T("DormElectricity.Building");
        _roomLabel.Text = T("DormElectricity.Room");
        _save.Content = T("DormElectricity.Save");
        _room.PlaceholderText = T("DormElectricity.RoomPlaceholder");
    }

    private async void Campus_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            await LoadBuildingsAsync();
        }
    }

    private async Task LoadBuildingsAsync(string? selectBuildingId = null)
    {
        _buildingLoad?.Cancel();
        _buildingLoad?.Dispose();
        _buildingLoad = new CancellationTokenSource();
        CancellationToken token = _buildingLoad.Token;
        _building.Items.Clear();
        _building.IsEnabled = false;
        string? client = (_campus.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(client))
        {
            return;
        }

        _status.Text = T("DormElectricity.LoadingBuildings");
        try
        {
            IReadOnlyList<DormElectricityBuilding> buildings =
                await _service.GetBuildingsAsync(client, token);
            if (token.IsCancellationRequested)
            {
                return;
            }
            foreach (DormElectricityBuilding building in buildings)
            {
                _building.Items.Add(new ComboBoxItem { Content = building.Name, Tag = building.Id });
            }
            _building.SelectedItem = _building.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => Equals(item.Tag, selectBuildingId));
            _building.IsEnabled = true;
            _status.Text = string.Empty;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _status.Text = T("DormElectricity.NetworkError");
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }
        if (_campus.SelectedItem is not ComboBoxItem campus ||
            _building.SelectedItem is not ComboBoxItem building ||
            string.IsNullOrWhiteSpace(_room.Text))
        {
            _status.Text = T("DormElectricity.ConfigurationRequired");
            return;
        }

        AppSettings settings = _settings.Settings;
        settings.DormElectricity.DormElectricityClient = (string)campus.Tag;
        settings.DormElectricity.DormElectricityBuildingId = (string)building.Tag;
        settings.DormElectricity.DormElectricityBuildingName = building.Content?.ToString() ?? string.Empty;
        settings.DormElectricity.DormElectricityRoomName = _room.Text.Trim();
        _save.IsEnabled = false;
        try
        {
            bool saved = await _settings.SaveCheckedAsync();
            _status.Text = T(saved ? "DormElectricity.Saved" : "DormElectricity.SaveFailed");
        }
        catch (Exception ex)
        {
            App.Log($"[DormElectricity] Could not save settings: {ex}");
            _status.Text = T("DormElectricity.SaveFailed");
        }
        finally
        {
            _save.IsEnabled = true;
        }
    }

    private void Localization_LanguageChanged() => UpdateLabels();
}
