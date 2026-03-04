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
    private readonly FrameView _systemFrame;
    private readonly FrameView _mailFrame;

    private readonly ListView _agendaList;
    private readonly Label _dateLabel;
    private readonly Label _weatherLabel;
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

        _systemFrame = new FrameView
        {
            Title = "System",
            X = Pos.Right(_agendaFrame),
            Y = 0,
            Width = Dim.Percent(30),
            Height = Dim.Fill()
        };

        var today = DateTimeOffset.Now;
        var week = System.Globalization.ISOWeek.GetWeekOfYear(today.DateTime);
        _dateLabel = new Label { X = 0, Y = 0, Width = Dim.Fill(), Text = $"{today:dddd, d MMMM yyyy}  (W{week})" };
        _weatherLabel = new Label { X = 0, Y = 2, Width = Dim.Fill(), Text = "Weather: loading..." };
        _powerLabel = new Label { X = 0, Y = 3, Width = Dim.Fill(), Text = "Power: loading..." };
        _clockLabel = new Label { X = 0, Y = 5, Width = Dim.Fill(), Height = 4, Text = "Clock: loading..." };
        _systemFrame.Add(_dateLabel, _weatherLabel, _powerLabel, _clockLabel);

        _mailFrame = new FrameView
        {
            Title = "Mail Overview",
            X = Pos.Right(_systemFrame),
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

        Add(_agendaFrame, _systemFrame, _mailFrame);

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

                lines.Add($"  {e.Start.ToLocalTime():HH:mm}  {e.Summary}");
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
                _weatherLabel.Text = $"Weather: {weather.TemperatureCelsius:F1}°C  {weather.Condition}";
            else
                _weatherLabel.Text = "Weather: unavailable";

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
}
