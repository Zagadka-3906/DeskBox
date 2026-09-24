using DeskBox.Contracts;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class DormElectricityWidgetContent : UserControl, IWidgetContent, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly SettingsService? _settings;
    private readonly DormElectricityService _service = new();
    private readonly DispatcherQueueTimer? _timer;
    private readonly TextBlock _locationText = new() { FontSize = 12, Opacity = 0.75 };
    private readonly TextBlock _balanceText = new() { FontSize = 34, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _recordedAtText = new() { FontSize = 11, Opacity = 0.65 };
    private readonly TextBlock _statusText = new() { FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _rows = new() { Spacing = 5 };
    private readonly Button _usageButton = new();
    private readonly Button _paymentsButton = new();
    private DormElectricitySnapshot? _snapshot;
    private DormElectricityLocation? _lastLocation;
    private DateTime _lastRefreshAt;
    private bool _showPayments;
    private bool _isRefreshing;
    private bool _pendingRefresh;
    private bool _disposed;

    public DormElectricityWidgetContent(
        WidgetConfig config,
        LocalizationService localization,
        SettingsService? settings)
    {
        Config = config;
        _localization = localization;
        _settings = settings;
        Content = CreateView();

        DispatcherQueue? queue = DispatcherQueue.GetForCurrentThread();
        if (queue is not null)
        {
            _timer = queue.CreateTimer();
            _timer.Interval = TimeSpan.FromMinutes(60);
            _timer.IsRepeating = true;
            _timer.Tick += Timer_Tick;
        }
        if (_settings is not null)
        {
            _settings.SettingsChanged += Settings_SettingsChanged;
        }
        _localization.LanguageChanged += Localization_LanguageChanged;
        UpdateLabels();
    }

    public WidgetConfig Config { get; }
    public string WidgetId => Config.Id;
    public WidgetKind WidgetKind => Config.WidgetKind;
    public FrameworkElement View => this;

    public Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        if (_disposed)
        {
            return;
        }
        if (_isRefreshing)
        {
            _pendingRefresh = true;
            return;
        }

        DormElectricityLocation? location = GetLocation();
        if (location is null)
        {
            _snapshot = null;
            _lastLocation = null;
            _locationText.Text = string.Empty;
            _balanceText.Text = "—";
            _recordedAtText.Text = string.Empty;
            _statusText.Text = T("DormElectricity.ConfigurePrompt");
            RenderRows();
            return;
        }

        _isRefreshing = true;
        _locationText.Text = location.BuildingName + " " + location.RoomName;
        _statusText.Text = T("DormElectricity.Refreshing");
        try
        {
            DormElectricitySnapshot snapshot = await _service.GetSnapshotAsync(
                location, DateTime.Now);
            if (_disposed)
            {
                return;
            }
            if (GetLocation() != location)
            {
                _pendingRefresh = true;
                return;
            }
            _snapshot = snapshot;
            _lastLocation = location;
            _lastRefreshAt = DateTime.Now;
            DormElectricityDay? latest = snapshot.Days.LastOrDefault();
            _balanceText.Text = latest is null
                ? "—"
                : latest.RemainingKwh.ToString("0.##") + " " + T("DormElectricity.KwhUnit");
            _recordedAtText.Text = latest is null
                ? T("DormElectricity.NoUsageData")
                : _localization.Format("DormElectricity.BalanceRecordedAt",
                    latest.RecordedAt.ToString("yyyy-MM-dd HH:mm"));
            _statusText.Text = _localization.Format(
                "DormElectricity.UpdatedAt", _lastRefreshAt.ToString("HH:mm"));
            RenderRows();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _statusText.Text = ex is HttpRequestException or TaskCanceledException
                ? T("DormElectricity.NetworkError")
                : ex.Message;
        }
        finally
        {
            _isRefreshing = false;
            if (_pendingRefresh && !_disposed)
            {
                _pendingRefresh = false;
                _ = RefreshAsync();
            }
        }
    }

    public void ApplyAppearance() { }
    public void OnActivated() { }
    public void OnDeactivated() { }

    public void OnWindowVisibilityChanged(bool visible)
    {
        if (visible)
        {
            _timer?.Start();
            if (DateTime.Now - _lastRefreshAt > TimeSpan.FromMinutes(60))
            {
                _ = RefreshAsync();
            }
        }
        else
        {
            _timer?.Stop();
        }
    }

    private FrameworkElement CreateView()
    {
        var root = new Grid { Padding = new Thickness(14, 11, 14, 11), RowSpacing = 9 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new Grid { ColumnSpacing = 6 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _locationText.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(_locationText);
        var settingsButton = new Button { Content = "⚙", Padding = new Thickness(7, 3, 7, 3) };
        ToolTipService.SetToolTip(settingsButton, T("DormElectricity.Settings"));
        settingsButton.Click += (_, _) => App.Current.ShowSettings("DormElectricitySettings");
        Grid.SetColumn(settingsButton, 1);
        top.Children.Add(settingsButton);
        var refreshButton = new Button { Content = "↻", Padding = new Thickness(7, 3, 7, 3) };
        ToolTipService.SetToolTip(refreshButton, T("DormElectricity.Refresh"));
        refreshButton.Click += async (_, _) => await RefreshAsync();
        Grid.SetColumn(refreshButton, 2);
        top.Children.Add(refreshButton);
        root.Children.Add(top);

        var balanceCard = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(20, 120, 120, 120)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 9)
        };
        var balanceStack = new StackPanel { Spacing = 1 };
        var balanceLabel = new TextBlock { Name = "BalanceLabel", FontSize = 12, Opacity = 0.7 };
        balanceStack.Children.Add(balanceLabel);
        balanceStack.Children.Add(_balanceText);
        balanceStack.Children.Add(_recordedAtText);
        balanceCard.Child = balanceStack;
        Grid.SetRow(balanceCard, 1);
        root.Children.Add(balanceCard);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _usageButton.Click += (_, _) => { _showPayments = false; RenderRows(); };
        _paymentsButton.Click += (_, _) => { _showPayments = true; RenderRows(); };
        tabs.Children.Add(_usageButton);
        tabs.Children.Add(_paymentsButton);
        Grid.SetRow(tabs, 2);
        root.Children.Add(tabs);

        var scroll = new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 3);
        root.Children.Add(scroll);
        Grid.SetRow(_statusText, 4);
        root.Children.Add(_statusText);
        return root;
    }

    private void UpdateLabels()
    {
        _usageButton.Content = T("DormElectricity.UsageTab");
        _paymentsButton.Content = T("DormElectricity.PaymentsTab");
        if (Content is Grid root && root.Children.OfType<Border>().FirstOrDefault()?.Child is StackPanel stack &&
            stack.Children.FirstOrDefault() is TextBlock label)
        {
            label.Text = T("DormElectricity.Balance");
        }
        RenderRows();
    }

    private void RenderRows()
    {
        _usageButton.Opacity = _showPayments ? 0.6 : 1;
        _paymentsButton.Opacity = _showPayments ? 1 : 0.6;
        _rows.Children.Clear();
        if (_snapshot is null)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = T("DormElectricity.ConfigurePrompt"),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });
            return;
        }

        if (_showPayments)
        {
            AddHeader(T("DormElectricity.Date"), T("DormElectricity.Purchased"), T("DormElectricity.Amount"));
            foreach (DormElectricityPayment payment in _snapshot.Payments)
            {
                AddRow(payment.PaidAt.ToString("yyyy-MM-dd HH:mm"),
                    payment.PurchasedKwh.ToString("0.##"),
                    payment.AmountYuan.ToString("0.##"));
            }
            if (_snapshot.Payments.Count == 0)
            {
                _rows.Children.Add(new TextBlock { Text = T("DormElectricity.NoPayments"), Opacity = 0.7 });
            }
        }
        else
        {
            AddHeader(T("DormElectricity.Date"), T("DormElectricity.Used"), T("DormElectricity.Remaining"));
            foreach (DormElectricityDay day in _snapshot.Days.Reverse())
            {
                AddRow(day.RecordedAt.ToString("MM-dd"),
                    day.UsedKwh?.ToString("0.##") ?? "—",
                    day.RemainingKwh.ToString("0.##"));
            }
            if (_snapshot.Days.Count == 0)
            {
                _rows.Children.Add(new TextBlock { Text = T("DormElectricity.NoUsageData"), Opacity = 0.7 });
            }
        }
    }

    private void AddHeader(string date, string middle, string right)
    {
        AddRow(date, middle, right, header: true);
    }

    private void AddRow(string date, string middle, string right, bool header = false)
    {
        var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(3, 3, 3, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        AddCell(grid, date, 0, header);
        AddCell(grid, middle, 1, header);
        AddCell(grid, right, 2, header);
        _rows.Children.Add(grid);
    }

    private static void AddCell(Grid grid, string value, int column, bool header)
    {
        var text = new TextBlock
        {
            Text = value,
            FontSize = header ? 11 : 12,
            FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
            Opacity = header ? 0.6 : 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right
        };
        Grid.SetColumn(text, column);
        grid.Children.Add(text);
    }

    private DormElectricityLocation? GetLocation()
    {
        AppSettings? settings = _settings?.Settings;
        if (settings is null || string.IsNullOrWhiteSpace(settings.DormElectricity.DormElectricityBuildingId) ||
            string.IsNullOrWhiteSpace(settings.DormElectricity.DormElectricityRoomName))
        {
            return null;
        }
        return new DormElectricityLocation(
            settings.DormElectricity.DormElectricityClient,
            settings.DormElectricity.DormElectricityBuildingId,
            settings.DormElectricity.DormElectricityBuildingName,
            settings.DormElectricity.DormElectricityRoomName);
    }

    private string T(string key) => _localization.T(key);

    private void Timer_Tick(DispatcherQueueTimer sender, object args) => _ = RefreshAsync();

    private void Settings_SettingsChanged()
    {
        DormElectricityLocation? location = GetLocation();
        if (location != _lastLocation || _isRefreshing)
        {
            _ = RefreshAsync();
        }
    }

    private void Localization_LanguageChanged() => UpdateLabels();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer?.Stop();
        if (_settings is not null)
        {
            _settings.SettingsChanged -= Settings_SettingsChanged;
        }
        _localization.LanguageChanged -= Localization_LanguageChanged;
    }
}
