using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DetectAspectRatio.Analysis;

/// <summary>
/// Queues <see cref="BlackBarAnalysisTask"/> whenever Jellyfin's Generate Trickplay Images task finishes, so new trickplay images are measured before playback asks.
/// </summary>
/// <param name="taskManager">The task manager.</param>
/// <param name="logger">The logger.</param>
public sealed class TrickplayTaskListener(ITaskManager taskManager, ILogger<TrickplayTaskListener> logger) : IHostedService
{
    /// <summary>
    /// The key of Jellyfin's Generate Trickplay Images task.
    /// </summary>
    public const string TrickplayTaskKey = "RefreshTrickplayImages";

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        taskManager.TaskCompleted += OnTaskCompleted;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        taskManager.TaskCompleted -= OnTaskCompleted;
        return Task.CompletedTask;
    }

    private void OnTaskCompleted(object? sender, TaskCompletionEventArgs eventArgs)
    {
        if (!string.Equals(eventArgs.Result.Key, TrickplayTaskKey, StringComparison.Ordinal))
        {
            return;
        }

        // Any outcome counts: a cancelled or failed run still leaves the trickplay images it finished
        try
        {
            taskManager.QueueScheduledTask<BlackBarAnalysisTask>();
        }
        catch (Exception exception)
        {
            // NOTE: The task manager raises this event while it finishes the trickplay task, so nothing may escape
            logger.LogError(exception, "Detect Aspect Ratio could not queue its measurement after Generate Trickplay Images");
        }
    }
}
