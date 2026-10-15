using BepInEx;
using BepInEx.Logging;

namespace Liquidwarp.HideLockedFlea.Client;

[BepInPlugin(MyModInfo.MOD_GUID, MyModInfo.MOD_NAME, MyModInfo.MOD_VERSION)]
[BepInDependency("com.SPT.core", MyModInfo.CLIENT_SPT_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public static ManualLogSource LogSource;

    private void Awake()
    {
        LogSource = Logger;

        new GetOffersPatch().Enable();
        new SaveRuleAsDefaultPatch().Enable();
        new ClearSavedFiltersPatch().Enable();
        new FilterWindowAwakePatch().Enable();
        new FilterWindowShowPatch().Enable();
    }
}
