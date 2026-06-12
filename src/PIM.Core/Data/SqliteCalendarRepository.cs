using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PIM.Core.Models;
using PIM.Core.Serialization;

namespace PIM.Core.Data;

public sealed class SqliteCalendarRepository : ICalendarRepository
{
    private readonly DbConnectionFactory _factory;

    public SqliteCalendarRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task UpsertEventsAsync(IEnumerable<CalendarEvent> events, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var transaction = conn.BeginTransaction();

        foreach (var evt in events)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT OR REPLACE INTO calendar_events
                (event_id, account_id, calendar_id, summary, description,
                 start_time, end_time, is_all_day, location, invitees,
                 recurrence_rule, status, transparency, synced_at)
                VALUES (@eid, @aid, @cid, @sum, @desc,
                        @start, @end, @allDay, @loc, @inv,
                        @rrule, @status, @transparency, @synced)
                """;
            cmd.Parameters.AddWithValue("@eid", evt.EventId);
            cmd.Parameters.AddWithValue("@aid", evt.AccountId);
            cmd.Parameters.AddWithValue("@cid", evt.CalendarId);
            cmd.Parameters.AddWithValue("@sum", (object?)evt.Summary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@desc", (object?)evt.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@start", evt.Start.ToString("O"));
            cmd.Parameters.AddWithValue("@end", evt.End.ToString("O"));
            cmd.Parameters.AddWithValue("@allDay", evt.IsAllDay ? 1 : 0);
            cmd.Parameters.AddWithValue("@loc", (object?)evt.Location ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@inv", JsonSerializer.Serialize(evt.Invitees, PimJsonContext.Default.ListString));
            cmd.Parameters.AddWithValue("@rrule", (object?)evt.RecurrenceRule ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@status", evt.Status.ToString());
            cmd.Parameters.AddWithValue("@transparency", evt.Transparency.ToString());
            cmd.Parameters.AddWithValue("@synced", DateTimeOffset.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync(ct);
        }

        transaction.Commit();
    }

    public async Task<List<CalendarEvent>> GetEventsInRangeAsync(
        DateTimeOffset start, DateTimeOffset end, string? accountId = null, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();

        var whereClause = "WHERE start_time < @end AND end_time > @start";
        cmd.Parameters.AddWithValue("@start", start.ToString("O"));
        cmd.Parameters.AddWithValue("@end", end.ToString("O"));

        if (accountId != null)
        {
            whereClause += " AND account_id = @aid";
            cmd.Parameters.AddWithValue("@aid", accountId);
        }

        cmd.CommandText = $"SELECT * FROM calendar_events {whereClause} ORDER BY start_time";

        var results = new List<CalendarEvent>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(ReadEvent(reader));

        return results;
    }

    public async Task DeleteEventAsync(string eventId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM calendar_events WHERE event_id = @eid";
        cmd.Parameters.AddWithValue("@eid", eventId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> DeleteEventsNotInCalendarsAsync(
        string accountId, IReadOnlySet<string> keepCalendarIds, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();

        if (keepCalendarIds.Count == 0)
        {
            // No calendars to keep — delete all events for this account
            cmd.CommandText = "DELETE FROM calendar_events WHERE account_id = @aid";
            cmd.Parameters.AddWithValue("@aid", accountId);
        }
        else
        {
            // Build parameterized IN clause for the calendars to keep
            var paramNames = new List<string>();
            var i = 0;
            foreach (var calId in keepCalendarIds)
            {
                var paramName = $"@cid{i}";
                paramNames.Add(paramName);
                cmd.Parameters.AddWithValue(paramName, calId);
                i++;
            }
            cmd.CommandText = $"DELETE FROM calendar_events WHERE account_id = @aid AND calendar_id NOT IN ({string.Join(", ", paramNames)})";
            cmd.Parameters.AddWithValue("@aid", accountId);
        }

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM calendar_events WHERE end_time < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static CalendarEvent ReadEvent(SqliteDataReader reader)
    {
        return new CalendarEvent(
            EventId: reader.GetString(reader.GetOrdinal("event_id")),
            AccountId: reader.GetString(reader.GetOrdinal("account_id")),
            CalendarId: reader.GetString(reader.GetOrdinal("calendar_id")),
            Summary: reader.IsDBNull(reader.GetOrdinal("summary")) ? "" : reader.GetString(reader.GetOrdinal("summary")),
            Description: reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
            Start: DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("start_time")), CultureInfo.InvariantCulture),
            End: DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("end_time")), CultureInfo.InvariantCulture),
            IsAllDay: reader.GetInt32(reader.GetOrdinal("is_all_day")) != 0,
            Location: reader.IsDBNull(reader.GetOrdinal("location")) ? null : reader.GetString(reader.GetOrdinal("location")),
            Invitees: DeserializeList(reader, "invitees"),
            RecurrenceRule: reader.IsDBNull(reader.GetOrdinal("recurrence_rule")) ? null : reader.GetString(reader.GetOrdinal("recurrence_rule")),
            Status: Enum.Parse<EventStatus>(reader.GetString(reader.GetOrdinal("status"))),
            Transparency: Enum.Parse<Transparency>(reader.GetString(reader.GetOrdinal("transparency")))
        );
    }

    private static List<string> DeserializeList(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return [];
        var json = reader.GetString(ordinal);
        return JsonSerializer.Deserialize(json, PimJsonContext.Default.ListString) ?? [];
    }
}
