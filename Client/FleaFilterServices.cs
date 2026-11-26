using EFT.UI;
using SPT.Common.Http;
using UnityEngine;

namespace Liquidwarp.HideLockedFlea.Client;

internal class FleaFilterServices
{
    internal static FleaFilterServices Current { get; set; } = new();

    public virtual int ReadPreference(string key, int fallback) => PlayerPrefs.GetInt(key, fallback);

    public virtual void SavePreference(string key, int value)
    {
        PlayerPrefs.SetInt(key, value);
        PlayerPrefs.Save();
    }

    public virtual void PostJson(string url, string json) => RequestHandler.PostJson(url, json);

    public virtual void LogWarning(string message) => Plugin.LogSource.LogWarning(message);

    public virtual UpdatableToggle DuplicateToggle(UpdatableToggle original, string name, string label)
    {
        var copy = Object.Instantiate(original, original.transform.parent, false);
        copy.name = name;
        copy.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);

        copy.GetComponentInChildren<LocalizedText>(true)?.LocalizationKey = label;
        return copy;
    }
}
