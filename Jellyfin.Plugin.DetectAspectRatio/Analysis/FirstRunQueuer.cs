using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DetectAspectRatio.Analysis;

/// <summary>
/// Queues <see cref="BlackBarAnalysisTask"/> once on a server where it never ran, as after the plugin's first install, so the trickplay images the server already has are measured without waiting for the daily run.
/// </summary>
/// <param name="taskManager">The task manager.</param>
/// <param name="logger">The logger.</param>
public sealed class FirstRunQueuer(ITaskManager taskManager, ILogger<FirstRunQueuer> logger) : BackgroundService
{
    // Jellyfin starts hosted services before it registers scheduled tasks, and raises no event when it does
    private static readonly TimeSpan RegistrationPollInterval = TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Jellyfin registers every scheduled task in one step, early in its startup and ahead of checks that can still fail it, such as finding ffmpeg
            IReadOnlyList<IScheduledTaskWorker> workers = taskManager.ScheduledTasks;
            while (workers.Count == 0)
            {
                await Task.Delay(RegistrationPollInterval, stoppingToken).ConfigureAwait(false);
                workers = taskManager.ScheduledTasks;
            }

            QueueIfNeverRun(workers);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The server stopped before it registered its scheduled tasks
        }
        catch (Exception exception)
        {
            // NOTE: An exception escaping a background service stops the server
            logger.LogError(exception, "Detect Aspect Ratio could not queue its first computation of aspect ratios");
        }
    }

    /// <summary>
    /// Queues the task unless it ran before or is running.
    /// </summary>
    /// <param name="workers">The registered scheduled tasks.</param>
    private void QueueIfNeverRun(IReadOnlyList<IScheduledTaskWorker> workers)
    {
        IScheduledTaskWorker? worker = workers.FirstOrDefault(
            candidate => string.Equals(candidate.ScheduledTask.Key, BlackBarAnalysisTask.TaskKey, StringComparison.Ordinal));
        if (worker is null)
        {
            logger.LogWarning("Detect Aspect Ratio found no scheduled task to compute aspect ratios with");
            return;
        }

        // Every run leaves a last result, even a cancelled one, and the server keeps it across restarts and plugin updates
        if (worker.LastExecutionResult is not null)
        {
            return;
        }

        taskManager.QueueIfNotRunning<BlackBarAnalysisTask>();
        logger.LogInformation("Detect Aspect Ratio queued its first computation of aspect ratios from trickplay data");
    }
}
