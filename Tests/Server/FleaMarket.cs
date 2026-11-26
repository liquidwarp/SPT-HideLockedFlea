using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

namespace Liquidwarp.HideLockedFlea.Server.Tests;

internal sealed class FleaMarket
{
    public const string SettingsUrl = "/hidelockedflea/settings";
    public readonly MongoId Session = new();
    public readonly MongoId Trader = new();
    public readonly MongoId Template = new();
    public readonly TraderAssort Assort = new() { Items = [], BarterScheme = [], LoyalLevelItems = [] };
    public readonly TraderInfo TraderInfo = new() { Unlocked = true, Disabled = false, LoyaltyLevel = 2 };
    public readonly PmcData Profile;
    public readonly Dictionary<MongoId, TraderAssort> Assorts;
    public readonly SettingsRouter Router;

    public FleaMarket()
    {
        Assorts = new() { [Trader] = Assort };
        Profile = new() { TradersInfo = new() { [Trader] = TraderInfo } };
        var json = new JsonUtil([new SptJsonConverterRegistrator()]);
        Router = new(json, new HttpResponseUtil(json, null!));
    }

    public ValueTask<object> SetFilter(MongoId session, bool enabled) =>
        Router.HandleStaticAsync(SettingsUrl, enabled ? """{"hideLocked":true}""" : """{"hideLocked":false}""", session, "");

    public RagfairOffer Offer(OfferCreator creator = OfferCreator.Trader, int? loyalty = 2, MongoId? template = null)
    {
        var item = new Item { Id = new MongoId(), Template = template ?? Template, Upd = new() { StackObjectsCount = 5 } };
        if (creator == OfferCreator.Trader)
            Assort.Items.Add(item);
        return new()
        {
            Id = new MongoId(),
            Root = item.Id,
            Items = [item],
            CreatedBy = creator,
            User = new()
            {
                Id = creator == OfferCreator.Trader ? Trader : new MongoId(),
                MemberType = creator == OfferCreator.Trader ? MemberCategory.Trader : MemberCategory.Default,
            },
            LoyaltyLevel = loyalty,
            Quantity = 5,
        };
    }

    public List<RagfairOffer> Search(MongoId session, params RagfairOffer[] offers)
    {
        GetOffersPatch.Prefix(session);
        try
        {
            List<RagfairOffer> results = [.. offers];
            GetOffersForSearchTypePatch.Postfix(Assorts, Profile, results);
            return results;
        }
        finally
        {
            GetOffersPatch.Finalizer();
        }
    }
}
