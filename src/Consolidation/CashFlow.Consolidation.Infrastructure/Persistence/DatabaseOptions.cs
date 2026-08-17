using System.ComponentModel.DataAnnotations;

namespace CashFlow.Consolidation.Infrastructure.Persistence;

/// <summary>Supported storage engines.</summary>
public enum DatabaseProvider
{
    Postgres = 0,
    Sqlite = 1,
}

/// <summary>Database settings, bound from the "Database" configuration section.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Postgres;

    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    public bool ApplySchemaOnStartup { get; set; } = true;

    [Range(0, 20)]
    public int MaxRetryCount { get; set; } = 5;

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    public bool EnableSensitiveDataLogging { get; set; }
}
