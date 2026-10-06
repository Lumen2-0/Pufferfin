using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.AutoConfig;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.AutoConfig;

public sealed class AutoConfigRunnerTests
{
    private static AutoConfigRunner CreateRunner(StoreTestContext context, IEnumerable<IAutoConfigStep> steps, TimeSpan? stepTimeout = null)
        => new(steps, context.Start(), TimeProvider.System, context.Logs.CreateLogger<AutoConfigRunner>(), stepTimeout ?? TimeSpan.FromSeconds(30));

    [Fact]
    public async Task TwoConsecutiveRuns_LeaveTheSameState()
    {
        using var context = new StoreTestContext();
        var remote = new FakeRemoteService();
        remote.Items["manual-tweak"] = "set by hand";
        using var runner = CreateRunner(context, [new EnsureItemsStep(remote, "download-client", "category")]);

        var first = await runner.RunAsync(AutoConfigTrigger.Manual, CancellationToken.None);
        var stateAfterFirst = remote.Snapshot();
        var second = await runner.RunAsync(AutoConfigTrigger.ConfigurationSaved, CancellationToken.None);

        Assert.Equal(stateAfterFirst, remote.Snapshot());
        Assert.Equal(AutoConfigStepStatus.Applied, first.Steps.Single().Status);
        Assert.Equal(AutoConfigStepStatus.Unchanged, second.Steps.Single().Status);

        // A setting made by hand on the remote service is never touched.
        Assert.Equal("set by hand", remote.Items["manual-tweak"]);
        Assert.Same(second, runner.LastRun);
    }

    [Fact]
    public async Task FailingStep_DoesNotStopTheOthers_AndItsMessageIsNotReported()
    {
        using var context = new StoreTestContext();
        var remote = new FakeRemoteService();
        using var runner = CreateRunner(context, [new ThrowingStep(), new EnsureItemsStep(remote, "item")]);

        var result = await runner.RunAsync(AutoConfigTrigger.Manual, CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Failed, result.Steps[0].Status);
        Assert.DoesNotContain("secret-in-message", result.Steps[0].Detail!, StringComparison.Ordinal);
        Assert.DoesNotContain(context.Logs.Entries, e => e.Contains("secret-in-message", StringComparison.Ordinal));
        Assert.Equal(AutoConfigStepStatus.Applied, result.Steps[1].Status);
    }

    [Fact]
    public async Task HangingStep_FailsAtItsTimeout()
    {
        using var context = new StoreTestContext();
        using var runner = CreateRunner(context, [new HangingStep()], TimeSpan.FromMilliseconds(200));

        var result = await runner.RunAsync(AutoConfigTrigger.Manual, CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Failed, result.Steps.Single().Status);
    }

    [Fact]
    public async Task ConcurrentRuns_NeverOverlap()
    {
        using var context = new StoreTestContext();
        var step = new OverlapDetectingStep();
        using var runner = CreateRunner(context, [step]);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => runner.RunAsync(AutoConfigTrigger.ConfigurationSaved, CancellationToken.None)));

        Assert.Equal(5, step.Runs);
        Assert.False(step.Overlapped);
    }

    [Fact]
    public async Task Run_ReadsTheLatestSavedSettings()
    {
        using var context = new StoreTestContext();
        var store = context.Start();
        var step = new RecordingStep();
        using var runner = new AutoConfigRunner([step], store, TimeProvider.System, context.Logs.CreateLogger<AutoConfigRunner>());

        store.Update(StoreTestContext.Changes((SettingKeys.MaxConcurrentDownloads, 4)));
        await runner.RunAsync(AutoConfigTrigger.ConfigurationSaved, CancellationToken.None);

        Assert.Equal(4, step.LastSettings!.MaxConcurrentDownloads);
    }

    private sealed class FakeRemoteService
    {
        public ConcurrentDictionary<string, string> Items { get; } = new();

        public string Snapshot() => string.Join(";", Items.OrderBy(i => i.Key, StringComparer.Ordinal).Select(i => i.Key + "=" + i.Value));
    }

    /// <summary>
    /// Follows the step contract: create what is missing, never modify what exists.
    /// </summary>
    private sealed class EnsureItemsStep : IAutoConfigStep
    {
        private readonly FakeRemoteService _remote;
        private readonly string[] _items;

        public EnsureItemsStep(FakeRemoteService remote, params string[] items)
        {
            _remote = remote;
            _items = items;
        }

        public string Name => "Ensure items";

        public Task<AutoConfigStepOutcome> ApplyAsync(PufferfinSettings settings, CancellationToken cancellationToken)
        {
            var created = _items.Count(item => _remote.Items.TryAdd(item, "pufferfin"));
            return Task.FromResult(new AutoConfigStepOutcome(created > 0 ? AutoConfigStepStatus.Applied : AutoConfigStepStatus.Unchanged));
        }
    }

    private sealed class ThrowingStep : IAutoConfigStep
    {
        public string Name => "Throwing";

        public Task<AutoConfigStepOutcome> ApplyAsync(PufferfinSettings settings, CancellationToken cancellationToken)
            => throw new InvalidOperationException("http://radarr/?apikey=secret-in-message");
    }

    private sealed class HangingStep : IAutoConfigStep
    {
        public string Name => "Hanging";

        public async Task<AutoConfigStepOutcome> ApplyAsync(PufferfinSettings settings, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new AutoConfigStepOutcome(AutoConfigStepStatus.Applied);
        }
    }

    private sealed class OverlapDetectingStep : IAutoConfigStep
    {
        private int _active;
        private int _runs;

        public string Name => "Overlap";

        public bool Overlapped { get; private set; }

        public int Runs => _runs;

        public async Task<AutoConfigStepOutcome> ApplyAsync(PufferfinSettings settings, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _active) > 1)
            {
                Overlapped = true;
            }

            await Task.Delay(20, cancellationToken);
            Interlocked.Decrement(ref _active);
            Interlocked.Increment(ref _runs);
            return new AutoConfigStepOutcome(AutoConfigStepStatus.Unchanged);
        }
    }

    private sealed class RecordingStep : IAutoConfigStep
    {
        public string Name => "Recording";

        public PufferfinSettings? LastSettings { get; private set; }

        public Task<AutoConfigStepOutcome> ApplyAsync(PufferfinSettings settings, CancellationToken cancellationToken)
        {
            LastSettings = settings;
            return Task.FromResult(new AutoConfigStepOutcome(AutoConfigStepStatus.Unchanged));
        }
    }
}
