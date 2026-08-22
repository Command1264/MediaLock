using MediaLock.Core.Lifecycle;
using MediaLock.Windows.Lifecycle;
using Xunit;

namespace MediaLock.Windows.Tests;

public sealed class NamedPipeInstanceCoordinatorTests
{
    [Fact]
    public async Task SecondInstanceActivatesTheExistingPrimaryInstance()
    {
        var instanceName = $"MediaLock.Tests.{Guid.NewGuid():N}";
        await using var primary = new NamedPipeInstanceCoordinator(instanceName);
        await using var secondary = new NamedPipeInstanceCoordinator(instanceName);
        var activated = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        primary.ActivationRequested += (_, _) => activated.TrySetResult();

        Assert.Equal(
            InstanceStartResult.Primary,
            await primary.StartAsync(CancellationToken.None));
        Assert.Equal(
            InstanceStartResult.ActivatedExisting,
            await secondary.StartAsync(CancellationToken.None));

        await activated.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
