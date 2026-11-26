using System.Runtime.CompilerServices;
using EFT;
using EFT.UI;
using EFT.UI.Ragfair;
using HarmonyLib;
using Liquidwarp.HideLockedFlea.Server.Tests;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using SPTarkov.Server.Core.Models.Enums;
using UnityEngine.Events;

namespace Liquidwarp.HideLockedFlea.Client.Tests;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[NonParallelizable]
public class FilterSettingsTests
{
    private sealed class TestServices : FleaFilterServices
    {
        public readonly Dictionary<string, int> Preferences = [];
        public UpdatableToggle? Template;
        public string? Label;

        public override int ReadPreference(string key, int fallback) => Preferences.GetValueOrDefault(key, fallback);
        public override void SavePreference(string key, int value) => Preferences[key] = value;

        public override UpdatableToggle DuplicateToggle(UpdatableToggle original, string name, string label)
        {
            Template = original;
            Label = label;
            return ManagedControl<UpdatableToggle>();
        }
    }

    private readonly TestServices _services = new();
    private readonly RagfairFilterWindow _window = ManagedControl<RagfairFilterWindow>();
    private readonly UpdatableToggle _template = ManagedControl<UpdatableToggle>();
    private readonly DefaultUIButton _apply = Button();
    private readonly DefaultUIButton _reset = Button();
    private FleaFilterServices _original = null!;
    private static readonly Harmony RequestBuilder = new("net.liquidwarp.hidelockedflea.tests.requests");
    private static BackendRequestParams? _outgoingRequest;

    [OneTimeSetUp]
    public static void PatchRequestBuilder() => RequestBuilder.Patch(
        AccessTools.Method(typeof(BackendRequestParams), nameof(BackendRequestParams.CreateFromLegacyParams)),
        postfix: new HarmonyMethod(typeof(FleaRequestPatch), nameof(FleaRequestPatch.Postfix)));

    [OneTimeTearDown]
    public static void UnpatchRequestBuilder() => RequestBuilder.UnpatchSelf();

    [SetUp]
    public void SetUp()
    {
        _original = FleaFilterServices.Current;
        FleaFilterServices.Current = _services;
        ClearSavedFiltersPatch.Postfix();
        FilterWindowAwakePatch.Prefix(_window, _template, _apply, _reset);
    }

    [TearDown]
    public void TearDown()
    {
        ClearSavedFiltersPatch.Postfix();
        FilterWindowAwakePatch.HideLockedToggle = null;
        FleaFilterServices.Current = _original;
    }

    // Unity constructs these objects in a scene. The tests need only their managed state and button events.
    private static T ManagedControl<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static DefaultUIButton Button()
    {
        var button = ManagedControl<DefaultUIButton>();
        typeof(DefaultUIButton).GetField(nameof(DefaultUIButton.OnClick))!.SetValue(button, new UnityEvent());
        return button;
    }

    private RagFair OpenView(EViewListType view)
    {
        var flea = ManagedControl<RagFair>();
        flea.FilterRule = new FilterRule { ViewListType = view };
        Show(flea);
        return flea;
    }

    private void Show(RagFair flea)
    {
        AccessTools.Field(typeof(RagfairFilterWindow), "_ragfair").SetValue(_window, flea);
        FilterWindowShowPatch.Postfix(flea);
    }

    private void Apply(bool value)
    {
        FilterWindowAwakePatch.HideLockedToggle.SetIsOnWithoutNotify(value);
        _apply.OnClick.Invoke();
    }

    private static BackendRequestParams Request(string path) =>
        BackendRequestParams.CreateFromLegacyParams(new SendRequest { Url = "https://eft.test" + path });

    private static BackendRequestParams Search(EViewListType view)
    {
        GetOffersPatch.Prefix(new FilterRule { ViewListType = view }, out var previous);
        try
        {
            return Request("/client/ragfair/find");
        }
        finally
        {
            GetOffersPatch.Finalizer(previous);
        }
    }

