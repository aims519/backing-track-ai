using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Jobs;

/// Writes straight into `JobStore` rather than going through the workflow's own messaging -
/// `IWorkflowContext.SendMessageAsync` only delivers a queued message on the *next* SuperStep, so a
/// status sent at the start of a long-running executor wouldn't actually reach the store until that
/// executor's whole handler (LLM call included) had already finished, defeating the point of a
/// live-progress update.
internal sealed class JobStatusEmitter(JobStore jobStore, ILogger<JobStatusEmitter> logger) : IJobStatusEmitter
{
    public void Emit(Guid jobId, string stage, int attempt, string message)
    {
        JobFeedback feedbackItem = new(DateTime.UtcNow, stage, message, attempt);
        jobStore.AppendJobFeedback(jobId, feedbackItem);
        logger.LogInformation("Feedback item pushed for job {Id}: {FeedbackItem}", jobId, JsonSerializer.Serialize(feedbackItem, GenerationJobJsonContext.Default.JobFeedback));
    }
}