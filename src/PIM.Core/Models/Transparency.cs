using System.Text.Json.Serialization;

namespace PIM.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<Transparency>))]
public enum Transparency { Busy, Free }