    [Test]
    public void ReopeningTheWindowDisplaysTheCurrentViewAndButtonsActOnThatView()
    {
        Assert.That(_services.Template, Is.SameAs(_template));
        Assert.That(_services.Label, Is.EqualTo("Hide locked offers"));
        var toggle = FilterWindowAwakePatch.HideLockedToggle;
        toggle.Bind(_ => Assert.Fail("Displaying the filter fired its change callback"));
        var all = OpenView(EViewListType.AllOffers);
        Apply(true);
        Assert.That(all.ForceNextUpdate, Is.True);

        var wish = OpenView(EViewListType.WishList);
        Assert.That(toggle.isOn, Is.False);
        Apply(true);
        Assert.That(wish.ForceNextUpdate, Is.True);
        _reset.OnClick.Invoke();
        Assert.That(HideLockedSetting.Get(EViewListType.WishList), Is.False);

        Show(all);
        Assert.That(toggle.isOn, Is.True);
    }

    [Test]
    public void ApplyingOnlyRefreshesOnChangesAndPreservesAnAlreadyPendingRefresh()
    {
        var flea = OpenView(EViewListType.AllOffers);
        Assert.That(HideLockedSetting.Get(EViewListType.AllOffers), Is.False);
        Apply(false);
        Assert.That(flea.ForceNextUpdate, Is.False);

        Apply(true);
        Assert.That(flea.ForceNextUpdate, Is.True);
        Assert.That(HideLockedSetting.Get(EViewListType.AllOffers), Is.True);
        Assert.That(HideLockedSetting.Get(EViewListType.WishList), Is.False);
        Apply(true);
        Assert.That(flea.ForceNextUpdate, Is.True);

        flea.ForceNextUpdate = false;
        Apply(true);
        Assert.That(flea.ForceNextUpdate, Is.False);
        Apply(false);
        Assert.That(flea.ForceNextUpdate, Is.True);
        Assert.That(_services.Preferences, Is.Empty);
    }

    [Test]
    public void RememberSelectedReadsExistingKeysAndSavesOnlyTheChosenView()
    {
        const string allKey = "Liquidwarp.HideLockedFlea.DefaultFilterRule_AllOffers";
        const string wishKey = "Liquidwarp.HideLockedFlea.DefaultFilterRule_WishList";
        _services.Preferences[allKey] = 1;
        Assert.That(HideLockedSetting.Get(EViewListType.AllOffers), Is.True);

        OpenView(EViewListType.AllOffers);
        Apply(false);
        var wishList = OpenView(EViewListType.WishList);
        Apply(true);
        SaveRuleAsDefaultPatch.Postfix(wishList);
        Assert.That(_services.Preferences[allKey], Is.EqualTo(1));
        Assert.That(_services.Preferences[wishKey], Is.EqualTo(1));

        ClearSavedFiltersPatch.Postfix();
        Assert.That(HideLockedSetting.Get(EViewListType.AllOffers), Is.True);
        Assert.That(HideLockedSetting.Get(EViewListType.WishList), Is.True);
        Apply(false);
        SaveRuleAsDefaultPatch.Postfix(wishList);
        Assert.That(_services.Preferences[wishKey], Is.Zero);

        ClearSavedFiltersPatch.Postfix();
        Assert.That(HideLockedSetting.Get(EViewListType.AllOffers), Is.True);
        Assert.That(HideLockedSetting.Get(EViewListType.WishList), Is.False);
    }

