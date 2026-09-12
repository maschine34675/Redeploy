using Redeploy.Core.Engine;
using Xunit;

namespace Redeploy.Tests;

public class SignalDispatcherTests
{
    private sealed class Subscriber
    {
        public readonly List<string> Calls = new();
        public bool Dead;
        public void Handle(int value) => Calls.Add($"instance:{value}");
    }

    private static readonly List<string> StaticCalls = new();
    private static void StaticHandle(int value) => StaticCalls.Add($"static:{value}");

    [Fact]
    public void ThrowingSubscriberDoesNotStopTheOthersAndIsNamed()
    {
        var log = new List<string>();
        var calls = new List<string>();
        Action<int> signal = null;
        signal += _ => calls.Add("first");
        signal += _ => throw new InvalidOperationException("boom");
        signal += _ => calls.Add("third");

        new SignalDispatcher(log.Add).Raise(signal, h => h(1), "Replaced");

        Assert.Equal(new[] { "first", "third" }, calls);
        var line = Assert.Single(log);
        Assert.Contains("Replaced subscriber", line);
        Assert.Contains("boom", line);
        Assert.Contains(typeof(SignalDispatcherTests).Assembly.GetName().Name, line);
    }

    [Fact]
    public void DepthCountsNestedRaisesAndRecoversAfterAThrow()
    {
        var dispatcher = new SignalDispatcher(_ => { });
        var depthInner = -1;
        var depthOuter = -1;
        Action<int> inner = _ => depthInner = dispatcher.Depth;
        Action<int> outer = _ =>
        {
            depthOuter = dispatcher.Depth;
            dispatcher.Raise(inner, h => h(0), "Inner");
        };

        dispatcher.Raise(outer, h => h(0), "Outer");

        Assert.Equal(1, depthOuter);
        Assert.Equal(2, depthInner);
        Assert.Equal(0, dispatcher.Depth);

        Action<int> thrower = _ => throw new Exception("boom");
        dispatcher.Raise(thrower, h => h(0), "Thrower");
        Assert.Equal(0, dispatcher.Depth);
    }

    [Fact]
    public void DeadTargetsAreSkippedStaticHandlersAreNot()
    {
        var alive = new Subscriber();
        var dead = new Subscriber { Dead = true };
        StaticCalls.Clear();
        Action<int> signal = null;
        signal += dead.Handle;
        signal += alive.Handle;
        signal += StaticHandle;

        new SignalDispatcher(_ => { }, target => target is Subscriber { Dead: true }).Raise(signal, h => h(7), "Signal");

        Assert.Empty(dead.Calls);
        Assert.Equal(new[] { "instance:7" }, alive.Calls);
        Assert.Equal(new[] { "static:7" }, StaticCalls);
    }

    [Fact]
    public void UnsubscribingInsideAHandlerTakesEffectFromTheNextRaise()
    {
        var calls = new List<string>();
        var dispatcher = new SignalDispatcher(_ => { });
        Action<int> signal = null;
        Action<int> second = _ => calls.Add("second");
        signal += _ => { calls.Add("first"); signal -= second; };
        signal += second;

        dispatcher.Raise(signal, h => h(0), "Signal");
        Assert.Equal(new[] { "first", "second" }, calls);

        calls.Clear();
        dispatcher.Raise(signal, h => h(0), "Signal");
        Assert.Equal(new[] { "first" }, calls);
    }

    [Fact]
    public void NullMulticastIsANoOp()
    {
        var dispatcher = new SignalDispatcher(_ => throw new Exception("must not log"));
        dispatcher.Raise<Action<int>>(null, h => h(0), "Signal");
        Assert.Equal(0, dispatcher.Depth);
    }

    [Fact]
    public void DescribeNamesTypeMethodAndAssembly()
    {
        Action<int> handler = StaticHandle;
        var description = SignalDispatcher.Describe(handler);
        Assert.Contains(nameof(SignalDispatcherTests), description);
        Assert.Contains(nameof(StaticHandle), description);
        Assert.Contains(typeof(SignalDispatcherTests).Assembly.GetName().Name, description);
        Assert.Equal("<unknown>", SignalDispatcher.Describe(null));
    }
}
