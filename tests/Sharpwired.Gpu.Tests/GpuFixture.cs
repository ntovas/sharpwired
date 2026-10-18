// No Handwired source. GpuFixture opens each WebGPU adapter one time for the tests of GpuCollection.
// GpuPolicy holds the rule for a machine with no adapter (decision 1 of the phase 5 plan).

using Xunit.Abstractions;

namespace Sharpwired.Gpu.Tests;

/// <summary>What a GPU test does on this machine.</summary>
internal enum GpuPolicyAction
{
    // Run the test on each adapter.
    Run,

    // No adapter, and SHARPWIRED_NO_GPU holds a reason: write the reason and pass.
    PassWithReason,

    // No adapter, and no reason: fail.
    Fail,
}

internal sealed record GpuPolicyResult(GpuPolicyAction Action, string Message);

/// <summary>The zero-adapter rule. A machine with an adapter always runs the GPU tests.</summary>
internal static class GpuPolicy
{
    public const string Variable = "SHARPWIRED_NO_GPU";

    public const string NoAdapterMessage =
        "no WebGPU adapter was found; set SHARPWIRED_NO_GPU to a reason to accept a machine with no GPU";

    // listError is the message of a ListAdapters call that threw, or null.
    public static GpuPolicyResult Decide(IReadOnlyList<GpuAdapterInfo> adapters, string? reason, string? listError = null)
    {
        if (adapters.Count > 0) return new GpuPolicyResult(GpuPolicyAction.Run, "");
        if (!string.IsNullOrWhiteSpace(reason)) return new GpuPolicyResult(GpuPolicyAction.PassWithReason, $"[gpu] no adapter: {reason.Trim()}");
        var message = listError is null ? NoAdapterMessage : $"{NoAdapterMessage} (ListAdapters threw: {listError})";
        return new GpuPolicyResult(GpuPolicyAction.Fail, message);
    }
}

/// <summary>One open device for each adapter. The tests of the collection use these devices one at a time.</summary>
public sealed class GpuFixture : IDisposable
{
    private readonly List<GpuDevice> devices = [];
    private readonly List<string> openErrors = [];
    private readonly string? listError;

    public GpuFixture()
    {
        try
        {
            Adapters = GpuDevice.ListAdapters();
        }
        catch (Exception exception)
        {
            Adapters = [];
            listError = $"{exception.GetType().Name}: {exception.Message}";
        }

        foreach (var adapter in Adapters)
        {
            try
            {
                devices.Add(GpuDevice.Open(adapter));
            }
            catch (Exception exception)
            {
                openErrors.Add($"{adapter.Name} ({adapter.Backend}): {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    public IReadOnlyList<GpuAdapterInfo> Adapters { get; }

    public IReadOnlyList<GpuDevice> Devices => devices;

    /// <summary>
    /// Apply the zero-adapter rule. Return true when the test must run on the adapters. Return false
    /// after the reason was written to the output. Fail when no adapter was found and no reason is set,
    /// and when an adapter did not open.
    /// </summary>
    public bool RequireAdapters(ITestOutputHelper output)
    {
        var policy = GpuPolicy.Decide(Adapters, Environment.GetEnvironmentVariable(GpuPolicy.Variable), listError);
        switch (policy.Action)
        {
            case GpuPolicyAction.PassWithReason:
                output.WriteLine(policy.Message);
                return false;
            case GpuPolicyAction.Fail:
                Assert.Fail(policy.Message);
                return false;
        }

        Assert.True(openErrors.Count == 0, "an adapter did not open: " + string.Join("; ", openErrors));
        return true;
    }

    /// <summary>
    /// Run the test on the device of each adapter. Write the adapter to the output. Assert that the test
    /// released each native object that it created.
    /// </summary>
    public void ForEachDevice(ITestOutputHelper output, Action<GpuDevice> test)
    {
        if (!RequireAdapters(output)) return;
        foreach (var device in devices)
        {
            output.WriteLine($"[gpu] adapter {device.Adapter.Index}: {device.Adapter.Name}, {device.Adapter.Backend}, {device.Adapter.Type}, FlushesSubnormals {device.FlushesSubnormals}");
            var before = GpuDevice.LiveObjects;
            test(device);
            Assert.True(before == GpuDevice.LiveObjects, $"the test left {GpuDevice.LiveObjects - before} native objects alive on {device.Adapter.Name}");
        }
    }

    public void Dispose()
    {
        foreach (var device in devices) device.Dispose();
    }
}

/// <summary>The GPU tests. They share one GpuFixture, and they do not run in parallel.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GpuCollection : ICollectionFixture<GpuFixture>
{
    public const string Name = "Gpu";
}
