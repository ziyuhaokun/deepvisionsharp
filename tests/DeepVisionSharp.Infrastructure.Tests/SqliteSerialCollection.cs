using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

/// <summary>Tests that open many temporary SQLite databases run alone, so connection pools and file locks do not overlap.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqliteSerialCollection
{
    public const string Name = "sqlite-serial";
}
