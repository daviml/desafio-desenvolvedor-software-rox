using System.Collections.Concurrent;

namespace CashFlow.Messaging;

/// <summary>
/// Two-way map between the wire name of an event ("cashflow.entry.registered") and its CLR type.
/// Keeping the wire name explicit - instead of relying on assembly-qualified type names - means
/// producer and consumer can be refactored, renamed or rewritten in another language independently.
/// </summary>
public sealed class IntegrationEventRegistry
{
    private readonly ConcurrentDictionary<string, Type> _typesByName = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string> _namesByType = new();

    public IntegrationEventRegistry Register<TEvent>(string wireName)
        where TEvent : IntegrationEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wireName);

        _typesByName[wireName] = typeof(TEvent);
        _namesByType[typeof(TEvent)] = wireName;
        return this;
    }

    public string GetWireName(Type eventType) =>
        _namesByType.TryGetValue(eventType, out var name)
            ? name
            : throw new InvalidOperationException(
                $"Integration event '{eventType.Name}' is not registered. Register it in the composition root.");

    public bool TryResolveType(string wireName, out Type eventType) =>
        _typesByName.TryGetValue(wireName, out eventType!);
}
