using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace Liquidwarp.HideLockedFlea.Server;

internal static class ProfileSettings
{
    private static readonly ConcurrentDictionary<MongoId, bool> HideLockedByProfile = new();

    public static bool HideLocked(MongoId profileId) => HideLockedByProfile.GetValueOrDefault(profileId);

    public static void SetHideLocked(MongoId profileId, bool hideLocked) => HideLockedByProfile[profileId] = hideLocked;
}

public record SettingsRequestData : IRequestData
{
    [JsonPropertyName("hideLocked")]
    public bool HideLocked { get; set; }
}

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class SettingsRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil) : StaticRouter(
    jsonUtil,
    [
        new RouteAction<SettingsRequestData>(
            "/hidelockedflea/settings",
            (url, request, sessionId, output, cancellationToken) =>
            {
                ProfileSettings.SetHideLocked(sessionId, request.HideLocked);
                return new ValueTask<string>(httpResponseUtil.NullResponse());
            })
    ]);
