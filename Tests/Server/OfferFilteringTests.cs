using NUnit.Framework;
using SPTarkov.Server.Core.Models.Enums;

namespace Liquidwarp.HideLockedFlea.Server.Tests;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class OfferFilteringTests
{
    private readonly FleaMarket _market = new();

    [SetUp]
    public async Task EnableFilter() => await _market.SetFilter(_market.Session, true);

    [TestCase(true, false, 1, 2, false)]
    [TestCase(true, false, 2, 2, true)]
    [TestCase(true, false, 3, 2, true)]
    [TestCase(false, false, 4, 1, false)]
    [TestCase(true, true, 4, 1, false)]
    [TestCase(null, null, 2, 2, true)]
    [TestCase(true, false, null, 2, true)]
    [TestCase(true, false, 1, null, true)]
    public void TraderAccessAndLoyaltyControlVisibility(bool? unlocked, bool? disabled, int? loyalty, int? required, bool visible)
    {
        _market.TraderInfo.Unlocked = unlocked;
        _market.TraderInfo.Disabled = disabled;
        _market.TraderInfo.LoyaltyLevel = loyalty;
        var offer = _market.Offer(loyalty: required);

        Assert.That(_market.Search(_market.Session, offer).Contains(offer), Is.EqualTo(visible));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingTraderStandingFallsBackToTheAssort(bool missingDictionary)
    {
        _market.Profile.TradersInfo = missingDictionary ? null : [];
        var available = _market.Offer();
        var missing = _market.Offer();
        _market.Assort.Items.RemoveAll(item => item.Id == missing.Root);

        Assert.That(_market.Search(_market.Session, missing, available), Is.EqualTo(new[] { available }));
    }

    [Test]
    public void AQuestLockOnALaterBarterAlternativeOnlyHidesThatAssortRoot()
    {
        var locked = _market.Offer();
        var unlocked = _market.Offer();
        _market.Assort.BarterScheme[locked.Root] = [[new()], [new() { SptQuestLocked = false }, new() { SptQuestLocked = true }]];
        _market.Assort.BarterScheme[unlocked.Root] = [[new(), new() { SptQuestLocked = false }]];

        Assert.That(_market.Search(_market.Session, unlocked, locked), Is.EqualTo(new[] { unlocked }));

        _market.Assort.BarterScheme[locked.Root][1][1].SptQuestLocked = false;
        Assert.That(_market.Search(_market.Session, locked), Is.EqualTo(new[] { locked }));
    }

    [Test]
    public void TwoTradersSellingTheSameItemUseTheirOwnLoyaltyLevels()
    {
        var second = new FleaMarket();
        var locked = _market.Offer(loyalty: 3);
        var available = second.Offer(loyalty: 3, template: _market.Template);
        second.TraderInfo.LoyaltyLevel = 3;
        _market.Assorts.Add(second.Trader, second.Assort);
        _market.Profile.TradersInfo!.Add(second.Trader, second.TraderInfo);

        Assert.That(_market.Search(_market.Session, locked, available), Is.EqualTo(new[] { available }));
    }

    [TestCase(OfferCreator.Player)]
    [TestCase(OfferCreator.FakePlayer)]
    public void TierLockedPlayerOffersSurviveEvenWhenTheTraderListingForTheSameItemIsLocked(OfferCreator creator)
    {
        var trader = _market.Offer(loyalty: 4);
        var player = _market.Offer(creator);
        player.Locked = true;

        Assert.That(_market.Search(_market.Session, trader, player), Is.EqualTo(new[] { player }));
        Assert.That(player.Locked, Is.True);
    }

    [Test]
    public void OffersWithoutCreatorMetadataUseSptsSellerClassification()
    {
        var trader = _market.Offer(loyalty: 4);
        var player = _market.Offer(OfferCreator.Player);
        trader.CreatedBy = player.CreatedBy = null;

        Assert.That(_market.Search(_market.Session, trader, player), Is.EqualTo(new[] { player }));
    }

    [Test]
    public void ExhaustedStockAndPersonalPurchaseLimitsStayVisible()
    {
        var soldOut = _market.Offer();
        soldOut.Quantity = 0;
        soldOut.Items![0].Upd!.StackObjectsCount = 0;
        var atLimit = _market.Offer();
        atLimit.BuyRestrictionCurrent = atLimit.BuyRestrictionMax = 3;

        Assert.That(_market.Search(_market.Session, soldOut, atLimit), Is.EqualTo(new[] { soldOut, atLimit }));
        Assert.That(soldOut.Items[0].Upd!.StackObjectsCount, Is.Zero);
        Assert.That(atLimit.BuyRestrictionCurrent, Is.EqualTo(3));
    }
}