    [TestCase(EViewListType.AllOffers, true)]
    [TestCase(EViewListType.WishList, true)]
    [TestCase(EViewListType.MyOffers, false)]
    [TestCase(EViewListType.WeaponBuild, false)]
    public void NativeResetUpdatesTheRememberedDefaultForTheViewsEftResets(EViewListType view, bool resets)
    {
        var flea = OpenView(view);
        Apply(true);
        SaveRuleAsDefaultPatch.Postfix(flea);
        flea.ForceNextUpdate = false;

        // EFT registers this listener in Awake, after the mod's prefix. Bypass the scene
        // refresh, native PlayerPrefs and window closing, but run EFT's actual reset decision.
        var nativeReset = AccessTools.Method(typeof(RagfairFilterWindow), "CG_Awake1").CreateDelegate<UnityAction>(_window);
        // UnityEvent skips scene objects without a native handle. Give it a managed listener.
        _reset.OnClick.AddListener(() => nativeReset());
        var scene = new Harmony("net.liquidwarp.hidelockedflea.tests.reset");
        var skip = new HarmonyMethod(typeof(FilterSettingsTests), nameof(SkipSceneOperation));
        try
        {
            scene.Patch(AccessTools.Method(typeof(RagFair), nameof(RagFair.AddSearchesInRule)), prefix: skip);
            scene.Patch(AccessTools.Method(typeof(RagFair), nameof(RagFair.SaveRuleAsDefault)), prefix: skip,
                postfix: new HarmonyMethod(typeof(SaveRuleAsDefaultPatch), nameof(SaveRuleAsDefaultPatch.Postfix)));
            scene.Patch(AccessTools.DeclaredMethod(typeof(UIElement), nameof(UIElement.Close)), prefix: skip);
            _reset.OnClick.Invoke();
        }
        finally
        {
            scene.UnpatchSelf();
        }

        Assert.That(HideLockedSetting.Get(view), Is.EqualTo(!resets), "current choice after Reset");
        Assert.That(flea.ForceNextUpdate, Is.EqualTo(resets));
        ClearSavedFiltersPatch.Postfix();
        Assert.That(HideLockedSetting.Get(view), Is.EqualTo(!resets), "remembered choice after reloading");
    }

    private static bool SkipSceneOperation() => false;

