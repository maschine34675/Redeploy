using System;

namespace Redeploy.Core.Engine;

/// <summary>
/// Raises a multicast delegate one subscriber at a time, so a throwing handler cannot
/// silence the handlers after it, and names the handler that failed. Pure (no Unity or
/// EFT types) so Redeploy.Tests covers it; <see cref="CoreSignals"/> hosts the two
/// instances the engine uses.
/// </summary>
public sealed class SignalDispatcher
{
    private readonly Action<string> _logWarning;
    private readonly Func<object, bool> _isDeadTarget;

    /// <param name="logWarning">Receives one line per failed handler.</param>
    /// <param name="isDeadTarget">
    /// Reports a handler target the host considers gone (a destroyed UnityEngine.Object);
    /// such handlers are skipped instead of being invoked on a dead object. Null = never.
    /// </param>
    public SignalDispatcher(Action<string> logWarning, Func<object, bool> isDeadTarget = null)
    {
        _logWarning = logWarning;
        _isDeadTarget = isDeadTarget;
    }

    /// <summary>Raises in progress on this dispatcher, nested ones included; 0 = idle.</summary>
    public int Depth { get; private set; }

    /// <summary>
    /// Invokes every subscriber of <paramref name="multicast"/> through <paramref name="invoke"/>.
    /// The invocation list is snapshotted first: subscribing or unsubscribing from inside a
    /// handler takes effect from the next raise. Never throws.
    /// </summary>
    public void Raise<TDelegate>(TDelegate multicast, Action<TDelegate> invoke, string signalName)
        where TDelegate : Delegate
    {
        if (multicast == null)
            return;

        var handlers = multicast.GetInvocationList();

        Depth++;
        try
        {
            foreach (var handler in handlers)
            {
                if (_isDeadTarget != null && handler.Target != null && _isDeadTarget(handler.Target))
                    continue;

                try
                {
                    invoke((TDelegate)handler);
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke($"{signalName} subscriber {Describe(handler)} failed: {ex}");
                }
            }
        }
        finally
        {
            Depth--;
        }
    }

    /// <summary>"Namespace.Type.Method (assembly)" — enough to tell whose handler failed.</summary>
    public static string Describe(Delegate handler)
    {
        var method = handler?.Method;
        if (method == null)
            return "<unknown>";

        var type = method.DeclaringType;
        return type == null
            ? method.Name
            : $"{type.FullName}.{method.Name} ({type.Assembly.GetName().Name})";
    }
}
