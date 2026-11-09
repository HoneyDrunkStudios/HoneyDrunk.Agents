using System.Text.Json.Serialization;

namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>One audit entry about an agent. Field changes and metadata are never projected.</summary>
/// <param name="At">When the event happened.</param>
/// <param name="EventName">The event name; serialized as <c>event</c>.</param>
/// <param name="Actor">Who acted.</param>
/// <param name="Outcome">The outcome: <c>succeeded</c>, <c>denied</c>, <c>failed</c> or <c>pending</c>.</param>
/// <param name="TargetType">The target's type.</param>
/// <param name="TargetId">The target's id.</param>
/// <param name="Reason">The recorded reason, if any.</param>
public sealed record AuditEntryView(
    DateTimeOffset At,
    [property: JsonPropertyName("event")] string EventName,
    string Actor,
    string Outcome,
    string TargetType,
    string TargetId,
    string? Reason);
