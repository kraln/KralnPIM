using System.Text.Json.Serialization;

namespace PIM.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<SearchScope>))]
public enum SearchScope { All, Mail, Calendar }
