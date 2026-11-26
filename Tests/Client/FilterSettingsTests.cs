using System.Runtime.CompilerServices;
using System.Text.Json;
using EFT.UI;
using EFT.UI.Ragfair;
using HarmonyLib;
using Liquidwarp.HideLockedFlea.Server.Tests;
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
        public readonly List<(string Url, string Json)> Requests = [];
        public readonly List<string> Warnings = [];
        public bool RejectNextPost;
        public UpdatableToggle? Template;
        public string? Label;

        public override int ReadPreference(string key, int fallback) => Preferences.GetValueOrDefault(key, fallback);
        public override void SavePreference(string key, int value) => Preferences[key] = value;
        public override void LogWarning(string message) => Warnings.Add(message);

        public override UpdatableToggle DuplicateToggle(UpdatableToggle original, string name, string label)
        {
            Template = original;
            Label = label;
            return ManagedControl<UpdatableToggle>();
        }

        public override void PostJson(string url, string json)
        {
            Requests.Add((url, json));
            if (RejectNextPost)
            {
                RejectNextPost = false;
                throw new IOException("Test connection failed");
            }
        }
    }

    private readonly TestServices _services = new();
    private readonly RagfairFilterWindow _window = ManagedControl<RagfairFilterWindow>();
    private readonly UpdatableToggle _template = ManagedControl<UpdatableToggle>();
    private readonly DefaultUIButton _apply = Button();
    private readonly DefaultUIButton _reset = Button();
    private FleaFilterServices _original = null!;

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

    private static void Search(EViewListType view) => GetOffersPatch.Prefix(new FilterRule { ViewListType = view });

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
    [TestCase(EViewListType.Handbook, false)]
    [TestCase(EViewListType.WeaponBuild, false)]
    [TestCase(EViewListType.RequirementsWindow, false)]
    public void ResetUsesTheGameViewsDefaultWithoutOverwritingRememberedChoices(EViewListType view, bool supportsReset)
    {
        var flea = OpenView(view);
        Apply(true);
        SaveRuleAsDefaultPatch.Postfix(flea);
        flea.ForceNextUpdate = false;

        _reset.OnClick.Invoke();

        Assert.That(HideLockedSetting.Get(view), Is.EqualTo(!supportsReset));
        Assert.That(flea.ForceNextUpdate, Is.EqualTo(supportsReset));
        ClearSavedFiltersPatch.Postfix();
        Assert.That(HideLockedSetting.Get(view), Is.True);
    }

    [Test]
    public void SearchesPostTheFirstValueThenOnlyChangesAcrossViews()
    {
        Search(EViewListType.AllOffers);
        Search(EViewListType.WishList);
        OpenView(EViewListType.WishList);
        Apply(true);
        Search(EViewListType.WishList);
        Search(EViewListType.WishList);
        Search(EViewListType.AllOffers);

        AssertPosts(false, true, false);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CheckboxRequestControlsTheServerSearch(bool hideLocked)
    {
        var market = new FleaMarket();
        var locked = market.Offer(loyalty: 4);
        var player = market.Offer(OfferCreator.Player);
        OpenView(EViewListType.AllOffers);
        Apply(hideLocked);
        Search(EViewListType.AllOffers);

        var (url, json) = _services.Requests.Single();
        await market.Router.HandleStaticAsync(url, json, market.Session, "");
        var visible = market.Search(market.Session, locked, player);

        Assert.That(visible, Is.EqualTo(hideLocked ? new[] { player } : new[] { locked, player }));
    }

    [Test]
    public void ClearingRestoresSavedDefaultsAndResendsEvenWhenTheValueMatches()
    {
        var flea = OpenView(EViewListType.AllOffers);
        Apply(true);
        SaveRuleAsDefaultPatch.Postfix(flea);
        Search(EViewListType.AllOffers);
        Apply(false);

        ClearSavedFiltersPatch.Postfix();
        Search(EViewListType.AllOffers);
        Search(EViewListType.AllOffers);

        AssertPosts(true, true);
    }

    [Test]
    public void FailedPostsWarnAndRetryOnTheNextSearch()
    {
        Search(EViewListType.AllOffers);
        OpenView(EViewListType.AllOffers);
        Apply(true);
        _services.RejectNextPost = true;
        Search(EViewListType.AllOffers);
        Assert.That(_services.Warnings, Has.One.Contains("Test connection failed"));

        Search(EViewListType.AllOffers);
        Search(EViewListType.AllOffers);
        AssertPosts(false, true, true);
    }

    private void AssertPosts(params bool[] expected)
    {
        Assert.That(_services.Requests, Has.Count.EqualTo(expected.Length));
        for (var index = 0; index < expected.Length; index++)
        {
            var (url, json) = _services.Requests[index];
            Assert.That(url, Is.EqualTo("/hidelockedflea/settings"));
            using var body = JsonDocument.Parse(json);
            Assert.That(body.RootElement.GetProperty("hideLocked").GetBoolean(), Is.EqualTo(expected[index]));
        }
    }
}
