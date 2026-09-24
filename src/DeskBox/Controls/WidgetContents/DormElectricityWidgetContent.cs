using DeskBox.Contracts;
using DeskBox.Models;
using DeskBox.Services;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private readonly Grid _historyHost = new();
    private readonly Segmented _recordSelector = new()
    {
        MinHeight = 30,
        FontSize = 11,
        HorizontalAlignment = HorizontalAlignment.Right
    };
    private readonly TextBlock _usageSegmentText = new() { FontSize = 11, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _paymentSegmentText = new() { FontSize = 11, TextAlignment = TextAlignment.Center };
    private readonly Button _moreButton = new();
    private MenuFlyoutItem? _refreshMenuItem;
    private MenuFlyoutItem? _settingsMenuItem;
    private DormElectricitySnapshot? _snapshot;
    private string? _emptyMessage;
    private DormElectricityQuery? _lastQuery;
    private DateTime _lastRefreshAt;
    private bool _showPayments;
    private DormElectricityUsageLayout _usageLayout = DormElectricityUsageLayout.Vertical;
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
        SizeChanged += Widget_SizeChanged;
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

        DormElectricityQuery? query = GetQuery();
        if (query is null)
        {
            _snapshot = null;
            _emptyMessage = null;
            _lastQuery = null;
            _locationText.Text = string.Empty;
            _balanceText.Text = "—";
            _recordedAtText.Text = string.Empty;
            _statusText.Text = T("DormElectricity.ConfigurePrompt");
            RenderRows();
            return;
        }

        _isRefreshing = true;
        if (query != _lastQuery || _snapshot is null)
        {
            _snapshot = null;
            _lastQuery = null;
            _balanceText.Text = "—";
            _recordedAtText.Text = string.Empty;
            _emptyMessage = T("DormElectricity.Refreshing");
            RenderRows();
        }
        _locationText.Text = query.Location.Client == DormElectricityService.LihuPhaseTwoClient
            ? query.Location.RoomName
            : query.Location.BuildingName + " " + query.Location.RoomName;
        _statusText.Text = T("DormElectricity.Refreshing");
        try
        {
            DormElectricitySnapshot snapshot = await _service.GetSnapshotAsync(
                query.Location, DateTime.Now, query.UsagePeriod, query.PaymentPeriod);
            if (_disposed)
            {
                return;
            }
            if (GetQuery() != query)
            {
                _pendingRefresh = true;
                return;
            }
            _snapshot = snapshot;
            _emptyMessage = null;
            _lastQuery = query;
            _lastRefreshAt = DateTime.Now;
            DormElectricityDay? latest = snapshot.Days.LastOrDefault();
            decimal? balance = snapshot.RemainingKwh ?? latest?.RemainingKwh;
            DateTime? recordedAt = snapshot.BalanceRecordedAt ?? latest?.RecordedAt;
            _balanceText.Text = balance is null
                ? "—"
                : balance.Value.ToString("0.##") + " " + T("DormElectricity.KwhUnit");
            _recordedAtText.Text = recordedAt is null
                ? T("DormElectricity.NoUsageData")
                : _localization.Format("DormElectricity.BalanceRecordedAt",
                    recordedAt.Value.ToString(snapshot.BalanceRecordedAt is null
                        ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd"));
            _statusText.Text = _localization.Format(
                "DormElectricity.UpdatedAt", _lastRefreshAt.ToString("HH:mm"));
            UpdateLabels();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or ArgumentException)
        {
            if (GetQuery() != query)
            {
                _pendingRefresh = true;
                return;
            }
            string message = ex is HttpRequestException or TaskCanceledException
                ? T("DormElectricity.NetworkError")
                : ex.Message;
            _snapshot = null;
            _lastQuery = null;
            _balanceText.Text = "—";
            _recordedAtText.Text = string.Empty;
            _emptyMessage = message;
            _statusText.Text = message;
            RenderRows();
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
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new Grid { ColumnSpacing = 6 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _locationText.VerticalAlignment = VerticalAlignment.Center;
        _locationText.TextTrimming = TextTrimming.CharacterEllipsis;
        top.Children.Add(_locationText);
        _recordSelector.Style = (Style)Application.Current.Resources["PivotSegmentedStyle"];
        _recordSelector.Items.Add(new SegmentedItem
        {
            Content = _usageSegmentText,
            MinHeight = 30,
            Padding = new Thickness(6, 1, 6, 1)
        });
        _recordSelector.Items.Add(new SegmentedItem
        {
            Content = _paymentSegmentText,
            MinHeight = 30,
            Padding = new Thickness(6, 1, 6, 1)
        });
        _recordSelector.SelectedIndex = 0;
        _recordSelector.SelectionChanged += (_, _) =>
        {
            _showPayments = _recordSelector.SelectedIndex == 1;
            RenderRows();
        };
        Grid.SetColumn(_recordSelector, 1);
        top.Children.Add(_recordSelector);
        _moreButton.Style = (Style)Application.Current.Resources["WidgetTitleActionButtonStyle"];
        _moreButton.Content = new FontIcon { Glyph = "\uE712", FontSize = 16 };
        ToolTipService.SetToolTip(_moreButton, T("Widget.Tooltip.More"));
        var menu = new MenuFlyout();
        _refreshMenuItem = new MenuFlyoutItem
        {
            Icon = new FontIcon { Glyph = "\uE72C" }
        };
        _refreshMenuItem.Click += async (_, _) => await RefreshAsync();
        _settingsMenuItem = WidgetSettingsMenuHelper.CreateMenuItem(
            WidgetKind.DormElectricity, _localization);
        menu.Items.Add(_refreshMenuItem);
        menu.Items.Add(_settingsMenuItem);
        _moreButton.Flyout = menu;
        Grid.SetColumn(_moreButton, 2);
        top.Children.Add(_moreButton);
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

        Grid.SetRow(_historyHost, 2);
        root.Children.Add(_historyHost);
        Grid.SetRow(_statusText, 3);
        root.Children.Add(_statusText);
        return root;
    }

    private void UpdateLabels()
    {
        UpdateSegmentLabels(ActualWidth);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            _recordSelector, T("DormElectricity.RecordType"));
        _refreshMenuItem!.Text = T("DormElectricity.Refresh");
        _settingsMenuItem!.Text = T(WidgetSettingsMenuHelper.GetLocalizationKey(WidgetKind.DormElectricity));
        ToolTipService.SetToolTip(_moreButton, T("Widget.Tooltip.More"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            _moreButton, T("Widget.Tooltip.More"));
        if (Content is Grid root && root.Children.OfType<Border>().FirstOrDefault()?.Child is StackPanel stack &&
            stack.Children.FirstOrDefault() is TextBlock label)
        {
            label.Text = T("DormElectricity.Balance");
        }
        RenderRows();
    }

    private void UpdateSegmentLabels(double width)
    {
        bool compact = width > 0 && width < 230;
        _usageSegmentText.Text = T(compact ? "DormElectricity.UsageShort" : "DormElectricity.UsageTab");
        _paymentSegmentText.Text = T(compact ? "DormElectricity.PaymentsShort" : "DormElectricity.PaymentsTab");
        ToolTipService.SetToolTip(_recordSelector.Items[0] as UIElement, T("DormElectricity.UsageTab"));
        ToolTipService.SetToolTip(_recordSelector.Items[1] as UIElement, T("DormElectricity.PaymentsTab"));
    }

    private void Widget_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool shortWidget = e.NewSize.Height < 320;
        if (Content is Grid root)
        {
            root.RowSpacing = shortWidget ? 6 : 9;
        }
        _balanceText.FontSize = shortWidget ? 27 : 34;
        _recordedAtText.Visibility = shortWidget ? Visibility.Collapsed : Visibility.Visible;
        _statusText.Visibility = shortWidget ? Visibility.Collapsed : Visibility.Visible;
        _locationText.Visibility = e.NewSize.Width < 230 ? Visibility.Collapsed : Visibility.Visible;
        UpdateSegmentLabels(e.NewSize.Width);
        DormElectricityUsageLayout layout = GetUsageLayout(e.NewSize.Width, e.NewSize.Height);
        if (layout != _usageLayout)
        {
            _usageLayout = layout;
            if (!_showPayments)
            {
                RenderRows();
            }
        }
    }

    private static DormElectricityUsageLayout GetUsageLayout(double width, double height)
    {
        return width >= height * 1.3
            ? DormElectricityUsageLayout.Horizontal
            : DormElectricityUsageLayout.Vertical;
    }

    private void RenderRows()
    {
        _historyHost.Children.Clear();
        if (_snapshot is null)
        {
            _historyHost.Children.Add(new TextBlock
            {
                Text = _emptyMessage ?? T("DormElectricity.ConfigurePrompt"),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });
            return;
        }

        if (_showPayments)
        {
            _historyHost.Children.Add(CreatePaymentsView());
        }
        else if (_snapshot.Days.Count == 0)
        {
            _historyHost.Children.Add(new TextBlock { Text = T("DormElectricity.NoUsageData"), Opacity = 0.7 });
        }
        else
        {
            _historyHost.Children.Add(CreateCompactUsageView());
        }
    }

    private Grid CreateHistoryFrame(string title)
    {
        var frame = new Grid { RowSpacing = 6 };
        frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        frame.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            Opacity = 0.7,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        return frame;
    }

    private Grid CreatePaymentsView()
    {
        string period = GetQuery()?.PaymentPeriod ?? DormElectricityPeriods.OneYear;
        Grid frame = CreateHistoryFrame(T("DormElectricity.PaymentsTab") + " · " +
            T(DormElectricityPeriods.LabelKey(period)));
        var rows = new StackPanel { Spacing = 5 };
        AddRow(rows, T("DormElectricity.Date"), T("DormElectricity.Purchased"),
            T("DormElectricity.Amount"), header: true);
        foreach (DormElectricityPayment payment in _snapshot!.Payments)
        {
            AddRow(rows, payment.PaidAt.ToString("yyyy-MM-dd HH:mm"),
                payment.PurchasedKwh.ToString("0.##"), payment.AmountYuan.ToString("0.##"));
        }
        if (_snapshot.Payments.Count == 0)
        {
            rows.Children.Add(new TextBlock { Text = T("DormElectricity.NoPayments"), Opacity = 0.7 });
        }
        var scroll = new ScrollViewer
        {
            Content = rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        frame.Children.Add(scroll);
        return frame;
    }

    private Grid CreateCompactUsageView()
    {
        string period = GetQuery()?.UsagePeriod ?? DormElectricityPeriods.SevenDays;
        (DateTime start, DateTime end) = GetUsageDateRange();
        Dictionary<DateTime, DormElectricityDay> byDate = _snapshot!.Days
            .ToDictionary(day => day.RecordedAt.Date);
        Grid frame = CreateHistoryFrame(T("DormElectricity.DailyUsage") + " · " +
            T(DormElectricityPeriods.LabelKey(period)));
        bool horizontal = _usageLayout == DormElectricityUsageLayout.Horizontal;
        var items = new StackPanel
        {
            Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical,
            Spacing = 6
        };
        for (DateTime dateValue = end; dateValue >= start; dateValue = dateValue.AddDays(-1))
        {
            byDate.TryGetValue(dateValue, out DormElectricityDay? day);
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(18, 120, 120, 120)),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9, 6, 9, 6),
                Width = horizontal ? 82 : double.NaN
            };
            var date = new TextBlock
            {
                Text = dateValue.ToString("MM-dd"),
                FontSize = 11,
                Opacity = 0.7
            };
            var value = new TextBlock
            {
                Text = day?.UsedKwh?.ToString("0.##") ?? "—",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            if (horizontal)
            {
                var column = new StackPanel { Spacing = 1 };
                column.Children.Add(date);
                column.Children.Add(value);
                card.Child = column;
            }
            else
            {
                var row = new Grid { ColumnSpacing = 5 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(date);
                Grid.SetColumn(value, 1);
                row.Children.Add(value);
                card.Child = row;
            }
            ToolTipService.SetToolTip(card, dateValue.ToString("yyyy-MM-dd") + " · " +
                (day?.UsedKwh?.ToString("0.##") ?? "—") + " " + T("DormElectricity.KwhUnit"));
            items.Children.Add(card);
        }
        var scroll = new ScrollViewer
        {
            Content = items,
            HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
            HorizontalScrollMode = horizontal ? ScrollMode.Enabled : ScrollMode.Disabled,
            VerticalScrollMode = horizontal ? ScrollMode.Disabled : ScrollMode.Enabled
        };
        if (horizontal)
        {
            scroll.AddHandler(
                UIElement.PointerWheelChangedEvent,
                new PointerEventHandler(UsageScroll_PointerWheelChanged),
                handledEventsToo: true);
        }
        Grid.SetRow(scroll, 1);
        frame.Children.Add(scroll);
        return frame;
    }

    private static void UsageScroll_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ScrollViewer scroll || scroll.ScrollableWidth <= 0)
        {
            return;
        }
        int delta = e.GetCurrentPoint(scroll).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }
        double target = Math.Clamp(scroll.HorizontalOffset - delta * 1.2, 0, scroll.ScrollableWidth);
        if (Math.Abs(target - scroll.HorizontalOffset) < 0.5)
        {
            return;
        }
        scroll.ChangeView(target, null, null, disableAnimation: true);
        e.Handled = true;
    }

    private (DateTime Start, DateTime End) GetUsageDateRange()
    {
        DateTime today = _lastRefreshAt == default ? DateTime.Today : _lastRefreshAt.Date;
        string period = GetQuery()?.UsagePeriod ?? DormElectricityPeriods.SevenDays;
        return (DormElectricityPeriods.StartDate(period, today), today.AddDays(-1));
    }

    private void AddRow(StackPanel rows, string date, string middle, string right, bool header = false)
    {
        var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(3, 3, 3, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        AddCell(grid, date, 0, header);
        AddCell(grid, middle, 1, header);
        AddCell(grid, right, 2, header);
        rows.Children.Add(grid);
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

    private DormElectricityQuery? GetQuery()
    {
        AppSettings? settings = _settings?.Settings;
        if (settings is null || string.IsNullOrWhiteSpace(settings.DormElectricity.DormElectricityBuildingId) ||
            string.IsNullOrWhiteSpace(settings.DormElectricity.DormElectricityRoomName))
        {
            return null;
        }
        return new DormElectricityQuery(
            new DormElectricityLocation(
                settings.DormElectricity.DormElectricityClient,
                settings.DormElectricity.DormElectricityBuildingId,
                settings.DormElectricity.DormElectricityBuildingName,
                settings.DormElectricity.DormElectricityRoomName,
                settings.DormElectricity.DormElectricityFloorId,
                settings.DormElectricity.DormElectricityRoomId),
            DormElectricityPeriods.Normalize(settings.DormElectricity.DormElectricityUsagePeriod,
                DormElectricityPeriods.SevenDays),
            DormElectricityPeriods.Normalize(settings.DormElectricity.DormElectricityPaymentPeriod,
                DormElectricityPeriods.OneYear));
    }

    private string T(string key) => _localization.T(key);

    private void Timer_Tick(DispatcherQueueTimer sender, object args) => _ = RefreshAsync();

    private void Settings_SettingsChanged()
    {
        DormElectricityQuery? query = GetQuery();
        if (query != _lastQuery || _isRefreshing)
        {
            if (query != _lastQuery)
            {
                _snapshot = null;
                _balanceText.Text = "—";
                _recordedAtText.Text = string.Empty;
                _emptyMessage = query is null ? null : T("DormElectricity.Refreshing");
                _locationText.Text = query is null ? string.Empty
                    : query.Location.Client == DormElectricityService.LihuPhaseTwoClient
                        ? query.Location.RoomName
                        : query.Location.BuildingName + " " + query.Location.RoomName;
            }
            UpdateLabels();
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

    private sealed record DormElectricityQuery(
        DormElectricityLocation Location, string UsagePeriod, string PaymentPeriod);

    private enum DormElectricityUsageLayout
    {
        Vertical,
        Horizontal
    }
}
