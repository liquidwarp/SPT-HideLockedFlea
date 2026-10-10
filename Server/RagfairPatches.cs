namespace HideLockedFleaServer;

using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Ragfair;

internal static class CurrentSearch
{
    [ThreadStatic] public static TraderLocks? TraderLocks;
}

internal sealed class TraderLocks(Dictionary<MongoId, TraderAssort> traderAssorts, PmcData pmcProfile)
{
    private readonly HashSet<MongoId> _questLockedAssortIds = traderAssorts.Values
        .SelectMany(assort => assort.BarterScheme)
        .Where(scheme => scheme.Value.Any(barters => barters.Any(barter => barter.SptQuestLocked == true)))
        .Select(scheme => scheme.Key)
        .ToHashSet();

    public bool IsLocked(RagfairOffer offer)
    {
        if (!offer.IsTraderOffer())
            return false;
        if (!traderAssorts.ContainsKey(offer.User.Id))
            return true;
        if (pmcProfile.TradersInfo is { } tradersInfo && tradersInfo.TryGetValue(offer.User.Id, out var trader)
            && (trader.Unlocked == false || trader.Disabled == true || trader.LoyaltyLevel < offer.LoyaltyLevel))
            return true;

        return offer.Items?.Any(item => _questLockedAssortIds.Contains(item.Id)) == true;
    }
}

// Drops locked offers before the server counts and paginates them.
[Injectable]
public class GetOffersForSearchTypePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(RagfairController), "GetOffersForSearchType");

    [PatchPostfix]
    public static void Postfix(Dictionary<MongoId, TraderAssort> traderAssorts, PmcData pmcProfile, List<RagfairOffer> __result)
    {
        var traderLocks = CurrentSearch.TraderLocks = new TraderLocks(traderAssorts, pmcProfile);
        __result.RemoveAll(offer => offer.Locked == true || traderLocks.IsLocked(offer));
    }
}

[Injectable]
public class GetSpecificCategoriesPatch : AbstractPatch
{
    private static RagfairOfferService _ragfairOfferService = null!;
    private static RagfairCategoriesService _ragfairCategoriesService = null!;
    private static GlobalTable _globalTable = null!;
    private static RagfairConfig _ragfairConfig = null!;
    private static Action<TieredFlea, RagfairOffer, HashSet<MongoId>, int> _checkAndLockOfferFromPlayerTieredFlea = null!;

    public GetSpecificCategoriesPatch(
        RagfairOfferService ragfairOfferService,
        RagfairCategoriesService ragfairCategoriesService,
        RagfairOfferHelper ragfairOfferHelper,
        GlobalTable globalTable,
        RagfairConfig ragfairConfig)
    {
        _ragfairOfferService = ragfairOfferService;
        _ragfairCategoriesService = ragfairCategoriesService;
        _globalTable = globalTable;
        _ragfairConfig = ragfairConfig;
        _checkAndLockOfferFromPlayerTieredFlea = AccessTools.MethodDelegate<Action<TieredFlea, RagfairOffer, HashSet<MongoId>, int>>(
            AccessTools.Method(typeof(RagfairOfferHelper), "CheckAndLockOfferFromPlayerTieredFlea"), ragfairOfferHelper);
    }

    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(RagfairController), "GetSpecificCategories");

    [PatchPostfix]
    public static void Postfix(PmcData pmcProfile, SearchRequestData searchRequest, Dictionary<MongoId, int> __result)
    {
        if (CurrentSearch.TraderLocks is not { } traderLocks)
            return;

        if (searchRequest.LinkedSearchId is { IsEmpty: false } || searchRequest.NeededSearchId is { IsEmpty: false })
            return;

        var tieredFlea = _ragfairConfig.TieredFlea;
        var tieredFleaLimitTypes = tieredFlea.Enabled ? tieredFlea.UnlocksType.Keys.ToHashSet() : [];
        var playerLevel = pmcProfile.Info!.Level!.Value;

        var locked = _ragfairOfferService.GetOffers().Where(offer => offer.IsTraderOffer()
            ? traderLocks.IsLocked(offer)
            : tieredFlea.Enabled && IsTieredFleaLocked(offer, tieredFlea, tieredFleaLimitTypes, playerLevel));

        var fleaUnlocked = playerLevel >= _globalTable.Configuration.RagFair.MinUserLevel;
        foreach (var (template, count) in _ragfairCategoriesService.GetCategoriesFromOffers(locked, searchRequest, fleaUnlocked))
        {
            if (!__result.TryGetValue(template, out var total))
                continue;

            if (total > count)
                __result[template] = total - count;
            else
                __result.Remove(template);
        }
    }

    private static bool IsTieredFleaLocked(RagfairOffer offer, TieredFlea tieredFlea, HashSet<MongoId> limitTypes, int playerLevel)
    {
        var copy = offer with { User = offer.User with { } };
        _checkAndLockOfferFromPlayerTieredFlea(tieredFlea, copy, limitTypes, playerLevel);
        return copy.Locked == true;
    }
}
