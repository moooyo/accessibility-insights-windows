// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace AccessibilityInsights.Core.IssueReporting;

public class AzureDevOpsEntity
{
    [JsonPropertyOrder(1)]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyOrder(2)]
    public Guid Id { get; set; }

    public override bool Equals(object? obj) =>
        obj is AzureDevOpsEntity other &&
        string.Equals(Name, other.Name, StringComparison.Ordinal) &&
        Id == other.Id;

    public override int GetHashCode() => HashCode.Combine(Name, Id);
}

public sealed class AzureDevOpsProject : AzureDevOpsEntity
{
}

public sealed class AzureDevOpsTeam : AzureDevOpsEntity
{
    [JsonPropertyOrder(0)]
    public AzureDevOpsProject? ParentProject { get; set; }
}

public sealed class AzureDevOpsConnection
{
    private static readonly DateTime DefaultTime = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [JsonPropertyOrder(0)]
    public Uri? ServerUri { get; set; }

    [JsonPropertyOrder(1)]
    public AzureDevOpsProject? Project { get; set; }

    [JsonPropertyOrder(2)]
    public AzureDevOpsTeam? Team { get; set; }

    [JsonPropertyOrder(3)]
    public DateTime LastUsage { get; set; }

    [JsonIgnore]
    public bool IsPopulated => ServerUri is not null && Project is not null;

    public void SetLastUsage(DateTime? updateTime = null) =>
        LastUsage = updateTime?.ToUniversalTime() ?? DefaultTime;

    public bool DataEquals(AzureDevOpsConnection? other) =>
        other is not null &&
        Equals(ServerUri, other.ServerUri) &&
        Equals(Project, other.Project) &&
        Equals(Team, other.Team);

    public override string ToString() =>
        string.Join("/", new[] { Project?.Name, Team?.Name }.Where(value => !string.IsNullOrEmpty(value)));
}

public sealed class AzureDevOpsConnectionCache
{
    public const int Capacity = 5;
    private readonly List<AzureDevOpsConnection> connections = new(Capacity);

    public IReadOnlyList<AzureDevOpsConnection> Connections => connections;

    public bool Add(AzureDevOpsConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.ServerUri is null)
        {
            throw new ArgumentException("The Azure DevOps connection must have a server URI.", nameof(connection));
        }

        int removed = connections.RemoveAll(existing =>
            existing.ServerUri == connection.ServerUri &&
            existing.LastUsage <= connection.LastUsage);

        bool reachedCapacity = connections.Count >= Capacity;
        if (reachedCapacity)
        {
            connections.Sort((left, right) => left.LastUsage.CompareTo(right.LastUsage));
            connections.RemoveAt(0);
        }

        connections.Add(Clone(connection));
        return removed > 0 || reachedCapacity;
    }

    public AzureDevOpsConnection? GetMostRecent(Uri? serverUri = null) =>
        connections
            .Where(connection => serverUri is null || connection.ServerUri == serverUri)
            .OrderByDescending(connection => connection.LastUsage)
            .Select(Clone)
            .FirstOrDefault();

    public string ToConfigString() =>
        JsonSerializer.Serialize(connections, AzureDevOpsConfigurationSerializer.SerializerOptions);

    public static AzureDevOpsConnectionCache FromConfigString(string? serialized)
    {
        var cache = new AzureDevOpsConnectionCache();
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return cache;
        }

        try
        {
            AzureDevOpsConnection[]? saved =
                JsonSerializer.Deserialize<AzureDevOpsConnection[]>(serialized, AzureDevOpsConfigurationSerializer.SerializerOptions);
            foreach (AzureDevOpsConnection connection in saved?.Take(Capacity) ?? [])
            {
                if (connection.IsPopulated)
                {
                    cache.Add(connection);
                }
            }
        }
        catch (JsonException)
        {
        }

        return cache;
    }

    internal static AzureDevOpsConnection Clone(AzureDevOpsConnection source)
    {
        return new AzureDevOpsConnection
        {
            ServerUri = source.ServerUri,
            Project = source.Project is null
                ? null
                : new AzureDevOpsProject { Name = source.Project.Name, Id = source.Project.Id },
            Team = source.Team is null
                ? null
                : new AzureDevOpsTeam
                {
                    Name = source.Team.Name,
                    Id = source.Team.Id,
                    ParentProject = source.Team.ParentProject is null
                        ? null
                        : new AzureDevOpsProject
                        {
                            Name = source.Team.ParentProject.Name,
                            Id = source.Team.ParentProject.Id,
                        },
                },
            LastUsage = source.LastUsage,
        };
    }
}

