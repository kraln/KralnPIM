namespace PIM.Tui.Models;

public sealed record TihResponse(
    string Date,
    int DayOfYear,
    int DaysRemaining,
    List<TihEntry> Birthdays,
    List<TihEntry> Events,
    List<TihHoliday> Holidays,
    List<TihPersonal> Personal);

public sealed record TihEntry(int Id, int? Year, string Description);

public sealed record TihHoliday(int Id, string Name, string Description);

public sealed record TihPersonal(int Id, string Type, string Description);
