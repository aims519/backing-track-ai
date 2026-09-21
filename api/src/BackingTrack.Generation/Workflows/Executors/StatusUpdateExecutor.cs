using System.Text.Json;

using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Workflows.Inputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Workflows.Executors;

internal sealed class StatusUpdateExecutor(JobStore jobStore, ILogger<StatusUpdateExecutor> logger) : Executor("StatusUpdateExecutor")
{
    private void Handle(StatusUpdateInput input, IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        JobFeedback feedbackItem = new(input.Timestamp, input.Stage, input.Message, input.Attempt);
        jobStore.AppendJobFeedback(input.JobId, feedbackItem);
        logger.LogInformation("Feedback item pushed for job {Id}: {FeedbackItem}", input.JobId, JsonSerializer.Serialize(feedbackItem, GenerationJobJsonContext.Default.JobFeedback));
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .ConfigureRoutes(routes => routes.AddHandler<StatusUpdateInput>(Handle));
}