public sealed class AzureDevOpsConfiguration
{
    public int ZoomLevel { get; set; } = 100;
    public AzureDevOpsConnection? SavedConnection { get; set; }
    public AzureDevOpsConnectionCache CachedConnections { get; set; } = new();
}

public static class AzureDevOpsConfigurationSerializer
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public static string Serialize(AzureDevOpsConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var contract = new SerializedConfiguration
        {
            ZoomLevel = configuration.ZoomLevel,
            SavedConnection = configuration.SavedConnection is null
                ? null
                : AzureDevOpsConnectionCache.Clone(configuration.SavedConnection),
            CachedConnections = [.. configuration.CachedConnections.Connections.Select(AzureDevOpsConnectionCache.Clone)],
        };
        return JsonSerializer.Serialize(contract, SerializerOptions);
    }

    public static AzureDevOpsConfiguration Deserialize(string? serialized)
    {
        var configuration = new AzureDevOpsConfiguration();
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return configuration;
        }

        try
        {
            if (JsonNode.Parse(serialized) is not JsonObject root)
            {
                return configuration;
            }

            if (root["ZoomLevel"] is JsonValue zoomValue && zoomValue.TryGetValue(out int zoomLevel))
            {
                configuration.ZoomLevel = Math.Clamp(zoomLevel, 25, 1000);
            }

            JsonNode? savedConnectionNode = root["SavedConnection"];
            if (savedConnectionNode is null &&
                root["SerializedSavedConnection"] is JsonValue savedStringValue &&
                savedStringValue.TryGetValue(out string? savedString) &&
                !string.IsNullOrWhiteSpace(savedString))
            {
                savedConnectionNode = JsonNode.Parse(savedString);
            }

            if (savedConnectionNode is not null)
            {
                configuration.SavedConnection =
                    savedConnectionNode.Deserialize<AzureDevOpsConnection>(SerializerOptions);
            }

            IEnumerable<AzureDevOpsConnection> cachedConnections = ReadCachedConnections(root);
            foreach (AzureDevOpsConnection connection in cachedConnections)
            {
                if (connection.IsPopulated)
                {
                    configuration.CachedConnections.Add(connection);
                }
            }
        }
        catch (JsonException)
        {
        }

        return configuration;
    }

    private static IEnumerable<AzureDevOpsConnection> ReadCachedConnections(JsonObject root)
    {
        JsonNode? cached = root["CachedConnections"];
        if (cached is JsonObject cacheObject)
        {
            cached = cacheObject["Connections"];
        }
        else if (cached is JsonValue cachedStringValue &&
            cachedStringValue.TryGetValue(out string? cachedString) &&
            !string.IsNullOrWhiteSpace(cachedString))
        {
            cached = JsonNode.Parse(cachedString);
        }

        if (cached is null &&
            root["SerializedCachedConnections"] is JsonValue legacyValue &&
            legacyValue.TryGetValue(out string? legacyString) &&
            !string.IsNullOrWhiteSpace(legacyString))
        {
            cached = JsonNode.Parse(legacyString);
        }

        return cached is JsonArray array
            ? array
                .Select(node => node?.Deserialize<AzureDevOpsConnection>(SerializerOptions))
                .OfType<AzureDevOpsConnection>()
            : [];
    }

    private sealed class SerializedConfiguration
    {
        public int ZoomLevel { get; set; } = 100;
        public AzureDevOpsConnection? SavedConnection { get; set; }
        public AzureDevOpsConnection[]? CachedConnections { get; set; }
    }
}

public static class AzureDevOpsServerUrl
{
    public static bool TryNormalize(string? value, out Uri? normalized)
    {
        normalized = null;
        if (!Uri.TryCreate(value?.Trim().TrimEnd('/'), UriKind.Absolute, out Uri? candidate) ||
            !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(candidate.Host) ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            !string.IsNullOrEmpty(candidate.Query) ||
            !string.IsNullOrEmpty(candidate.Fragment))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }
}
