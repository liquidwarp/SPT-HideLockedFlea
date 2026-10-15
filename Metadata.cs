using System.Collections.Generic;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace Liquidwarp.HideLockedFlea;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = MyModInfo.MOD_GUID;
    public string Name { get; init; } = MyModInfo.MOD_NAME;
    public string Author { get; init; } = MyModInfo.MOD_AUTHOR;
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new(MyModInfo.MOD_VERSION);
    public SemanticVersioning.Range SptVersion { get; init; } = new(MyModInfo.SERVER_SPT_VERSION);
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; }
}
