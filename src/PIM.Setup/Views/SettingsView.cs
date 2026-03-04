using PIM.Core.Config;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PIM.Setup.Views;

internal enum SettingsSection { Ui, System, Storage, Server }

internal sealed class SettingsView : View
{
    private readonly SetupApp _app;

    public SettingsView(SetupApp app, SettingsSection section)
    {
        _app = app;
        CanFocus = true;

        // Defer rendering until App is available (needed for App?.Invoke calls in Render* methods)
        Initialized += (_, _) =>
        {
            switch (section)
            {
                case SettingsSection.Ui:
                    RenderUi();
                    break;
                case SettingsSection.System:
                    RenderSystem();
                    break;
                case SettingsSection.Storage:
                    RenderStorage();
                    break;
                case SettingsSection.Server:
                    RenderServer();
                    break;
            }
        };
    }

    private void RenderUi()
    {
        var title = new Label { X = 2, Y = 0, Text = "UI Settings" };

        var tzPrimLabel = new Label { X = 2, Y = 2, Text = "Primary Timezone:" };
        var tzPrimField = new TextField { X = 22, Y = 2, Width = 30, Text = _app.Config.Ui.TimezonePrimary };

        var tzSecLabel = new Label { X = 2, Y = 4, Text = "Secondary Timezone:" };
        var tzSecField = new TextField { X = 22, Y = 4, Width = 30, Text = _app.Config.Ui.TimezoneSecondary ?? "" };

        Add(title, tzPrimLabel, tzPrimField, tzSecLabel, tzSecField);

        AddSaveCancel(6, () =>
        {
            var primary = tzPrimField.Text;
            if (string.IsNullOrWhiteSpace(primary))
            {
                _app.ShowError("Primary timezone is required.");
                return false;
            }

            try { TimeZoneInfo.FindSystemTimeZoneById(primary); }
            catch { _app.ShowError($"Invalid timezone: '{primary}'"); return false; }

            var secondary = string.IsNullOrWhiteSpace(tzSecField.Text) ? null : tzSecField.Text;
            if (secondary is not null)
            {
                try { TimeZoneInfo.FindSystemTimeZoneById(secondary); }
                catch { _app.ShowError($"Invalid timezone: '{secondary}'"); return false; }
            }

            _app.Config = _app.Config with { Ui = new UiConfig(primary, secondary) };
            return true;
        }, [tzPrimField, tzSecField]);

        App?.Invoke(() => tzPrimField.SetFocus());
    }

    private void RenderSystem()
    {
        var title = new Label { X = 2, Y = 0, Text = "System Settings" };

        var locLabel = new Label { X = 2, Y = 2, Text = "Weather Location:" };
        var locField = new TextField { X = 22, Y = 2, Width = 30, Text = _app.Config.System.WeatherLocation ?? "" };
        var locHint = new Label { X = 22, Y = 3, Text = "(lat,lon e.g. 40.7128,-74.0060, optional)" };

        var provLabel = new Label { X = 2, Y = 5, Text = "Weather Provider:" };
        var provField = new Label { X = 22, Y = 5, Text = _app.Config.System.WeatherProvider + " (read-only)" };

        Add(title, locLabel, locField, locHint, provLabel, provField);

        AddSaveCancel(7, () =>
        {
            var location = string.IsNullOrWhiteSpace(locField.Text) ? null : locField.Text;
            _app.Config = _app.Config with { System = new SystemConfig(location, _app.Config.System.WeatherProvider) };
            return true;
        }, [locField]);

        App?.Invoke(() => locField.SetFocus());
    }

