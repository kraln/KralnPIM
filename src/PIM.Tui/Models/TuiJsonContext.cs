using System.Text.Json.Serialization;

namespace PIM.Tui.Models;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(MailFlagPatch))]
[JsonSerializable(typeof(MailDetail))]
[JsonSerializable(typeof(AccountOverview))]
[JsonSerializable(typeof(List<AccountOverview>))]
[JsonSerializable(typeof(DeepSearchRequest))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(SystemStatus))]
[JsonSerializable(typeof(AccountStatusInfo))]
[JsonSerializable(typeof(List<AccountStatusInfo>))]
[JsonSerializable(typeof(ReauthResponse))]
[JsonSerializable(typeof(AttachmentDownloadResult))]
[JsonSerializable(typeof(WsEventEnvelope))]
[JsonSerializable(typeof(MailSyncEvent))]
[JsonSerializable(typeof(CalendarSyncEvent))]
[JsonSerializable(typeof(StatusChangeEvent))]
[JsonSerializable(typeof(TihResponse))]
[JsonSerializable(typeof(List<TihEntry>))]
[JsonSerializable(typeof(List<TihHoliday>))]
[JsonSerializable(typeof(List<TihPersonal>))]
public partial class TuiJsonContext : JsonSerializerContext
{
}
