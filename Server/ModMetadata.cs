namespace HideLockedFleaServer;

using SPTarkov.Server.Core.Models.Spt.Mod;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.liquidwarp.hidelockedflea";
    public string Name { get; init; } = "Hide Locked Flea";
    public string Author { get; init; } = "liquidwarp";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.6");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; } = false;
}
