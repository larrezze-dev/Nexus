using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Confluent.SchemaRegistry;

namespace Nexus.Projections.Tests.Runtime.Infrastructure;

/// <summary>
/// An <see cref="ISchemaRegistryClient"/> that forwards to a real client, except while
/// <see cref="FailWith"/> is set: then every asynchronous call fails with a
/// <see cref="SchemaRegistryException"/> of that HTTP status (e.g. 403: the registry refusing our
/// credentials). Built with <see cref="DispatchProxy"/>, so the whole interface is covered.
/// </summary>
public class SwitchableSchemaRegistry : DispatchProxy
{
    private ISchemaRegistryClient _inner = null!;

    /// <summary>Status every async call fails with; null = pass-through.</summary>
    public HttpStatusCode? FailWith { get; set; }

    /// <summary>Async calls answered with the failure so far.</summary>
    public int Failures { get; private set; }

    public static (ISchemaRegistryClient Client, SwitchableSchemaRegistry Switch) Create(ISchemaRegistryClient inner)
    {
        var client = Create<ISchemaRegistryClient, SwitchableSchemaRegistry>();
        var control = (SwitchableSchemaRegistry)(object)client;
        control._inner = inner;
        return (client, control);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (FailWith is { } status && targetMethod.ReturnType.IsGenericType
            && targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            Failures++;
            var resultType = targetMethod.ReturnType.GetGenericArguments()[0];
            var fromException = typeof(Task).GetMethod(nameof(Task.FromException), 1, [typeof(Exception)])!.MakeGenericMethod(resultType);
            return fromException.Invoke(null, [new SchemaRegistryException($"Simulated {(int)status}", status, (int)status * 100 + 1)]);
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(ex.InnerException);
            throw;
        }
    }
}
