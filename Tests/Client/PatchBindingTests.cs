using System.Reflection;
using System.Runtime.CompilerServices;
using EFT.UI.Ragfair;
using NUnit.Framework;

namespace Liquidwarp.HideLockedFlea.Client.Tests;

public class PatchBindingTests
{
    [TestCase(typeof(GetOffersPatch), typeof(RagFair), nameof(RagFair.GetOffers), "Prefix", new[] { typeof(bool), typeof(FilterRule) })]
    [TestCase(typeof(SaveRuleAsDefaultPatch), typeof(RagFair), nameof(RagFair.SaveRuleAsDefault), "Postfix", new Type[0])]
    [TestCase(typeof(ClearSavedFiltersPatch), typeof(RagFair), nameof(RagFair.ClearSavedFilters), "Postfix", new Type[0])]
    [TestCase(typeof(FilterWindowAwakePatch), typeof(RagfairFilterWindow), nameof(RagfairFilterWindow.Awake), "Prefix", new Type[0])]
    [TestCase(typeof(FilterWindowShowPatch), typeof(RagfairFilterWindow), nameof(RagfairFilterWindow.Show), "Postfix", new[] { typeof(RagFair) })]
    public void ResolvesTheEftMethodAndItsInjectedArguments(Type patchType, Type owner, string method, string hook, Type[] arguments)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var patch = RuntimeHelpers.GetUninitializedObject(patchType);
        var target = (MethodInfo?)patchType.GetMethod("GetTargetMethod", flags)!.Invoke(patch, null);
        Assert.That(target, Is.Not.Null);
        Assert.That(target, Is.EqualTo(owner.GetMethod(method, flags, arguments)));

        var hookMethod = patchType.GetMethod(hook)!;
        Assert.That(hookMethod.CustomAttributes.Select(attribute => attribute.AttributeType.Name), Does.Contain($"Patch{hook}Attribute"));
        foreach (var parameter in hookMethod.GetParameters())
        {
            var name = parameter.Name!;
            var injectedType = name switch
            {
                "__instance" => owner,
                _ when name.StartsWith("___") => owner.GetField(name[3..], flags)?.FieldType,
                _ => target!.GetParameters().SingleOrDefault(arg => arg.Name == name)?.ParameterType,
            };
            Assert.That(injectedType, Is.EqualTo(parameter.ParameterType), $"{patchType.Name}.{hook}({name})");
        }
    }

    [Test]
    public void FilterWindowExposesTheRagfairInstanceUsedByTheButtonCallbacks()
    {
        var field = typeof(RagfairFilterWindow).GetField("_ragfair", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        Assert.That(field?.FieldType, Is.EqualTo(typeof(RagFair)));
    }
}
