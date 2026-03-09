using PIM.Core.Models;
using PIM.Tui.Client;
using PIM.Tui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace PIM.Tui.Views;

internal sealed class DashboardTab : View
{
    private readonly PimApiClient _api;
    private readonly TuiApp _app;

    private readonly FrameView _agendaFrame;
    private readonly FrameView _weatherFrame;
    private readonly FrameView _systemFrame;
    private readonly FrameView _mailFrame;

    private readonly AgendaListView _agendaList;
    private readonly Label _dateLabel;
    private readonly Label _currentWeatherLabel;
    private readonly Label _forecastLabel;
    private readonly Label _powerLabel;
    private readonly Label _clockLabel;
    private readonly AccountListView _accountList;
    private readonly EmailListView _recentMailList;

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

        _agendaList = new AgendaListView(app)
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _agendaFrame.Add(_agendaList);

        var today = DateTimeOffset.Now;
        var week = System.Globalization.ISOWeek.GetWeekOfYear(today.DateTime);

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
            Title = "Mail",
            X = Pos.Right(_weatherFrame),
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
        _mailFrame.Add(_accountList, _recentMailList);

        Add(_agendaFrame, _weatherFrame, _systemFrame, _mailFrame);

        _app.RegisterQuitKey(_agendaList);
        _app.RegisterQuitKey(_accountList);
        _app.RegisterQuitKey(_recentMailList);

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
            _agendaList.SetRows(AgendaListView.BuildRows(_todayEvents, today));
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

    /// <summary>
    /// Compact custom-drawn account list with per-account foreground colors.
    /// Accounts can be toggled on/off to filter the email list.
    /// </summary>
    private sealed class AccountListView : View
    {
        private readonly TuiApp _app;
        private List<AccountOverview> _accounts = [];
        private readonly Dictionary<string, Color> _accountColors = new();
        private readonly HashSet<string> _disabledAccounts = new();
        private int _selectedIndex;

        public event Action? FilterChanged;

        public AccountListView(TuiApp app)
        {
            _app = app;
            CanFocus = true;
            KeyDown += HandleKeyDown;
            MouseEvent += HandleMouseEvent;
        }

        public HashSet<string> DisabledAccountIds => _disabledAccounts;

        public void SetAccounts(List<AccountOverview> accounts)
        {
            _accounts = accounts;
            if (_selectedIndex >= _accounts.Count)
                _selectedIndex = Math.Max(0, _accounts.Count - 1);
            EnsureColors();
            SetNeedsDraw();
        }

        private void EnsureColors()
        {
            foreach (var a in _accounts)
            {
                if (_accountColors.ContainsKey(a.Id)) continue;
                _accountColors[a.Id] = _app.GetOrAssignAccountColor(a.Id);
            }
        }

        private void ToggleSelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _accounts.Count) return;
            var id = _accounts[_selectedIndex].Id;
            if (!_disabledAccounts.Remove(id))
                _disabledAccounts.Add(id);
            SetNeedsDraw();
            FilterChanged?.Invoke();
        }

        private void HandleKeyDown(object? sender, Key e)
        {
            if (e == Key.CursorUp && _selectedIndex > 0)
            {
                _selectedIndex--;
                SetNeedsDraw();
                e.Handled = true;
            }
            else if (e == Key.CursorDown && _selectedIndex < _accounts.Count - 1)
            {
                _selectedIndex++;
                SetNeedsDraw();
                e.Handled = true;
            }
            else if (e == Key.Space || e == Key.Enter)
            {
                ToggleSelected();
                e.Handled = true;
            }
        }

        private void HandleMouseEvent(object? sender, Mouse e)
        {
            if (e.Flags.HasFlag(MouseFlags.LeftButtonPressed) && e.Position is { } pos)
            {
                if (pos.Y >= 0 && pos.Y < _accounts.Count)
                {
                    _selectedIndex = pos.Y;
                    ToggleSelected();
                }
                if (!HasFocus) SetFocus();
                e.Handled = true;
            }
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var width = Viewport.Width;
            var height = Viewport.Height;

            SetAttribute(Sol.Normal);
            for (var r = 0; r < height; r++)
            {
                Move(0, r);
                AddStr(new string(' ', width));
            }

            for (var i = 0; i < _accounts.Count && i < height; i++)
            {
                var a = _accounts[i];
                var fg = _accountColors.GetValueOrDefault(a.Id, Sol.Base0);
                var disabled = _disabledAccounts.Contains(a.Id);
                var selected = i == _selectedIndex && HasFocus;
                var bg = selected ? Sol.Base02 : Sol.Base03;

                Move(0, i);

                // Indicator: ● enabled, ○ disabled, in account color (dimmed if disabled)
                var indicatorFg = disabled ? Sol.Base01 : fg;
                SetAttribute(new GuiAttribute(indicatorFg, bg));
                AddStr(disabled ? "\u25cb " : "\u25cf ");

                // Account details — dimmed if disabled
                var textFg = disabled ? Sol.Base01 : fg;
                SetAttribute(new GuiAttribute(textFg, bg));

                var status = _app.IsAccountOnline(a.Id) ? "" : " [OFFLINE]";
                var text = $"{a.DisplayName} ({a.Type}){status}  U:{a.UnreadCount} F:{a.FlaggedCount}";
                if (text.Length > width - 2)
                    text = text[..(width - 2)];
                AddStr(text);

                // Pad rest of line with bg color for selected highlight
                var remaining = width - 2 - text.Length;
                if (remaining > 0) AddStr(new string(' ', remaining));
            }

            return true;
        }
    }
}
