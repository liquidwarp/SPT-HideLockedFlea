using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.UI;
using EFT.UI.Ragfair;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Liquidwarp.HideLockedFlea.Client;

internal static class HideLockedSetting
{
    private const string PlayerPrefsKeyPrefix = "Liquidwarp.HideLockedFlea.DefaultFilterRule_";

    public const bool DefaultValue = false;

    private static readonly Dictionary<EViewListType, bool> CurrentValues = [];

    public static bool Get(EViewListType view) => CurrentValues.TryGetValue(view, out var value) ? value : GetDefault(view);

    public static bool Set(EViewListType view, bool value)
    {
        var changed = Get(view) != value;
        CurrentValues[view] = value;
        return changed;
    }

    public static void SaveAsDefault(EViewListType view) => FleaFilterServices.Current.SavePreference(Key(view), Get(view) ? 1 : 0);

    public static void ClearCurrent() => CurrentValues.Clear();

    private static bool GetDefault(EViewListType view) => FleaFilterServices.Current.ReadPreference(Key(view), DefaultValue ? 1 : 0) == 1;

    private static string Key(EViewListType view) => PlayerPrefsKeyPrefix + view;
}

public class GetOffersPatch : ModulePatch
{
    // EFT constructs the backend request before the first await in RagFair.GetOffers.
    [ThreadStatic] internal static bool HideLocked;

    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(RagFair), nameof(RagFair.GetOffers), [typeof(bool), typeof(FilterRule)]);

    [PatchPrefix]
    public static void Prefix(FilterRule filterRule, out bool __state)
    {
        __state = HideLocked;
        HideLocked = HideLockedSetting.Get(filterRule.ViewListType);
    }

    [PatchFinalizer]
    public static void Finalizer(bool __state) => HideLocked = __state;
}

public class FleaRequestPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(BackendRequestParams), nameof(BackendRequestParams.CreateFromLegacyParams));

    [PatchPostfix]
    public static void Postfix(BackendRequestParams __result)
    {
        bool hideLocked;
        switch (__result.BackendMethod)
        {
            case "/client/ragfair/find":
                hideLocked = GetOffersPatch.HideLocked;
                break;
            case "/client/ragfair/offer/findbyid":
                // EFT switches to All Offers before looking up a numeric ID.
                hideLocked = HideLockedSetting.Get(EViewListType.AllOffers);
                break;
            default:
                return;
        }

        __result.BackendMethod += hideLocked ? "?hideLockedFlea=1" : "?hideLockedFlea=0";
    }
}

public class SaveRuleAsDefaultPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(RagFair), nameof(RagFair.SaveRuleAsDefault));

    [PatchPostfix]
    public static void Postfix(RagFair __instance) => HideLockedSetting.SaveAsDefault(__instance.FilterRule.ViewListType);
}

public class ClearSavedFiltersPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(RagFair), nameof(RagFair.ClearSavedFilters));

    [PatchPostfix]
    public static void Postfix() => HideLockedSetting.ClearCurrent();
}

public class FilterWindowAwakePatch : ModulePatch
{
    private const string Label = "Hide locked offers";

    private static readonly AccessTools.FieldRef<RagfairFilterWindow, RagFair> RagfairRef =
        AccessTools.FieldRefAccess<RagfairFilterWindow, RagFair>("_ragfair");

    internal static UpdatableToggle HideLockedToggle;

    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(RagfairFilterWindow), nameof(RagfairFilterWindow.Awake));

    [PatchPrefix]
    public static void Prefix(RagfairFilterWindow __instance, UpdatableToggle ____onlyFunctional, DefaultUIButton ____applyButton, DefaultUIButton ____resetButton)
    {
        HideLockedToggle = FleaFilterServices.Current.DuplicateToggle(____onlyFunctional, "HideLocked", Label);

        ____applyButton.OnClick.AddListener(() => SetHideLocked(RagfairRef(__instance), HideLockedToggle.isOn));
        ____resetButton.OnClick.AddListener(() =>
        {
            var ragfair = RagfairRef(__instance);
            if (ragfair.FilterRule.ViewListType is EViewListType.AllOffers or EViewListType.WishList)
                SetHideLocked(ragfair, HideLockedSetting.DefaultValue);
        });
    }

    private static void SetHideLocked(RagFair ragfair, bool hideLocked)
    {
        if (HideLockedSetting.Set(ragfair.FilterRule.ViewListType, hideLocked))
            ragfair.ForceNextUpdate = true;
    }
}

public class FilterWindowShowPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(RagfairFilterWindow), nameof(RagfairFilterWindow.Show));

    [PatchPostfix]
    public static void Postfix(RagFair ragfair) =>
        FilterWindowAwakePatch.HideLockedToggle.UpdateValue(HideLockedSetting.Get(ragfair.FilterRule.ViewListType), sendCallback: false);
}
