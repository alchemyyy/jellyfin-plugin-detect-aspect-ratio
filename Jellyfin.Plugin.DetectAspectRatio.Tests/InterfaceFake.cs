using System;
using System.Collections.Generic;
using System.Reflection;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

/// <summary>
/// Implements an interface from handlers keyed by member name, so fakes of Jellyfin's large interfaces stay small.
/// Any member without a handler throws, which shows what the code under test calls.
/// </summary>
public class InterfaceFake : DispatchProxy
{
    private Dictionary<string, Func<MethodInfo, object?[], object?>> handlers = new Dictionary<string, Func<MethodInfo, object?[], object?>>(StringComparer.Ordinal);

    public static T Create<T>(Dictionary<string, Func<MethodInfo, object?[], object?>> handlers)
        where T : class
    {
        T fake = DispatchProxy.Create<T, InterfaceFake>();
        ((InterfaceFake)(object)fake).handlers = handlers;
        return fake;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (handlers.TryGetValue(targetMethod.Name, out Func<MethodInfo, object?[], object?>? handler))
        {
            return handler(targetMethod, args ?? []);
        }

        throw new NotSupportedException(targetMethod.DeclaringType?.Name + "." + targetMethod.Name + " has no handler in this test");
    }
}