    [TestCase(EViewListType.Handbook)]
    [TestCase(EViewListType.RequirementsWindow)]
    public void ModResetListenerLeavesViewsWithoutAResetButtonAlone(EViewListType view)
    {
        var flea = OpenView(view);
        Apply(true);
        flea.ForceNextUpdate = false;
        _reset.OnClick.Invoke();
        Assert.That(HideLockedSetting.Get(view), Is.True);
        Assert.That(flea.ForceNextUpdate, Is.False);
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void ExactIdUsesAllOffersEvenBeforeTheFirstSearchAndAfterAWishlistSearch(bool all, bool wish)
    {
        _services.Preferences["Liquidwarp.HideLockedFlea.DefaultFilterRule_AllOffers"] = all ? 1 : 0;
        OpenView(EViewListType.WishList);
        Apply(wish);
        var session = BackendSession();
        var transport = new Harmony("net.liquidwarp.hidelockedflea.tests.lookup");
        var expected = "/client/ragfair/offer/findbyid" + (all ? "?hideLockedFlea=1" : "?hideLockedFlea=0");
        try
        {
            InterceptTransport<Offer>(transport);
            session.FindById(71, _ => { });
            Assert.That(_outgoingRequest?.BackendMethod, Is.EqualTo(expected));
            _outgoingRequest = null;
            Assert.That(Search(EViewListType.WishList).BackendMethod, Does.EndWith(wish ? "=1" : "=0"));
            session.FindById(72, _ => { });
            Assert.That(_outgoingRequest?.BackendMethod, Is.EqualTo(expected));
        }
        finally
        {
            transport.UnpatchSelf();
            _outgoingRequest = null;
        }
    }

    private static EftClientBackendSession BackendSession()
    {
        var session = ManagedControl<EftClientBackendSession>();
        AccessTools.Field(typeof(ClientBackendSession), "_backendUrls")
            .SetValue(session, new JsonType.BackendUrls { RagFair = "https://eft.test" });
        return session;
    }

    private static void InterceptTransport<T>(Harmony transport)
    {
        var send = AccessTools.Method(typeof(Backend), nameof(Backend.Send), [typeof(BackendRequestParams)], [typeof(T)]);
        var capture = AccessTools.Method(typeof(FilterSettingsTests), nameof(CaptureRequest)).MakeGenericMethod(typeof(T));
        transport.Patch(send, prefix: new HarmonyMethod(capture));
    }

    private static bool CaptureRequest<T>(BackendRequestParams __0, ref Task<T> __result)
    {
        // Stop at the transport boundary. This test only inspects the outgoing request.
        _outgoingRequest = __0;
        __result = new TaskCompletionSource<T>().Task;
        return false;
    }

    [Test]
    public void QueuedEftSearchUsesItsOwnFilterRuleEvenAfterSwitchingViews()
    {
        OpenView(EViewListType.AllOffers);
        Apply(true);
        var flea = OpenView(EViewListType.WishList);
        AccessTools.Field(typeof(RagFair), "_tradingSession").SetValue(flea, BackendSession());
        var cache = AccessTools.Field(typeof(RagFair), "_cachedInfo");
        cache.SetValue(flea, Activator.CreateInstance(cache.FieldType));
        var hooks = new Harmony("net.liquidwarp.hidelockedflea.tests.search");
        try
        {
            InterceptTransport<OffersList>(hooks);
            hooks.Patch(AccessTools.Method(typeof(RagFair), nameof(RagFair.GetOffers)),
                prefix: new HarmonyMethod(typeof(GetOffersPatch), nameof(GetOffersPatch.Prefix)),
                finalizer: new HarmonyMethod(typeof(GetOffersPatch), nameof(GetOffersPatch.Finalizer)));
            hooks.Patch(AccessTools.Method(typeof(LocalizationExtensions), nameof(LocalizationExtensions.Localized), [typeof(string), typeof(string)]),
                prefix: new HarmonyMethod(typeof(FilterSettingsTests), nameof(SkipSceneOperation)));
            var search = flea.GetOffers(false, new FilterRule { ViewListType = EViewListType.AllOffers, NeededSearchId = "test-item" });
            if (search.IsFaulted)
                search.GetAwaiter().GetResult();
            Assert.That(_outgoingRequest?.BackendMethod, Is.EqualTo("/client/ragfair/find?hideLockedFlea=1"));
            Assert.That(GetOffersPatch.HideLocked, Is.False, "EFT has dispatched the request and left the search scope");
        }
        finally
        {
            hooks.UnpatchSelf();
            _outgoingRequest = null;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EachSearchCarriesTheCheckboxThroughAFreshServerAndAnEftRetry(bool hideLocked)
    {
        OpenView(EViewListType.AllOffers);
        Apply(hideLocked);
        var session = new SPTarkov.Server.Core.Models.Common.MongoId();
        var accessor = new HttpContextAccessor();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var request = Search(EViewListType.AllOffers);
            if (attempt > 0)
            {
                request.IncreaseRetryIndex();
                Assert.That(request.CurSelectedURLNamePath, Does.EndWith("?retry=1"));
            }
            var uri = new Uri("https://" + request.CurSelectedURLNamePath);
            var context = new DefaultHttpContext();
            context.Request.Path = uri.AbsolutePath;
            context.Request.QueryString = QueryString.FromUriComponent(uri);
            accessor.HttpContext = context;
            try
            {
                var market = new FleaMarket(session);
                var locked = market.Offer(loyalty: 4);
                var player = market.Offer(OfferCreator.Player);
                Assert.That(market.Search(session, locked, player),
                    Is.EqualTo(hideLocked ? new[] { player } : new[] { locked, player }));
            }
            finally
            {
                accessor.HttpContext = null;
            }
        }
    }

    [Test]
    public void CapturedRequestsKeepTheirViewWhenAnotherSearchStarts()
    {
        OpenView(EViewListType.WishList);
        Apply(true);
        var wishlist = Search(EViewListType.WishList);
        var all = Search(EViewListType.AllOffers);
        Assert.That(all.BackendMethod, Is.EqualTo("/client/ragfair/find?hideLockedFlea=0"));
        Assert.That(wishlist.BackendMethod, Is.EqualTo("/client/ragfair/find?hideLockedFlea=1"));
    }

    [Test]
    public void OtherBackendRequestsKeepTheirOriginalPathAndParameters()
    {
        OpenView(EViewListType.AllOffers);
        Apply(true);
        var body = new object();
        var request = BackendRequestParams.CreateFromLegacyParams(new SendRequest
            { Url = "https://eft.test/client/ragfair/itemMarketPrice", Params = body });
        Assert.That(request.BackendMethod, Is.EqualTo("/client/ragfair/itemMarketPrice"));
        Assert.That(request.Params, Is.SameAs(body));
    }
}
