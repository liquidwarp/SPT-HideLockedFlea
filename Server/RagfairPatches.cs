using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Ragfair;

namespace Liquidwarp.HideLockedFlea.Server;

internal static class CurrentSearch
{
    [ThreadStatic] public static bool HideLocked;
    [ThreadStatic] public static TraderLocks? TraderLocks;
}

internal sealed class TraderLocks(Dictionary<MongoId, TraderAssort> traderAssorts, PmcData? pmcProfile)
{
    private readonly Dictionary<MongoId, HashSet<MongoId>> _assortIds =
        traderAssorts.ToDictionary(pair => pair.Key, pair => pair.Value.Items.Select(item => item.Id).ToHashSet());

    public bool IsLocked(RagfairOffer offer)
    {
        if (!offer.IsTraderOffer())
            return false;
        var seller = offer.User.Id;
        return !_assortIds.TryGetValue(seller, out var ids) || !ids.Contains(offer.Root)
            || TraderUnavailable(offer, pmcProfile) || HasQuestLockedItem(offer, traderAssorts[seller]);
    }

    internal static bool TraderUnavailable(RagfairOffer offer, PmcData? profile) =>
        profile?.TradersInfo?.TryGetValue(offer.User.Id, out var trader) == true
        && (trader.Unlocked == false || trader.Disabled == true || trader.LoyaltyLevel < offer.LoyaltyLevel);

    internal static bool HasQuestLockedItem(RagfairOffer offer, TraderAssort assort)
    {
        bool IsFlagged(MongoId id) => assort.BarterScheme.TryGetValue(id, out var alternatives)
            && alternatives.Any(barters => barters.Any(barter => barter.SptQuestLocked == true));

        if (IsFlagged(offer.Root))
            return true;
        if (offer.Items is { } items)
        {
            foreach (var item in items)
            {
                if (item.Id != offer.Root && IsFlagged(item.Id))
                    return true;
            }
        }
        return false;
    }
}

[Injectable]
public class GetOfferByInternalIdPatch : AbstractPatch
{
    private static Func<MongoId, RagfairOffer, bool> _isLocked = null!;

    public GetOfferByInternalIdPatch(TradersTable traders, RagfairConfig flea, TraderAssortHelper assorts, ProfileHelper profiles)
    {
        _isLocked = (sessionId, offer) =>
        {
            var seller = offer.User.Id;
            if (!traders.ContainsKey(seller) || !flea.Traders.ContainsKey(seller)
                || TraderLocks.TraderUnavailable(offer, profiles.GetPmcProfile(sessionId)))
                return true;

            var assort = assorts.GetAssort(sessionId, seller, showLockedAssorts: true);
            return !assort.Items.Exists(item => item.Id == offer.Root) || TraderLocks.HasQuestLockedItem(offer, assort);
        };
    }

    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(RagfairController), nameof(RagfairController.GetOfferByInternalId));

    [PatchPostfix]
    public static void Postfix(MongoId sessionId, ref RagfairOffer? __result)
    {
        if (__result is { } offer && offer.IsTraderOffer() && ProfileSettings.HideLocked(sessionId) && _isLocked(sessionId, offer))
            __result = null;
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
    public static void Postfix(Dictionary<MongoId, TraderAssort> traderAssorts, PmcData pmcProfile)
    {
        if (CurrentSearch.HideLocked)
            CurrentSearch.TraderLocks = new TraderLocks(traderAssorts, pmcProfile);
    }
}

[Injectable]
public class AddIndexValueToOffersPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(RagfairController), "AddIndexValueToOffers");

    [PatchPostfix]
    public static void Postfix(IEnumerable<RagfairOffer> offers)
    {
        // GetOffers passes its results list here. Keep SPT's numeric IDs, then filter before sorting and paging.
        if (CurrentSearch.TraderLocks is { } locks && offers is List<RagfairOffer> results)
            results.RemoveAll(locks.IsLocked);
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
