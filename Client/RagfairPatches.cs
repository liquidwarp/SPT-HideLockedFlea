using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.UI;
using EFT.UI.Ragfair;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Reflection.Patching;
using UnityEngine;

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

    public static void SaveAsDefault(EViewListType view)
    {
        PlayerPrefs.SetInt(Key(view), Get(view) ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void ClearCurrent() => CurrentValues.Clear();

    private static bool GetDefault(EViewListType view) => PlayerPrefs.GetInt(Key(view), DefaultValue ? 1 : 0) == 1;

    private static string Key(EViewListType view) => PlayerPrefsKeyPrefix + view;
}

public class GetOffersPatch : ModulePatch
{
    private static bool? _sentHideLocked;

    public static void ResendNextSearch() => _sentHideLocked = null;

    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(RagFair), nameof(RagFair.GetOffers), [typeof(bool), typeof(FilterRule)]);

    [PatchPrefix]
    public static void Prefix(FilterRule filterRule)
    {
        var hideLocked = HideLockedSetting.Get(filterRule.ViewListType);
        if (_sentHideLocked == hideLocked)
            return;

        try
        {
            RequestHandler.PostJson("/hidelockedflea/settings", JsonConvert.SerializeObject(new { hideLocked }));
            _sentHideLocked = hideLocked;
        }
        catch (Exception ex)
        {
            // Not remembered as sent, so the next search tries again.
            Plugin.LogSource.LogWarning($"Failed to send the setting to the server: {ex.Message}");
        }
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
    public static void Postfix()
    {
        HideLockedSetting.ClearCurrent();
        GetOffersPatch.ResendNextSearch();
    }
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
        HideLockedToggle = DuplicateToggle(____onlyFunctional, "HideLocked", Label);

        ____applyButton.OnClick.AddListener(() => SetHideLocked(RagfairRef(__instance), HideLockedToggle.isOn));
        ____resetButton.OnClick.AddListener(() =>
        {
            var ragfair = RagfairRef(__instance);
            if (ragfair.FilterRule.ViewListType is EViewListType.AllOffers or EViewListType.WishList)
                SetHideLocked(ragfair, HideLockedSetting.DefaultValue);
        });
    }

    private static UpdatableToggle DuplicateToggle(UpdatableToggle original, string name, string label)
    {
        var copy = UnityEngine.Object.Instantiate(original, original.transform.parent, false);
        copy.name = name;
        copy.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);

        copy.GetComponentInChildren<LocalizedText>(true)?.LocalizationKey = label;

        return copy;
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
