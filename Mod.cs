using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;

namespace Liquidwarp.HideLockedFlea;

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public class Mod(IEnumerable<IRuntimePatch> patches) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken ct = default)
    {
        foreach (var patch in patches)
            patch.Enable();

        return Task.CompletedTask;
    }
}
