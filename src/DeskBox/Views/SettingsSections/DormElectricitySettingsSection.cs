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
    private readonly ComboBox _floor = new() { MinWidth = 220 };
    private readonly ComboBox _roomSelect = new() { MinWidth = 220 };
    private readonly TextBox _room = new() { MaxLength = 20, MinWidth = 220 };
    private readonly ComboBox _usagePeriod = new() { MinWidth = 220 };
    private readonly ComboBox _paymentPeriod = new() { MinWidth = 220 };
    private readonly Button _save = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
    private readonly TextBlock _title = new() { FontSize = 22, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.72 };
    private readonly TextBlock _campusLabel = new();
    private readonly TextBlock _buildingLabel = new();
    private readonly TextBlock _floorLabel = new();
    private readonly TextBlock _roomSelectLabel = new();
    private readonly TextBlock _roomLabel = new();
    private readonly StackPanel _floorField;
    private readonly StackPanel _roomSelectField;
    private readonly StackPanel _roomInputField;
    private readonly TextBlock _usagePeriodLabel = new();
    private readonly TextBlock _paymentPeriodLabel = new();
    private SettingsService? _settings;
    private LocalizationService? _localization;
    private CancellationTokenSource? _buildingLoad;
    private CancellationTokenSource? _floorLoad;
    private CancellationTokenSource? _roomLoad;
    private bool _loading;

    public DormElectricitySettingsSection()
    {
        _floorField = CreateField(_floorLabel, _floor);
        _roomSelectField = CreateField(_roomSelectLabel, _roomSelect);
        _roomInputField = CreateField(_roomLabel, _room);
        var root = new StackPanel { Spacing = 12, Margin = new Thickness(4, 10, 4, 0) };
        root.Children.Add(_title);
        root.Children.Add(_description);
        root.Children.Add(CreateField(_campusLabel, _campus));
        root.Children.Add(CreateField(_buildingLabel, _building));
        root.Children.Add(_floorField);
        root.Children.Add(_roomSelectField);
        root.Children.Add(_roomInputField);
        root.Children.Add(CreateField(_usagePeriodLabel, _usagePeriod));
        root.Children.Add(CreateField(_paymentPeriodLabel, _paymentPeriod));
        root.Children.Add(_save);
        root.Children.Add(_status);
        Content = root;
        _campus.SelectionChanged += Campus_SelectionChanged;
        _building.SelectionChanged += Building_SelectionChanged;
        _floor.SelectionChanged += Floor_SelectionChanged;
        _save.Click += Save_Click;
        UpdateRoomMode();
    }

    public void Initialize(SettingsService settings, LocalizationService localization)
    {
        _settings = settings;
        _localization = localization;
        foreach (DormElectricityCampus campus in DormElectricityService.Campuses)
        {
            _campus.Items.Add(new ComboBoxItem { Content = campus.Name, Tag = campus.Client });
        }
        foreach (string period in DormElectricityPeriods.Options)
        {
            _usagePeriod.Items.Add(new ComboBoxItem { Tag = period });
            _paymentPeriod.Items.Add(new ComboBoxItem { Tag = period });
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
            _usagePeriod.SelectedItem = _usagePeriod.Items.OfType<ComboBoxItem>()
                .First(item => Equals(item.Tag, DormElectricityPeriods.Normalize(
                    settings.DormElectricity.DormElectricityUsagePeriod, DormElectricityPeriods.SevenDays)));
            _paymentPeriod.SelectedItem = _paymentPeriod.Items.OfType<ComboBoxItem>()
                .First(item => Equals(item.Tag, DormElectricityPeriods.Normalize(
                    settings.DormElectricity.DormElectricityPaymentPeriod, DormElectricityPeriods.OneYear)));
        }
        finally
        {
            _loading = false;
        }

        UpdateRoomMode();
        await LoadBuildingsAsync(settings.DormElectricity.DormElectricityBuildingId);
        if (IsLihuPhaseTwo)
        {
            await LoadFloorsAsync(settings.DormElectricity.DormElectricityFloorId);
            await LoadRoomsAsync(settings.DormElectricity.DormElectricityRoomId);
        }
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
        _floorLabel.Text = T("DormElectricity.Floor");
        _roomSelectLabel.Text = T("DormElectricity.Room");
        _roomLabel.Text = T("DormElectricity.Room");
        _usagePeriodLabel.Text = T("DormElectricity.UsagePeriod");
        _paymentPeriodLabel.Text = T("DormElectricity.PaymentPeriod");
        foreach (ComboBoxItem item in _usagePeriod.Items.OfType<ComboBoxItem>())
        {
            item.Content = T(DormElectricityPeriods.LabelKey((string)item.Tag));
        }
        foreach (ComboBoxItem item in _paymentPeriod.Items.OfType<ComboBoxItem>())
        {
            item.Content = T(DormElectricityPeriods.LabelKey((string)item.Tag));
        }
        _save.Content = T("DormElectricity.Save");
        _room.PlaceholderText = T("DormElectricity.RoomPlaceholder");
    }

    private async void Campus_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdateRoomMode();
            await LoadBuildingsAsync();
        }
    }

    private bool IsLihuPhaseTwo =>
        (_campus.SelectedItem as ComboBoxItem)?.Tag as string == DormElectricityService.LihuPhaseTwoClient;

    private void UpdateRoomMode()
    {
        Visibility lake = IsLihuPhaseTwo ? Visibility.Visible : Visibility.Collapsed;
        _floorField.Visibility = lake;
        _roomSelectField.Visibility = lake;
        _roomInputField.Visibility = IsLihuPhaseTwo ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Building_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && IsLihuPhaseTwo)
        {
            await LoadFloorsAsync();
        }
    }

    private async void Floor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && IsLihuPhaseTwo)
        {
            await LoadRoomsAsync();
        }
    }

    private async Task LoadBuildingsAsync(string? selectBuildingId = null)
    {
        _buildingLoad?.Cancel();
        _buildingLoad?.Dispose();
        _buildingLoad = new CancellationTokenSource();
        CancellationToken token = _buildingLoad.Token;
        _floorLoad?.Cancel();
        _roomLoad?.Cancel();
        _floor.Items.Clear();
        _roomSelect.Items.Clear();
        _floor.IsEnabled = false;
        _roomSelect.IsEnabled = false;
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
            _loading = true;
            try
            {
                foreach (DormElectricityBuilding building in buildings)
                {
                    _building.Items.Add(new ComboBoxItem { Content = building.Name, Tag = building.Id });
                }
                _building.SelectedItem = _building.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => Equals(item.Tag, selectBuildingId));
            }
            finally
            {
                _loading = false;
            }
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

    private async Task LoadFloorsAsync(string? selectedFloorId = null)
    {
        _floorLoad?.Cancel();
        _floorLoad?.Dispose();
        _floorLoad = new CancellationTokenSource();
        _roomLoad?.Cancel();
        _roomSelect.Items.Clear();
        _roomSelect.IsEnabled = false;
        _floor.Items.Clear();
        _floor.IsEnabled = false;
        if (!IsLihuPhaseTwo || _building.SelectedItem is not ComboBoxItem building)
        {
            return;
        }
        CancellationToken token = _floorLoad.Token;
        _status.Text = T("DormElectricity.LoadingFloors");
        try
        {
            IReadOnlyList<DormElectricityFloor> floors = await _service.GetFloorsAsync(
                DormElectricityService.LihuPhaseTwoClient, (string)building.Tag, token);
            if (token.IsCancellationRequested) return;
            _loading = true;
            try
            {
                foreach (DormElectricityFloor floor in floors)
                {
                    _floor.Items.Add(new ComboBoxItem { Content = floor.Name, Tag = floor.Id });
                }
                _floor.SelectedItem = _floor.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => Equals(item.Tag, selectedFloorId));
            }
            finally
            {
                _loading = false;
            }
            _floor.IsEnabled = true;
            _status.Text = string.Empty;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _status.Text = T("DormElectricity.NetworkError");
        }
    }

    private async Task LoadRoomsAsync(string? selectedRoomId = null)
    {
        _roomLoad?.Cancel();
        _roomLoad?.Dispose();
        _roomLoad = new CancellationTokenSource();
        _roomSelect.Items.Clear();
        _roomSelect.IsEnabled = false;
        if (!IsLihuPhaseTwo || _building.SelectedItem is not ComboBoxItem building ||
            _floor.SelectedItem is not ComboBoxItem floor)
        {
            return;
        }
        CancellationToken token = _roomLoad.Token;
        _status.Text = T("DormElectricity.LoadingRooms");
        try
        {
            IReadOnlyList<DormElectricityRoom> rooms = await _service.GetRoomsAsync(
                DormElectricityService.LihuPhaseTwoClient, (string)building.Tag,
                (string)floor.Tag, token);
            if (token.IsCancellationRequested) return;
            foreach (DormElectricityRoom room in rooms)
            {
                _roomSelect.Items.Add(new ComboBoxItem { Content = room.Name, Tag = room.Id });
            }
            _roomSelect.SelectedItem = _roomSelect.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => Equals(item.Tag, selectedRoomId));
            _roomSelect.IsEnabled = true;
            _status.Text = string.Empty;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
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
            (IsLihuPhaseTwo
                ? _floor.SelectedItem is not ComboBoxItem || _roomSelect.SelectedItem is not ComboBoxItem
                : string.IsNullOrWhiteSpace(_room.Text)))
        {
            _status.Text = T("DormElectricity.ConfigurationRequired");
            return;
        }

        AppSettings settings = _settings.Settings;
        settings.DormElectricity.DormElectricityClient = (string)campus.Tag;
        settings.DormElectricity.DormElectricityBuildingId = (string)building.Tag;
        settings.DormElectricity.DormElectricityBuildingName = building.Content?.ToString() ?? string.Empty;
        settings.DormElectricity.DormElectricityFloorId = IsLihuPhaseTwo
            ? (_floor.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty : string.Empty;
        settings.DormElectricity.DormElectricityRoomId = IsLihuPhaseTwo
            ? (_roomSelect.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty : string.Empty;
        settings.DormElectricity.DormElectricityRoomName = IsLihuPhaseTwo
            ? (_roomSelect.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty
            : _room.Text.Trim();
        settings.DormElectricity.DormElectricityUsagePeriod =
            (_usagePeriod.SelectedItem as ComboBoxItem)?.Tag as string ?? DormElectricityPeriods.SevenDays;
        settings.DormElectricity.DormElectricityPaymentPeriod =
            (_paymentPeriod.SelectedItem as ComboBoxItem)?.Tag as string ?? DormElectricityPeriods.OneYear;
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
