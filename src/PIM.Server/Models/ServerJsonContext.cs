using System.Text.Json.Serialization;
using PIM.Core.Models;
using PIM.Server.WebSocket;

namespace PIM.Server.Models;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
)]
[JsonSerializable(typeof(MailFlagPatch))]
[JsonSerializable(typeof(MailDetail))]
[JsonSerializable(typeof(AccountOverview))]
[JsonSerializable(typeof(List<AccountOverview>))]
[JsonSerializable(typeof(DeepSearchRequest))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(SystemStatus))]
[JsonSerializable(typeof(AccountStatusInfo))]
[JsonSerializable(typeof(List<AccountStatusInfo>))]
[JsonSerializable(typeof(MailSyncEvent))]
[JsonSerializable(typeof(CalendarSyncEvent))]
[JsonSerializable(typeof(StatusChangeEvent))]
[JsonSerializable(typeof(ReauthResponse))]
[JsonSerializable(typeof(SearchResult))]
[JsonSerializable(typeof(SearchScope))]
[JsonSerializable(typeof(FreeBusyShadow))]
public partial class ServerJsonContext : JsonSerializerContext
{
}
