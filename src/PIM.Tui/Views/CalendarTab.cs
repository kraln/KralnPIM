using System.Collections.ObjectModel;
using PIM.Core.Models;
using PIM.Tui.Client;
using Terminal.Gui.App;
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
    private readonly ListView _agendaList;
    private readonly FrameView[] _dayColumns;
    private readonly ListView[] _dayLists;

    private EventEditorView? _editorView;
    private DateTimeOffset _windowStart;
    private List<CalendarEvent> _todayEvents = [];
    private List<CalendarEvent> _windowEvents = [];

    public CalendarTab(PimApiClient api, TuiApp app)
    {
        _api = api;
        _app = app;
        CanFocus = true;
        X = 0; Y = 0; Width = Dim.Fill(); Height = Dim.Fill();
        _windowStart = DateTimeOffset.Now.Date;

        // Left pane: today's agenda
        _agendaFrame = new FrameView
        {
            Title = "Agenda",
            X = 0, Y = 0,
            Width = Dim.Percent(25),
            Height = Dim.Fill()
        };

        _agendaList = new ListView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _agendaFrame.Add(_agendaList);

        // Right pane: 4-day timeline
        _timelineFrame = new FrameView
        {
            Title = "Timeline",
            X = Pos.Right(_agendaFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _dayColumns = new FrameView[4];
        _dayLists = new ListView[4];

        for (var i = 0; i < 4; i++)
        {
            _dayColumns[i] = new FrameView
            {
                X = Pos.Percent(i * 25),
                Y = 0,
                Width = Dim.Percent(25),
                Height = Dim.Fill()
            };

            _dayLists[i] = new ListView
            {
                X = 0, Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            _dayColumns[i].Add(_dayLists[i]);
            _timelineFrame.Add(_dayColumns[i]);
        }

        Add(_agendaFrame, _timelineFrame);

        // Key handlers go on ListViews (the focused controls) so they fire before type-ahead search
        void HandleCalendarKey(object? sender, Key e)
        {
            if (e == Key.CursorLeft)
            {
                _windowStart = _windowStart.AddDays(-1);
                _ = RefreshTimelineAsync(CancellationToken.None);
                e.Handled = true;
            }
            else if (e == Key.CursorRight)
            {
                _windowStart = _windowStart.AddDays(1);
                _ = RefreshTimelineAsync(CancellationToken.None);
                e.Handled = true;
            }
            else if (e == Key.N)
            {
                if (_editorView is null)
                {
                    OpenEditor(null);
                    e.Handled = true;
                }
            }
            else if (e == Key.Enter)
            {
                if (_editorView is null)
                    TryEditSelectedEvent();
                e.Handled = true;
            }
        }

        _agendaList.KeyDown += HandleCalendarKey;
        _app.RegisterQuitKey(_agendaList);
        for (var i = 0; i < 4; i++)
        {
            _dayLists[i].KeyDown += HandleCalendarKey;
            _app.RegisterQuitKey(_dayLists[i]);
        }

        Initialized += (_, _) => _ = RefreshAsync(CancellationToken.None);
    }

    internal async Task RefreshAsync(CancellationToken ct)
    {
        await Task.WhenAll(
            RefreshAgendaAsync(ct),
            RefreshTimelineAsync(ct));
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
            _agendaList.SetSource(new ObservableCollection<string>(
                _todayEvents.Select(e =>
                {
                    var date = e.Start.ToLocalTime();
                    var prefix = date.Date == today ? $"{date:HH:mm}    " : $"{date:MMM d} {date:HH:mm}";
                    return $"{prefix}  {e.Summary}";
                })));
        });
    }

    private async Task RefreshTimelineAsync(CancellationToken ct)
    {
        var end = _windowStart.AddDays(4);
        var events = await _app.SafeApiCallAsync(
            c => _api.GetEventsAsync(_windowStart, end, ct: c), ct);

        if (events is null) return;

        _windowEvents = events;

        _app.App?.Invoke(() =>
        {
            _timelineFrame.Title = $"Timeline: {_windowStart:MMM d} - {end.AddDays(-1):MMM d}";

            for (var i = 0; i < 4; i++)
            {
                var day = _windowStart.AddDays(i);
                var dayEnd = day.AddDays(1);
                _dayColumns[i].Title = $"{day:ddd d}";

                var dayEvents = _windowEvents
                    .Where(e => e.Start < dayEnd && e.End > day)
                    .OrderBy(e => e.Start)
                    .ToList();

                _dayLists[i].SetSource(new ObservableCollection<string>(dayEvents
                    .Select(e =>
                    {
                        var start = e.Start.ToLocalTime().ToString("HH:mm");
                        var endStr = e.End.ToLocalTime().ToString("HH:mm");
                        return $"{start}-{endStr} {e.Summary}";
                    })));
            }
        });
    }

    private void TryEditSelectedEvent()
    {
        for (var i = 0; i < 4; i++)
        {
            if (!_dayLists[i].HasFocus) continue;

            var idx = _dayLists[i].SelectedItem ?? -1;
            var day = _windowStart.AddDays(i);
            var dayEnd = day.AddDays(1);
            var dayEvents = _windowEvents
                .Where(e => e.Start < dayEnd && e.End > day)
                .OrderBy(e => e.Start)
                .ToList();

            if (idx >= 0 && idx < dayEvents.Count)
                OpenEditor(dayEvents[idx]);

            return;
        }
    }

    private void OpenEditor(CalendarEvent? existingEvent)
    {
        _editorView = new EventEditorView(_api, _app, existingEvent, onClose: () =>
        {
            _timelineFrame.Remove(_editorView!);
            _editorView = null;

            // Restore day columns
            foreach (var col in _dayColumns)
                col.Visible = true;

            _ = RefreshAsync(CancellationToken.None);
        });

        // Hide day columns, show editor
        foreach (var col in _dayColumns)
            col.Visible = false;

        _timelineFrame.Add(_editorView);
        _editorView.SetFocus();
    }
}
