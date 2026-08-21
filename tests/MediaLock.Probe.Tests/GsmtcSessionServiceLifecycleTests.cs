using MediaLock.Probe;
using Windows.Media.Control;

namespace MediaLock.Probe.Tests;

public sealed class GsmtcSessionServiceLifecycleTests
{
    [Fact]
    public async Task ResumeReacquiresManagerAndLeavesOnlyNewSubscriptionActive()
    {
        await using var queue = new SerializedIntentQueue();
        var lifecycle = new FakeSystemLifecycle();
        var first = new FakeManager();
        var second = new FakeManager();
        var factory = new FakeManagerFactory(first, second);
        using var service = new GsmtcSessionService(queue, factory, lifecycle, TimeProvider.System);

        await queue.InvokeAsync(service.InitializeAsync);

        lifecycle.RaiseSuspending();
        lifecycle.RaiseResumed();
        await queue.InvokeAsync(() => ValueTask.CompletedTask);

        Assert.Equal(2, factory.AcquisitionCount);
        Assert.True(first.IsDisposed);
        Assert.Equal(0, first.SubscriptionCount);
        Assert.Equal(1, second.SubscriptionCount);

        first.RaiseSessionsChanged();
        second.RaiseSessionsChanged();
        await queue.InvokeAsync(() => ValueTask.CompletedTask);

        Assert.Equal(1, first.GetSessionsCount);
        Assert.Equal(2, second.GetSessionsCount);
    }

    [Fact]
    public async Task FailedResumeAcquisitionKeepsQueueAliveAndReportsUnavailable()
    {
        await using var queue = new SerializedIntentQueue();
        var lifecycle = new FakeSystemLifecycle();
        var first = new FakeManager();
        var factory = new FailingResumeManagerFactory(first);
        using var service = new GsmtcSessionService(queue, factory, lifecycle, TimeProvider.System);
        var messages = new List<string>();
        service.StateChanged += messages.Add;

        await queue.InvokeAsync(service.InitializeAsync);

        lifecycle.RaiseSuspending();
        lifecycle.RaiseResumed();
        await queue.InvokeAsync(() => ValueTask.CompletedTask);

        var queueContinued = false;
        await queue.InvokeAsync(() =>
        {
            queueContinued = true;
            return ValueTask.CompletedTask;
        });

        Assert.True(first.IsDisposed);
        Assert.True(queueContinued);
        Assert.Contains(messages, message =>
            message.StartsWith("GSMTC manager remains unavailable after resume:", StringComparison.Ordinal));
    }

    private sealed class FakeManagerFactory(params FakeManager[] managers) : IGsmtcSessionManagerFactory
    {
        private readonly Queue<FakeManager> managers = new(managers);

        public int AcquisitionCount { get; private set; }

        public Task<IGsmtcSessionManager> CreateAsync()
        {
            AcquisitionCount++;
            return Task.FromResult<IGsmtcSessionManager>(managers.Dequeue());
        }
    }

    private sealed class FailingResumeManagerFactory(FakeManager initialManager) : IGsmtcSessionManagerFactory
    {
        private int acquisitionCount;

        public Task<IGsmtcSessionManager> CreateAsync()
        {
            acquisitionCount++;
            return acquisitionCount == 1
                ? Task.FromResult<IGsmtcSessionManager>(initialManager)
                : Task.FromException<IGsmtcSessionManager>(new InvalidOperationException("manager unavailable"));
        }
    }

    private sealed class FakeManager : IGsmtcSessionManager
    {
        private Action? sessionsChanged;

        public int GetSessionsCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public int SubscriptionCount { get; private set; }

        public event Action SessionsChanged
        {
            add
            {
                sessionsChanged += value;
                SubscriptionCount++;
            }
            remove
            {
                sessionsChanged -= value;
                SubscriptionCount--;
            }
        }

        public IReadOnlyList<GlobalSystemMediaTransportControlsSession> GetSessions()
        {
            GetSessionsCount++;
            return [];
        }

        public void RaiseSessionsChanged() => sessionsChanged?.Invoke();

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class FakeSystemLifecycle : ISystemLifecycle
    {
        public event Action? Suspending;

        public event Action? Resumed;

        public void RaiseSuspending() => Suspending?.Invoke();

        public void RaiseResumed() => Resumed?.Invoke();
    }
}
