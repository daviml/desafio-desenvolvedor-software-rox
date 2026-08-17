using System.Text.Json;
using System.Text.Json.Serialization;

namespace CashFlow.Messaging;

/// <summary>Single place where the wire format of integration events is defined.</summary>
public static class IntegrationEventSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IntegrationEvent integrationEvent) =>
        JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), Options);

    public static IntegrationEvent Deserialize(string payload, Type eventType) =>
        JsonSerializer.Deserialize(payload, eventType, Options) as IntegrationEvent
        ?? throw new InvalidOperationException($"Payload could not be deserialized as '{eventType.Name}'.");

    public static IntegrationEvent Deserialize(ReadOnlySpan<byte> payload, Type eventType) =>
        JsonSerializer.Deserialize(payload, eventType, Options) as IntegrationEvent
        ?? throw new InvalidOperationException($"Payload could not be deserialized as '{eventType.Name}'.");
}
