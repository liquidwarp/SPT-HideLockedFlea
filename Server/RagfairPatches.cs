using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Services.Ragfair;

namespace Liquidwarp.HideLockedFlea.Server;

internal static class CurrentSearch
{
    [ThreadStatic] public static bool HideLocked;
    [ThreadStatic] public static TraderLocks? TraderLocks;
}

internal sealed class TraderLocks(Dictionary<MongoId, TraderAssort> traderAssorts, PmcData pmcProfile)
{
    // SPT already builds the trader stocks with quest locked items flagged.
    private readonly HashSet<MongoId> _buyableAssortIds = traderAssorts.Values
        .SelectMany(assort => assort.Items.Select(item => item.Id))
        .Except(traderAssorts.Values
            .SelectMany(assort => assort.BarterScheme)
            .Where(scheme => scheme.Value.Any(barters => barters.Any(barter => barter.SptQuestLocked == true)))
            .Select(scheme => scheme.Key))
        .ToHashSet();

    public bool IsLocked(RagfairOffer offer)
    {
        if (!offer.IsTraderOffer())
            return false;
        if (!_buyableAssortIds.Contains(offer.Root))
            return true;

        return pmcProfile.TradersInfo is { } tradersInfo 
            && tradersInfo.TryGetValue(offer.User.Id, out var trader)
            && (trader.Unlocked == false || trader.Disabled == true || trader.LoyaltyLevel < offer.LoyaltyLevel);
    }
}

[Injectable]
public class GetOffersPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(RagfairController), nameof(RagfairController.GetOffers));

    [PatchPrefix]
    public static void Prefix(MongoId sessionID) => CurrentSearch.HideLocked = ProfileSettings.HideLocked(sessionID);

    [PatchFinalizer]
    public static void Finalizer()
    {
        CurrentSearch.HideLocked = false;
        CurrentSearch.TraderLocks = null;
    }
}

[Injectable]
public class GetOffersForSearchTypePatch : AbstractPatch
{
    // The method is protected, have to refer to it by string name.
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(RagfairController), "GetOffersForSearchType");

    [PatchPostfix]
    public static void Postfix(Dictionary<MongoId, TraderAssort> traderAssorts, PmcData pmcProfile, List<RagfairOffer> __result)
    {
        if (!CurrentSearch.HideLocked)
            return;

        var traderLocks = CurrentSearch.TraderLocks = new TraderLocks(traderAssorts, pmcProfile);
        __result.RemoveAll(traderLocks.IsLocked);
    }
}

[Injectable]
public class GetCategoriesFromOffersPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(RagfairCategoriesService), nameof(RagfairCategoriesService.GetCategoriesFromOffers));

    [PatchPrefix]
    public static void Prefix(ref IEnumerable<RagfairOffer> offers)
    {
        if (CurrentSearch.TraderLocks is { } traderLocks)
            offers = offers.Where(offer => !traderLocks.IsLocked(offer));
    }
}
