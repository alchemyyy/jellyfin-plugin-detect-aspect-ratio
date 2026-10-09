using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Jellyfin.Plugin.DetectAspectRatio.Analysis;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

public sealed class TrickplayTaskListenerTests
{
    private readonly List<Type> queuedTasks = new List<Type>();
    private EventHandler<TaskCompletionEventArgs>? taskCompleted;

    [Theory]
    [InlineData(TaskCompletionStatus.Completed)]
    [InlineData(TaskCompletionStatus.Cancelled)]
    [InlineData(TaskCompletionStatus.Failed)]
    public async Task TrickplayTaskFinished_QueuesTheMeasurement(TaskCompletionStatus status)
    {
        TrickplayTaskListener listener = new TrickplayTaskListener(CreateTaskManager(), NullLogger<TrickplayTaskListener>.Instance);
        await listener.StartAsync(TestContext.Current.CancellationToken);

        RaiseTaskCompleted(TrickplayTaskListener.TrickplayTaskKey, status);

        Assert.Equal([typeof(BlackBarAnalysisTask)], queuedTasks);
    }

    [Theory]
    [InlineData("RefreshLibrary")]
    [InlineData(BlackBarAnalysisTask.TaskKey)]
    public async Task OtherTaskFinished_QueuesNothing(string key)
    {
        TrickplayTaskListener listener = new TrickplayTaskListener(CreateTaskManager(), NullLogger<TrickplayTaskListener>.Instance);
        await listener.StartAsync(TestContext.Current.CancellationToken);

        RaiseTaskCompleted(key, TaskCompletionStatus.Completed);

        Assert.Empty(queuedTasks);
    }

    [Fact]
    public async Task StopAsync_Unsubscribes()
    {
        TrickplayTaskListener listener = new TrickplayTaskListener(CreateTaskManager(), NullLogger<TrickplayTaskListener>.Instance);
        await listener.StartAsync(TestContext.Current.CancellationToken);

        await listener.StopAsync(TestContext.Current.CancellationToken);

        Assert.Null(taskCompleted);
    }

    private ITaskManager CreateTaskManager()
    {
        return InterfaceFake.Create<ITaskManager>(new Dictionary<string, Func<MethodInfo, object?[], object?>>(StringComparer.Ordinal)
        {
            ["add_TaskCompleted"] = (method, args) => taskCompleted += (EventHandler<TaskCompletionEventArgs>)args[0]!,
            ["remove_TaskCompleted"] = (method, args) => taskCompleted -= (EventHandler<TaskCompletionEventArgs>)args[0]!,
            ["QueueScheduledTask"] = (method, args) =>
            {
                queuedTasks.Add(method.GetGenericArguments()[0]);
                return null;
            },
        });
    }

    private void RaiseTaskCompleted(string key, TaskCompletionStatus status)
    {
        IScheduledTaskWorker worker = InterfaceFake.Create<IScheduledTaskWorker>(new Dictionary<string, Func<MethodInfo, object?[], object?>>(StringComparer.Ordinal));
        TaskResult result = new TaskResult { Key = key, Status = status };

        // The task manager raises the event with the worker as sender
        taskCompleted?.Invoke(worker, new TaskCompletionEventArgs(worker, result));
    }
}
