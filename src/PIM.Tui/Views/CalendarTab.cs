using PIM.Core.Models;
using PIM.Tui.Client;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PIM.Tui.Views;

internal sealed class CalendarTab : View
{
    private readonly PimApiClient _api;
    private readonly TuiApp _app;

    private readonly FrameView _agendaFrame;
    private readonly FrameView _timelineFrame;
    private readonly AgendaListView _agendaList;
    private readonly TimeGridView _gridView;

    private EventEditorView? _editorView;
    private DateTimeOffset _windowStart;
    private List<CalendarEvent> _todayEvents = [];

    public CalendarTab(PimApiClient api, TuiApp app)
    {
        _api = api;
        _app = app;
        CanFocus = true;
        X = 0; Y = 0; Width = Dim.Fill(); Height = Dim.Fill();
        _windowStart = DateTimeOffset.Now.Date;

        // Left pane: upcoming agenda
        _agendaFrame = new FrameView
        {
            Title = "Agenda",
            X = 0, Y = 0,
            Width = Dim.Percent(25),
            Height = Dim.Fill()
        };

        _agendaList = new AgendaListView(app)
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _agendaFrame.Add(_agendaList);

        // Right pane: 4-day time grid
        _timelineFrame = new FrameView
        {
            Title = "Timeline",
            X = Pos.Right(_agendaFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _gridView = new TimeGridView(
            app,
            onWindowShift: shift =>
            {
                _windowStart = _windowStart.AddDays(shift);
                _ = RefreshTimelineAndSunAsync(CancellationToken.None);
            },
            onEditEvent: evt =>
            {
                if (_editorView is null)
                    OpenEditor(evt);
            },
            onNewEvent: startTime =>
            {
                if (_editorView is null)
                    OpenEditor(null, startTime);
            },
            onJumpToToday: () =>
            {
                _windowStart = DateTimeOffset.Now.Date;
                _ = RefreshTimelineAndSunAsync(CancellationToken.None);
            });

        _timelineFrame.Add(_gridView);

        Add(_agendaFrame, _timelineFrame);

        // Agenda list key handlers
        _agendaList.EventSelected += evt =>
        {
            if (_editorView is null) OpenEditor(evt);
        };
        _agendaList.KeyDown += (_, e) =>
        {
            if (e == Key.N && _editorView is null)
            {
                OpenEditor(null, DateTimeOffset.Now);
                e.Handled = true;
            }
            else if (e == Key.CursorRight || e == Key.CursorLeft)
            {
                _gridView.SetFocus();
                e.Handled = true;
            }
        };
        _app.RegisterQuitKey(_agendaList);

        Initialized += (_, _) => _ = InitialLoadAsync();
    }

    private async Task InitialLoadAsync()
    {
        await RefreshAsync(CancellationToken.None);
        _app.App?.Invoke(() => _gridView.JumpToCurrentTime());
    }

    internal async Task RefreshAsync(CancellationToken ct)
    {
        await Task.WhenAll(
            RefreshAgendaAsync(ct),
            RefreshTimelineAsync(ct),
            RefreshSunTimesAsync(ct));
    }

    private async Task RefreshSunTimesAsync(CancellationToken ct)
    {
        var weather = await _app.SafeApiCallAsync(c => _api.GetWeatherAsync(c), ct);
        if (weather is null) return;

        _app.App?.Invoke(() => _gridView.SetDailyForecasts(weather.Daily ?? []));
    }

    private async Task RefreshAgendaAsync(CancellationToken ct)
    {
        var today = DateTimeOffset.Now.Date;
        var end = today.AddDays(14);
        var events = await _app.SafeApiCallAsync(
            c => _api.GetEventsAsync(new DateTimeOffset(today), new DateTimeOffset(end), ct: c), ct);

        if (events is null) return;

        _todayEvents = events.OrderBy(e => e.Start).ToList();
        _app.App?.Invoke(() =>
        {
            _agendaFrame.Title = "Upcoming";
            _agendaList.SetRows(AgendaListView.BuildRows(_todayEvents, today));
        });
    }

    private async Task RefreshTimelineAndSunAsync(CancellationToken ct)
    {
        await Task.WhenAll(RefreshTimelineAsync(ct), RefreshSunTimesAsync(ct));
    }

    private async Task RefreshTimelineAsync(CancellationToken ct)
    {
        var end = _windowStart.AddDays(4);
        var events = await _app.SafeApiCallAsync(
            c => _api.GetEventsAsync(_windowStart, end, ct: c), ct);

        if (events is null) return;

        _app.App?.Invoke(() =>
        {
            _timelineFrame.Title = $"Timeline: {_windowStart:MMM d} - {end.AddDays(-1):MMM d}";
            _gridView.SetEvents(_windowStart, events);
        });
    }

    private void OpenEditor(CalendarEvent? existingEvent, DateTimeOffset? suggestedStart = null)
    {
        // Show day context in the agenda pane
        var eventDate = existingEvent?.Start.ToLocalTime().Date ?? suggestedStart?.Date ?? DateTime.Today;
        ShowDayContext(eventDate);

        _editorView = new EventEditorView(_api, _app, existingEvent, onClose: () =>
        {
            _timelineFrame.Remove(_editorView!);
            _editorView = null;
            _gridView.Visible = true;
            _ = RefreshAsync(CancellationToken.None);
        }, suggestedStart: suggestedStart);

        _gridView.Visible = false;
        _timelineFrame.Add(_editorView);
        _editorView.SetFocus();
    }

    private void ShowDayContext(DateTime date)
    {
        var dayEvents = _todayEvents
            .Where(e => e.Start.ToLocalTime().Date == date)
            .OrderBy(e => e.Start)
            .ToList();

        _agendaFrame.Title = $"{date:ddd MMM d}";
        _agendaList.SetRows(AgendaListView.BuildRows(dayEvents, date));
    }
}
