using System.Collections.ObjectModel;
using PIM.Core.Models;
using PIM.Tui.Client;
using PIM.Tui.Models;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PIM.Tui.Views;

internal sealed class DashboardTab : View
{
    private readonly PimApiClient _api;
    private readonly TuiApp _app;

    private readonly FrameView _agendaFrame;
    private readonly FrameView _weatherFrame;
    private readonly FrameView _systemFrame;
    private readonly FrameView _mailFrame;

    private readonly ListView _agendaList;
    private readonly Label _dateLabel;
    private readonly Label _currentWeatherLabel;
    private readonly Label _forecastLabel;
    private readonly Label _powerLabel;
    private readonly Label _clockLabel;
    private readonly ListView _accountList;
    private readonly ListView _recentMailList;

    private List<CalendarEvent> _todayEvents = [];
    private List<AccountOverview> _accounts = [];
    private List<EmailHeader> _recentMail = [];

    public DashboardTab(PimApiClient api, TuiApp app)
    {
        _api = api;
        _app = app;
        CanFocus = true;
        X = 0; Y = 0; Width = Dim.Fill(); Height = Dim.Fill();

        _agendaFrame = new FrameView
        {
            Title = "Agenda",
            X = 0, Y = 0,
            Width = Dim.Percent(30),
            Height = Dim.Fill()
        };

        _agendaList = new ListView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _agendaFrame.Add(_agendaList);

        var today = DateTimeOffset.Now;
        var week = System.Globalization.ISOWeek.GetWeekOfYear(today.DateTime);

        // Weather frame: date, current conditions, forecast
        _weatherFrame = new FrameView
        {
            Title = "Weather",
            X = Pos.Right(_agendaFrame),
            Y = 0,
            Width = Dim.Percent(30),
            Height = Dim.Percent(60)
        };

        _dateLabel = new Label { X = 0, Y = 0, Width = Dim.Fill(), Text = $"{today:dddd, d MMMM yyyy}  (W{week})" };
        _currentWeatherLabel = new Label { X = 0, Y = 1, Width = Dim.Fill(), Text = "loading..." };
        _forecastLabel = new Label { X = 0, Y = 3, Width = Dim.Fill(), Height = 5, Text = "" };
        _weatherFrame.Add(_dateLabel, _currentWeatherLabel, _forecastLabel);

        // System frame: power, clocks
        _systemFrame = new FrameView
        {
            Title = "System",
            X = Pos.Right(_agendaFrame),
            Y = Pos.Bottom(_weatherFrame),
            Width = Dim.Percent(30),
            Height = Dim.Fill()
        };

        _powerLabel = new Label { X = 0, Y = 0, Width = Dim.Fill(), Text = "Power: loading..." };
        _clockLabel = new Label { X = 0, Y = 2, Width = Dim.Fill(), Height = Dim.Fill(), Text = "Clock: loading..." };
        _systemFrame.Add(_powerLabel, _clockLabel);

        _mailFrame = new FrameView
        {
            Title = "Mail Overview",
            X = Pos.Right(_weatherFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _accountList = new ListView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Percent(40)
        };

        _recentMailList = new ListView
        {
            X = 0, Y = Pos.Bottom(_accountList),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _mailFrame.Add(_accountList, _recentMailList);

        Add(_agendaFrame, _weatherFrame, _systemFrame, _mailFrame);

        // Register Q-to-quit on ListViews so it fires before type-ahead search
        _app.RegisterQuitKey(_agendaList);
        _app.RegisterQuitKey(_accountList);
        _app.RegisterQuitKey(_recentMailList);

        // Refresh system info every 60 seconds (deferred — App is null during construction)
        Initialized += (_, _) =>
        {
            _app.App!.AddTimeout(TimeSpan.FromSeconds(60), () =>
            {
                _ = RefreshSystemAsync(CancellationToken.None);
                return true;
            });
        };
    }

    internal async Task LoadAsync(CancellationToken ct)
    {
        await Task.WhenAll(
            RefreshAgendaAsync(ct),
            RefreshSystemAsync(ct),
            RefreshMailOverviewAsync(ct));
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
            var lines = new List<string>();
            var currentDate = (DateTime?)null;

            foreach (var e in _todayEvents)
            {
                var eventDate = e.Start.ToLocalTime().Date;
                if (currentDate != eventDate)
                {
                    if (lines.Count > 0) lines.Add("");
                    var dayLabel = eventDate == today
                        ? $"Today - {eventDate:ddd MMM d}"
                        : $"{eventDate:ddd MMM d}";
                    lines.Add(dayLabel);
                    currentDate = eventDate;
                }

                lines.Add(e.IsAllDay
                    ? $"  All day  {e.Summary}"
                    : $"  {e.Start.ToLocalTime():HH:mm}  {e.Summary}");
            }

            if (lines.Count == 0)
                lines.Add($"Today - {today:ddd MMM d}");

            _agendaList.SetSource(new ObservableCollection<string>(lines));
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
            var now = DateTimeOffset.Now;
            var wk = System.Globalization.ISOWeek.GetWeekOfYear(now.DateTime);
            _dateLabel.Text = $"{now:dddd, d MMMM yyyy}  (W{wk})";

            if (weather is not null)
            {
                var emoji = WeatherEmoji(weather.Condition);
                _currentWeatherLabel.Text = $"{emoji} {weather.TemperatureCelsius:F1}\u00b0C  {weather.Condition}";

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

            if (power is not null)
            {
                var remaining = power.TimeRemaining is not null ? $" ({power.TimeRemaining})" : "";
                _powerLabel.Text = $"Power: {power.BatteryPercent}%{remaining}";
            }
            else
            {
                _powerLabel.Text = "Power: unavailable";
            }

            if (clock is not null)
            {
                var lines = clock.Zones
                    .Select(z => $"  {z.Label}: {z.CurrentTime.ToLocalTime():HH:mm}")
                    .ToList();
                _clockLabel.Text = "Clocks:\n" + string.Join("\n", lines);
            }
            else
            {
                _clockLabel.Text = "Clock: unavailable";
            }
        });
    }

    internal async Task RefreshMailOverviewAsync(CancellationToken ct)
    {
        var accountsTask = _app.SafeApiCallAsync(c => _api.GetAccountsAsync(c), ct);
        var mailTask = _app.SafeApiCallAsync(c => _api.ListMailAsync(limit: 10, ct: c), ct);

        await Task.WhenAll(accountsTask, mailTask);

        var accounts = await accountsTask;
        var mail = await mailTask;

        if (accounts is not null)
            _accounts = accounts.Where(a => a.Type is not "CalDav").ToList();
        if (mail is not null)
            _recentMail = mail;

        _app.App?.Invoke(() =>
        {
            _accountList.SetSource(new ObservableCollection<string>(
                _accounts.Select(a =>
                {
                    var status = _app.IsAccountOnline(a.Id) ? "" : " [OFFLINE]";
                    return $"{a.DisplayName} ({a.Type}){status}  U:{a.UnreadCount} F:{a.FlaggedCount}";
                })));

            _recentMailList.SetSource(new ObservableCollection<string>(
                _recentMail.Select(m =>
                {
                    var indicator = m.IsRead ? " " : "*";
                    var from = m.FromDisplayName ?? m.FromAddress;
                    if (from.Length > 20) from = from[..17] + "...";
                    var subject = m.Subject ?? "(no subject)";
                    if (subject.Length > 30) subject = subject[..27] + "...";
                    return $"{indicator} {from}: {subject}";
                })));
        });
    }

    internal void UpdateAccountStatus(string accountId, bool online)
    {
        // Refresh account list to show updated online/offline status
        _app.App?.Invoke(() =>
        {
            _accountList.SetSource(new ObservableCollection<string>(
                _accounts.Select(a =>
                {
                    var status = _app.IsAccountOnline(a.Id) ? "" : " [OFFLINE]";
                    return $"{a.DisplayName} ({a.Type}){status}  U:{a.UnreadCount} F:{a.FlaggedCount}";
                })));
        });
    }

    // Only emoji verified in sandbox/emoji_test to have correct column alignment.
    // Many emoji (VS16, some supplementary plane) cause Terminal.Gui width mismatches.
    // See CLAUDE.md and sandbox/emoji_test/ for details.
    private static string WeatherEmoji(string condition) => condition switch
    {
        "Clear" or "Mainly Clear" => "\U0001f31e",  // 🌞 sun face
        "Partly Cloudy" => "\u26c5",                 // ⛅ partly cloudy
        "Overcast" or "Fog" => "\u2601",             // ☁ cloud (no VS16!)
        "Drizzle" or "Rain" or "Rain Showers" => "\U0001f4a7", // 💧 droplet
        "Freezing Drizzle" or "Freezing Rain" => "\U0001f9ca", // 🧊 ice
        "Snow" or "Snow Grains" or "Snow Showers" => "\u2744", // ❄ snowflake (no VS16!)
        "Thunderstorm" or "Thunderstorm with Hail" => "\u26c8", // ⛈ thunder cloud
        _ => "  ",
    };
}