    private void RenderStorage()
    {
        var title = new Label { X = 2, Y = 0, Text = "Storage Settings" };

        var dbLabel = new Label { X = 2, Y = 2, Text = "Database Path:" };
        var dbField = new TextField { X = 24, Y = 2, Width = 40, Text = _app.Config.Storage.DbPath };

        var attLabel = new Label { X = 2, Y = 4, Text = "Attachment Dir:" };
        var attField = new TextField { X = 24, Y = 4, Width = 40, Text = _app.Config.Storage.AttachmentDownloadDir };

        var backLabel = new Label { X = 2, Y = 6, Text = "Buffer Months Back:" };
        var backField = new TextField { X = 24, Y = 6, Width = 10, Text = _app.Config.Storage.BufferMonthsBack.ToString() };

        var fwdLabel = new Label { X = 2, Y = 8, Text = "Buffer Months Forward:" };
        var fwdField = new TextField { X = 24, Y = 8, Width = 10, Text = _app.Config.Storage.BufferMonthsForward.ToString() };

        Add(title, dbLabel, dbField, attLabel, attField, backLabel, backField, fwdLabel, fwdField);

        AddSaveCancel(10, () =>
        {
            if (string.IsNullOrWhiteSpace(dbField.Text))
            {
                _app.ShowError("Database path is required.");
                return false;
            }

            if (!int.TryParse(backField.Text, out var monthsBack) || monthsBack < 0)
            {
                _app.ShowError("Buffer months back must be a non-negative integer.");
                return false;
            }

            if (!int.TryParse(fwdField.Text, out var monthsFwd) || monthsFwd < 0)
            {
                _app.ShowError("Buffer months forward must be a non-negative integer.");
                return false;
            }

            _app.Config = _app.Config with
            {
                Storage = new StorageConfig(dbField.Text, attField.Text, monthsBack, monthsFwd)
            };
            return true;
        }, [dbField, attField, backField, fwdField]);

        App?.Invoke(() => dbField.SetFocus());
    }

    private void RenderServer()
    {
        var title = new Label { X = 2, Y = 0, Text = "Server Settings" };

        var addrLabel = new Label { X = 2, Y = 2, Text = "Listen Address:" };
        var addrField = new TextField { X = 20, Y = 2, Width = 20, Text = _app.Config.Server.ListenAddress };

        var restLabel = new Label { X = 2, Y = 4, Text = "REST Port:" };
        var restField = new TextField { X = 20, Y = 4, Width = 10, Text = _app.Config.Server.RestPort.ToString() };

        var wsLabel = new Label { X = 2, Y = 6, Text = "WebSocket Port:" };
        var wsField = new TextField { X = 20, Y = 6, Width = 10, Text = _app.Config.Server.WsPort.ToString() };

        Add(title, addrLabel, addrField, restLabel, restField, wsLabel, wsField);

        AddSaveCancel(8, () =>
        {
            if (string.IsNullOrWhiteSpace(addrField.Text))
            {
                _app.ShowError("Listen address is required.");
                return false;
            }

            if (!int.TryParse(restField.Text, out var restPort) || restPort is <= 0 or > 65535)
            {
                _app.ShowError("REST port must be 1-65535.");
                return false;
            }

            if (!int.TryParse(wsField.Text, out var wsPort) || wsPort is <= 0 or > 65535)
            {
                _app.ShowError("WebSocket port must be 1-65535.");
                return false;
            }

            if (restPort == wsPort)
            {
                _app.ShowError("REST and WebSocket ports must differ.");
                return false;
            }

            _app.Config = _app.Config with
            {
                Server = new ServerConfig(addrField.Text, restPort, wsPort)
            };
            return true;
        }, [addrField, restField, wsField]);

        App?.Invoke(() => addrField.SetFocus());
    }

    private void AddSaveCancel(int y, Func<bool> validate, View[]? formFields = null)
    {
        var save = new Button { X = Pos.AnchorEnd(22), Y = Pos.AnchorEnd(2), Text = "Save" };
        var cancel = new Button { X = Pos.AnchorEnd(10), Y = Pos.AnchorEnd(2), Text = "Cancel" };

        void SaveAndReturn()
        {
            if (validate())
            {
                _app.MarkChanged();
                _app.ShowStatus("Settings updated.");
                _app.ShowMainMenu();
            }
        }

        save.Accepting += (_, e) =>
        {
            e.Handled = true;
            SaveAndReturn();
        };

        cancel.Accepting += (_, e) => { _app.ShowMainMenu(); e.Handled = true; };

        if (formFields is not null)
            WireEnterAdvance(formFields, SaveAndReturn);

        Add(save, cancel);
    }

    private static void WireEnterAdvance(View[] fields, Action lastFieldAction)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            var nextField = i < fields.Length - 1 ? fields[i + 1] : null;
            fields[i].Accepting += (_, e) =>
            {
                e.Handled = true;
                if (nextField is not null)
                    nextField.SetFocus();
                else
                    lastFieldAction();
            };
        }
    }
}
