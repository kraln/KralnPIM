using System.Text.Json.Serialization;
using PIM.Core.Models;

namespace PIM.Core.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
)]
[JsonSerializable(typeof(EmailHeader))]
[JsonSerializable(typeof(List<EmailHeader>))]
[JsonSerializable(typeof(EmailBody))]
[JsonSerializable(typeof(AttachmentInfo))]
[JsonSerializable(typeof(List<AttachmentInfo>))]
[JsonSerializable(typeof(CalendarEvent))]
[JsonSerializable(typeof(List<CalendarEvent>))]
[JsonSerializable(typeof(OAuthToken))]
[JsonSerializable(typeof(SyncResult<EmailHeader>))]
[JsonSerializable(typeof(SyncResult<CalendarEvent>))]
[JsonSerializable(typeof(OutboundEmail))]
[JsonSerializable(typeof(EmailListQuery))]
[JsonSerializable(typeof(List<string>))]
public partial class PimJsonContext : JsonSerializerContext
{
}
