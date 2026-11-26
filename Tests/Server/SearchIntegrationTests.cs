using HarmonyLib;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SPTarkov.DI;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Ragfair;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

namespace Liquidwarp.HideLockedFlea.Server.Tests;

[NonParallelizable]
public class SearchIntegrationTests
{
    private AbstractPatch[] _patches = [];
    private readonly RagfairCategoriesService _categories = new(null!);
    private FleaMarket _market = null!;

    [OneTimeSetUp]
    public async Task LoadMod()
    {
        var services = new ServiceCollection();
        var json = new JsonUtil([new SptJsonConverterRegistrator()]);
        services.AddSingleton(json);
        services.AddSingleton(new HttpResponseUtil(json, null!));
        var injection = new DependencyInjectionHandler(services);
        injection.AddInjectableTypesFromAssembly(typeof(Mod).Assembly);
        injection.InjectAll();

        using var provider = services.BuildServiceProvider();
        _patches = provider.GetServices<IRuntimePatch>().Cast<AbstractPatch>().ToArray();
        Assert.That(_patches.Select(patch => patch.GetType()), Is.EquivalentTo(new[]
        {
            typeof(GetOffersPatch), typeof(GetOffersForSearchTypePatch), typeof(GetCategoriesFromOffersPatch),
        }));
        await provider.GetRequiredService<IOnLoad>().OnLoadAsync(CancellationToken.None);
        Assert.That(_patches.All(patch => Harmony.HasAnyPatches(patch.HarmonyId)), Is.True);
        Assert.That(provider.GetRequiredService<StaticRouter>().CanHandle(FleaMarket.SettingsUrl), Is.True);
    }

    [OneTimeTearDown]
    public void UnloadMod()
    {
        foreach (var patch in _patches)
            new Harmony(patch.HarmonyId).UnpatchSelf();
    }

    [SetUp]
    public void NewMarket() => _market = new();

    [Test]
    public async Task SettingsRouteControlsOnlyTheRequestingProfile()
    {
        var other = new MongoId();
        var locked = _market.Offer(loyalty: 4);
        Assert.That(_market.Router.CanHandle(FleaMarket.SettingsUrl), Is.True);
        Assert.That(_market.Search(_market.Session, locked), Has.Count.EqualTo(1));

        var response = await _market.SetFilter(_market.Session, true);
        using var body = System.Text.Json.JsonDocument.Parse((string)response);
        Assert.That(body.RootElement.GetProperty("err").GetInt32(), Is.Zero);
        Assert.That(_market.Search(_market.Session, locked), Is.Empty);
        Assert.That(_market.Search(other, locked), Has.Count.EqualTo(1));

        await _market.SetFilter(other, true);
        await _market.SetFilter(_market.Session, false);
        Assert.That(_market.Search(_market.Session, locked), Has.Count.EqualTo(1));
        Assert.That(_market.Search(other, locked), Is.Empty);
    }

    [Test]
    public async Task GlobalCategoryCountsHonorTheSameLocksAndSptsOwnSellerFilter()
    {
        var player = _market.Offer(OfferCreator.Player);
        var buyable = _market.Offer();
        var locked = _market.Offer(loyalty: 4);
        var lockedOnly = _market.Offer(loyalty: 4, template: new MongoId());
        RagfairOffer[] globalPool = [player, lockedOnly, locked, buyable];
        var request = new SearchRequestData();
        await _market.SetFilter(_market.Session, true);
        GetOffersPatch.Prefix(_market.Session);
        try
        {
            // An ordinary search can return one template while SPT counts the entire market.
            List<RagfairOffer> results = [locked, buyable, player];
            GetOffersForSearchTypePatch.Postfix(_market.Assorts, _market.Profile, results);
            Assert.That(results, Is.EqualTo(new[] { buyable, player }));
            var counts = _categories.GetCategoriesFromOffers(globalPool, request, true);
            Assert.That(counts, Has.Count.EqualTo(1));
            Assert.That(counts[_market.Template], Is.EqualTo(2));

            request.OfferOwnerType = OfferOwnerType.TraderOwnerType;
            Assert.That(_categories.GetCategoriesFromOffers(globalPool, request, true)[_market.Template], Is.EqualTo(1));
        }
        finally
        {
            GetOffersPatch.Finalizer();
        }

        var outsideSearch = _categories.GetCategoriesFromOffers(globalPool, new(), true);
        Assert.That(outsideSearch[_market.Template], Is.EqualTo(3));
        Assert.That(outsideSearch[lockedOnly.Items![0].Template], Is.EqualTo(1));
    }

