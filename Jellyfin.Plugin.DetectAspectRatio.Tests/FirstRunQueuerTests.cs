using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Jellyfin.Plugin.DetectAspectRatio.Analysis;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

public sealed class FirstRunQueuerTests
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(10);

    private readonly List<Type> queuedTasks = new List<Type>();
    private readonly TaskCompletionSource firstRegistrationCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private int registrationChecks;

    [Fact]
    public async Task NeverRun_QueuesTheTaskOnceTasksAreRegistered()
    {
        // Jellyfin starts hosted services before it registers scheduled tasks, so the first check finds none
        using FirstRunQueuer queuer = CreateQueuer(2, CreateWorker(BlackBarAnalysisTask.TaskKey, null));

        await RunAsync(queuer);

        Assert.Equal([typeof(BlackBarAnalysisTask)], queuedTasks);
        Assert.Equal(2, registrationChecks);
    }

    [Theory]
    [InlineData(TaskCompletionStatus.Completed)]
    [InlineData(TaskCompletionStatus.Cancelled)]
    [InlineData(TaskCompletionStatus.Aborted)]
    public async Task RunBefore_QueuesNothing(TaskCompletionStatus status)
    {
        TaskResult lastResult = new TaskResult { Key = BlackBarAnalysisTask.TaskKey, Status = status };
        using FirstRunQueuer queuer = CreateQueuer(1, CreateWorker(BlackBarAnalysisTask.TaskKey, lastResult));

        await RunAsync(queuer);

        Assert.Empty(queuedTasks);
    }

    [Fact]
    public async Task TaskMissingFromRegisteredTasks_StopsWithoutQueuing()
    {
        // Jellyfin registers every task in one step, so a task missing then never arrives
        using FirstRunQueuer queuer = CreateQueuer(1, CreateWorker(TrickplayTaskListener.TrickplayTaskKey, null));

        await RunAsync(queuer);

        Assert.Empty(queuedTasks);
        Assert.Equal(1, registrationChecks);
    }

    [Fact]
    public async Task QueueFails_CompletesAnyway()
    {
        // An exception escaping a background service would stop the server
        using FirstRunQueuer queuer = CreateQueuer(1, CreateWorker(BlackBarAnalysisTask.TaskKey, null), queueFails: true);

        await RunAsync(queuer);

        Assert.Empty(queuedTasks);
    }

    [Fact]
    public async Task ServerStopsBeforeRegistration_QueuesNothing()
    {
        using FirstRunQueuer queuer = CreateQueuer(int.MaxValue, CreateWorker(BlackBarAnalysisTask.TaskKey, null));
        await queuer.StartAsync(TestContext.Current.CancellationToken);
        await firstRegistrationCheck.Task.WaitAsync(RunTimeout, TestContext.Current.CancellationToken);

        await queuer.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(queuer.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Empty(queuedTasks);
    }

    // Awaiting the run rethrows any fault, so a test passes only when the queuer completes cleanly
    private static async Task RunAsync(FirstRunQueuer queuer)
    {
        await queuer.StartAsync(TestContext.Current.CancellationToken);
        await queuer.ExecuteTask!.WaitAsync(RunTimeout, TestContext.Current.CancellationToken);
    }

    private static IScheduledTaskWorker CreateWorker(string key, TaskResult? lastResult)
    {
        IScheduledTask task = InterfaceFake.Create<IScheduledTask>(new Dictionary<string, Func<MethodInfo, object?[], object?>>(StringComparer.Ordinal)
        {
            ["get_Key"] = (method, args) => key,
        });
        return InterfaceFake.Create<IScheduledTaskWorker>(new Dictionary<string, Func<MethodInfo, object?[], object?>>(StringComparer.Ordinal)
        {
            ["get_ScheduledTask"] = (method, args) => task,
            ["get_LastExecutionResult"] = (method, args) => lastResult,
        });
    }

    private FirstRunQueuer CreateQueuer(int registeredAtCheck, IScheduledTaskWorker worker, bool queueFails = false)
    {
        ITaskManager taskManager = InterfaceFake.Create<ITaskManager>(new Dictionary<string, Func<MethodInfo, object?[], object?>>(StringComparer.Ordinal)
        {
            ["get_ScheduledTasks"] = (method, args) =>
            {
                registrationChecks++;
                firstRegistrationCheck.TrySetResult();
                return registrationChecks >= registeredAtCheck ? new IScheduledTaskWorker[] { worker } : Array.Empty<IScheduledTaskWorker>();
            },
            ["QueueIfNotRunning"] = (method, args) =>
            {
                if (queueFails)
                {
                    throw new InvalidOperationException("The task manager failed");
                }

                queuedTasks.Add(method.GetGenericArguments()[0]);
                return null;
            },
        });
        return new FirstRunQueuer(taskManager, NullLogger<FirstRunQueuer>.Instance);
    }
}
