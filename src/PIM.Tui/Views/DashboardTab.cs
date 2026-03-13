using PIM.Core.Models;
using PIM.Tui.Client;
using PIM.Tui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PIM.Tui.Views;

internal sealed class DashboardTab : View
{
    private readonly PimApiClient _api;
    private readonly TuiApp _app;
    private readonly TihApiClient? _tihClient;

    private readonly FrameView _agendaFrame;
    private readonly FrameView _infoFrame;
    private readonly FrameView _tihFrame;
    private readonly FrameView _mailFrame;

    private readonly AgendaListView _agendaList;
    private readonly Label _currentWeatherLabel;
    private readonly Label _forecastLabel;
    private readonly TodayInHistoryView _tihView;
    private readonly AccountListView _accountList;
    private readonly EmailListView _recentMailList;

    private List<CalendarEvent> _todayEvents = [];
    private List<AccountOverview> _accounts = [];
    private List<EmailHeader> _recentMail = [];

    public DashboardTab(PimApiClient api, TuiApp app, TihApiClient? tihClient = null)
    {
        _api = api;
        _app = app;
        _tihClient = tihClient;
        CanFocus = true;
        X = 0; Y = 0; Width = Dim.Fill(); Height = Dim.Fill();

        _agendaFrame = new FrameView
        {
            Title = "Agenda",
            X = 0, Y = 0,
            Width = Dim.Percent(30),
            Height = Dim.Fill()
        };

        _agendaList = new AgendaListView(app)
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _agendaFrame.Add(_agendaList);

        _infoFrame = new FrameView
        {
            Title = "Weather",
            X = Pos.Right(_agendaFrame),
            Y = 0,
            Width = Dim.Percent(30),
            Height = 8
        };

        _currentWeatherLabel = new Label { X = 0, Y = 0, Width = Dim.Fill(), Text = "loading..." };
        _forecastLabel = new Label { X = 0, Y = 2, Width = Dim.Fill(), Height = 4, Text = "" };
        _infoFrame.Add(_currentWeatherLabel, _forecastLabel);

        _tihFrame = new FrameView
        {
            Title = "Today in History",
            X = Pos.Right(_agendaFrame),
            Y = Pos.Bottom(_infoFrame),
            Width = Dim.Percent(30),
            Height = Dim.Fill()
        };

        _tihView = new TodayInHistoryView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        if (_tihClient is null)
            _tihView.SetData(null);
        _tihFrame.Add(_tihView);

        _mailFrame = new FrameView
        {
            Title = "Mail",
            X = Pos.Right(_infoFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _accountList = new AccountListView(app)
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = 1 // sized dynamically in UpdateAccountList
        };

        _recentMailList = new EmailListView(app)
        {
            X = 0, Y = Pos.Bottom(_accountList),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _accountList.FilterChanged += ApplyMailFilter;
        _accountList.ReauthRequested += id => app.TriggerReauth(id);
        _mailFrame.Add(_accountList, _recentMailList);

        Add(_agendaFrame, _infoFrame, _tihFrame, _mailFrame);

        _app.RegisterQuitKey(_agendaList);
        _app.RegisterQuitKey(_accountList);
        _app.RegisterQuitKey(_recentMailList);
        _app.RegisterQuitKey(_tihView);

        Initialized += (_, _) =>
        {
            _app.App!.AddTimeout(TimeSpan.FromSeconds(60), () =>
            {
                _ = RefreshSystemAsync(CancellationToken.None);
                _ = RefreshTihAsync(CancellationToken.None);
                return true;
            });
        };
    }

    internal async Task LoadAsync(CancellationToken ct)
    {
        await Task.WhenAll(
            RefreshAgendaAsync(ct),
            RefreshSystemAsync(ct),
            RefreshMailOverviewAsync(ct),
            RefreshTihAsync(ct));
    }

    internal async Task RefreshAgendaAsync(CancellationToken ct)
    {
        var today = DateTimeOffset.Now.Date;
        var end = today.AddDays(14);
        var events = await _app.SafeApiCallAsync(
            c => _api.GetEventsAsync(new DateTimeOffset(today), new DateTimeOffset(end), ct: c), ct);

        if (events is null) return;

        _todayEvents = events.OrderBy(e => e.Start).ToList();
        _app.App?.Invoke(() =>
        {
            _agendaFrame.Title = "Agenda";
            _agendaList.SetRows(AgendaRowBuilder.BuildRows(_todayEvents, today));
        });
    }

    internal async Task RefreshSystemAsync(CancellationToken ct)
    {
        var weatherTask = _app.SafeApiCallAsync(c => _api.GetWeatherAsync(c), ct);
        var powerTask = _app.SafeApiCallAsync(c => _api.GetPowerAsync(c), ct);
        var clockTask = _app.SafeApiCallAsync(c => _api.GetClockAsync(c), ct);

        await Task.WhenAll(weatherTask, powerTask, clockTask);

        var weather = await weatherTask;
        var power = await powerTask;
        var clock = await clockTask;

        _app.App?.Invoke(() =>
        {
            if (weather is not null)
            {
                var emoji = WeatherEmoji(weather.Condition);
                var loc = weather.LocationName is not null ? $"  ({weather.LocationName})" : "";
                _currentWeatherLabel.Text = $"{emoji} {weather.TemperatureCelsius:F1}\u00b0C  {weather.Condition}{loc}";

                if (weather.Daily is { Count: > 0 })
                {
                    var today2 = DateOnly.FromDateTime(DateTimeOffset.Now.Date);
                    var fcLines = weather.Daily
                        .Where(f => f.Date > today2)
                        .Take(4)
                        .Select(fc =>
                        {
                            var e = fc.Condition is not null ? WeatherEmoji(fc.Condition) : " ";
                            var temps = fc.HighCelsius.HasValue && fc.LowCelsius.HasValue
                                ? $"{fc.HighCelsius.Value:F0}\u00b0/{fc.LowCelsius.Value:F0}\u00b0"
                                : "";
                            return $"{fc.Date:ddd d}: {e} {temps} {fc.Condition}";
                        });
                    _forecastLabel.Text = string.Join("\n", fcLines);
                }
                else
                {
                    _forecastLabel.Text = "";
                }
            }
            else
            {
                _currentWeatherLabel.Text = "unavailable";
                _forecastLabel.Text = "";
            }

            // Battery goes to status bar
            _app.UpdateBattery(power);

            // Clock data goes to TIH greeting
            _tihView.SetClock(clock);
        });
    }

    internal async Task RefreshTihAsync(CancellationToken ct)
    {
        if (_tihClient is null) return;

        var data = await _tihClient.GetTodayAsync(ct);
        _app.App?.Invoke(() => _tihView.SetData(data));
    }

    internal async Task RefreshMailOverviewAsync(CancellationToken ct)
    {
        // Fetch enough emails to fill the available space (2 rows per email)
        var mailHeight = Math.Max(10, _recentMailList.Viewport.Height);
        var limit = Math.Max(10, mailHeight / 2);

        var accountsTask = _app.SafeApiCallAsync(c => _api.GetAccountsAsync(c), ct);
        var mailTask = _app.SafeApiCallAsync(c => _api.ListMailAsync(limit: limit, ct: c), ct);

        await Task.WhenAll(accountsTask, mailTask);

        var accounts = await accountsTask;
        var mail = await mailTask;

        if (accounts is not null)
            _accounts = accounts.Where(a => a.Type is not "CalDav").ToList();
        if (mail is not null)
            _recentMail = mail;

        _app.App?.Invoke(() =>
        {
            UpdateAccountList();
            ApplyMailFilter();
        });
    }

    internal void UpdateAccountStatus(string accountId, bool online)
    {
        _app.App?.Invoke(UpdateAccountList);
    }

    private void UpdateAccountList()
    {
        _accountList.SetAccounts(_accounts);
        _accountList.Height = Math.Max(1, _accounts.Count);
    }

    private void ApplyMailFilter()
    {
        var disabled = _accountList.DisabledAccountIds;
        var filtered = disabled.Count > 0
            ? _recentMail.Where(m => !disabled.Contains(m.AccountId)).ToList()
            : _recentMail;
        _recentMailList.SetEmails(filtered);
    }

    private static string WeatherEmoji(string condition) => condition switch
    {
        "Clear" or "Mainly Clear" => "\U0001f31e",
        "Partly Cloudy" => "\u26c5",
        "Overcast" or "Fog" => "\u2601",
        "Drizzle" or "Rain" or "Rain Showers" => "\U0001f4a7",
        "Freezing Drizzle" or "Freezing Rain" => "\U0001f9ca",
        "Snow" or "Snow Grains" or "Snow Showers" => "\u2744",
        "Thunderstorm" or "Thunderstorm with Hail" => "\u26c8",
        _ => "  ",
    };
}