    [Test]
    public async Task OverlappingProfilesOnDifferentThreadsHaveIndependentOfferAndCategoryFilters()
    {
        var other = new MongoId();
        var offer = _market.Offer(loyalty: 4);
        await _market.SetFilter(_market.Session, true);
        GetOffersPatch.Prefix(_market.Session);
        try
        {
            GetOffersForSearchTypePatch.Postfix(_market.Assorts, _market.Profile, [offer]);
            var otherCounts = Task.Factory.StartNew(() =>
            {
                var before = _categories.GetCategoriesFromOffers([offer], new(), true);
                var results = _market.Search(other, offer);
                return (before.Count, results.Count);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).GetAwaiter().GetResult();

            Assert.That(otherCounts, Is.EqualTo((1, 1)));
            Assert.That(_categories.GetCategoriesFromOffers([offer], new(), true), Is.Empty);
            List<RagfairOffer> ownResults = [offer];
            GetOffersForSearchTypePatch.Postfix(_market.Assorts, _market.Profile, ownResults);
            Assert.That(ownResults, Is.Empty);
        }
        finally
        {
            GetOffersPatch.Finalizer();
        }
    }

    [Test]
    public async Task NormalSearchFiltersGlobalCategoryCountsEvenWhenThePageIsEmpty()
    {
        var locked = _market.Offer(template: new MongoId());
        var player = _market.Offer(OfferCreator.Player);
        var controller = CreateController(new MongoId(), locked, player);
        var request = new SearchRequestData { BuildCount = 0, BuildItems = [], Limit = 15, UpdateOfferCount = true };

        var unfiltered = controller.GetOffers(_market.Session, request);
        Assert.That(unfiltered.Offers, Is.Empty);
        Assert.That(unfiltered.Categories, Has.Count.EqualTo(2));

        await _market.SetFilter(_market.Session, true);
        var filtered = controller.GetOffers(_market.Session, request);
        Assert.That(filtered.Offers, Is.Empty);
        Assert.That(filtered.Categories, Has.Count.EqualTo(1));
        Assert.That(filtered.Categories![_market.Template], Is.EqualTo(1));
    }

    [Test]
    public async Task PatchedControllerReturnsFilteredCountsAndPriceSortedPages()
    {
        var locked = _market.Offer(template: new MongoId());
        var firstPlayer = _market.Offer(OfferCreator.Player);
        var secondPlayer = _market.Offer(OfferCreator.Player);
        var requiredItem = new MongoId();
        var controller = CreateController(requiredItem, secondPlayer, firstPlayer, locked);
        var request = new SearchRequestData
        {
            NeededSearchId = requiredItem,
            BuildCount = 0,
            Limit = 1,
            Page = 1,
            SortType = RagfairSort.PRICE,
            SortDirection = 1,
            UpdateOfferCount = true,
        };

        // The highest-priced listing belongs to a trader absent from the displayable assort.
        var unfiltered = controller.GetOffers(_market.Session, request);
        Assert.That(unfiltered.OffersCount, Is.EqualTo(3));
        Assert.That(unfiltered.Offers, Is.EqualTo(new[] { firstPlayer }));
        Assert.That(unfiltered.Categories, Has.Count.EqualTo(2));

        await _market.SetFilter(_market.Session, true);
        var filtered = controller.GetOffers(_market.Session, request);
        Assert.That(filtered.OffersCount, Is.EqualTo(2));
        Assert.That(filtered.Offers, Is.EqualTo(new[] { secondPlayer }));
        Assert.That(filtered.Categories, Has.Count.EqualTo(1));
        Assert.That(filtered.Categories![_market.Template], Is.EqualTo(2));
        Assert.That(_categories.GetCategoriesFromOffers([locked], new(), true), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task FailedSearchReleasesItsOfferAndCategoryFilters()
    {
        var locked = _market.Offer(loyalty: 4);
        var requiredItem = new MongoId();
        var controller = CreateController(requiredItem, locked);
        await _market.SetFilter(_market.Session, true);

        // Sorting follows offer filtering. Fail here to exercise the installed Harmony finalizer.
        var fault = new Harmony("net.liquidwarp.hidelockedflea.tests.sort");
        try
        {
            fault.Patch(AccessTools.Method(typeof(RagfairSortHelper), nameof(RagfairSortHelper.SortOffers)),
                prefix: new HarmonyMethod(typeof(SearchIntegrationTests), nameof(InterruptSort)));
            var request = new SearchRequestData { NeededSearchId = requiredItem, BuildCount = 0, Limit = 1 };
            Assert.Throws<OperationCanceledException>(() => controller.GetOffers(_market.Session, request));
        }
        finally
        {
            fault.UnpatchSelf();
        }

        Assert.That(_categories.GetCategoriesFromOffers([locked], new(), true), Has.Count.EqualTo(1));
        List<RagfairOffer> results = [locked];
        GetOffersForSearchTypePatch.Postfix(_market.Assorts, _market.Profile, results);
        Assert.That(results, Is.EqualTo(new[] { locked }));
    }

    private static void InterruptSort() => throw new OperationCanceledException("Interrupted test search");

    private RagfairController CreateController(MongoId requiredItem, params RagfairOffer[] offers)
    {
        // Build an isolated market from an in-memory profile, offer index and flea settings.
        var globals = new GlobalTable
        {
            Configuration = new() { RagFair = new() { MinUserLevel = 15 }, Mastering = [], RestrictionsInRaid = [],
                EventType = [], ArenaEftTransferSettings = null! },
            BotPresets = [], BotWeaponScatterings = [], ItemPresets = [], LocationInfection = [],
        };
        var fleaConfig = new RagfairConfig
        {
            Traders = [], TieredFlea = new() { UnlocksTpl = [], UnlocksType = [] }, RunIntervalValues = null!, Sell = null!, Dynamic = null!,
        };
        var payment = new PaymentHelper(new InventoryConfig
        {
            CustomMoneyTpls = [], DeprioritisedMoneyContainers = [], SkillGainMultipliers = [], RandomLootContainers = [],
            SealedAirdropContainer = null!,
        });
        var saves = SptService<SaveServer>();
        _market.Profile.Info = new() { Level = 20 };
        saves.AddProfile(new() { ProfileInfo = new() { ProfileId = _market.Session }, CharacterData = new() { PmcData = _market.Profile } });
        var profiles = SptService<ProfileHelper>(saves);

        var holder = SptService<RagfairOfferHolder>();
        for (var index = 0; index < offers.Length; index++)
        {
            var offer = offers[index];
            offer.Requirements = [new() { TemplateId = requiredItem, Count = 1 }];
            offer.RequirementsCost = index + 1;
            holder.AddOffer(offer);
        }
        var offerService = SptService<RagfairOfferService>(holder);
        var requiredItems = new RagfairRequiredItemsService(offerService, payment);
        var offerHelper = SptService<RagfairOfferHelper>(globals, fleaConfig, offerService, requiredItems, new TimeUtil());
        var fleaHelper = SptService<RagfairHelper>(new TradersTable(), fleaConfig);
        return SptService<RagfairController>(globals, profiles, fleaHelper, new RagfairSortHelper(null!), offerHelper, offerService, _categories);
    }

    // SPT services take dependencies for many unrelated operations. Supply only those used by this search.
    private static T SptService<T>(params object[] dependencies)
    {
        var constructor = typeof(T).GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select(parameter => dependencies.SingleOrDefault(parameter.ParameterType.IsInstanceOfType)).ToArray();
        return (T)constructor.Invoke(arguments);
    }
}
