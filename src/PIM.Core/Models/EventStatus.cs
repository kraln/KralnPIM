using System.Text.Json.Serialization;

namespace PIM.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<EventStatus>))]
public enum EventStatus { Confirmed, Tentative, Cancelled }
