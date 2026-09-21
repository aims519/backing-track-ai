using BackingTrack.Generation.Workflows.Inputs;

using Microsoft.Agents.AI.Workflows;

namespace BackingTrack.Generation.Tests.Workflows;

/// <summary>
/// A minimal <see cref="IWorkflowContext"/> for driving an Executor's HandleAsync directly in a
/// unit test, without going through InProcessExecution/WorkflowBuilder. Records everything sent
/// or yielded; state-store members throw, since none of the executors under test use them.
/// </summary>
internal sealed class FakeWorkflowContext : IWorkflowContext
{
    public List<object> SentMessages { get; } = [];
    public List<object> YieldedOutputs { get; } = [];

    /// <summary>
    /// SentMessages minus the fire-and-forget StatusUpdateInput every executor now sends
    /// alongside its real routing message; tests assert workflow routing against this so they
    /// don't have to know about the status-update instrumentation on every single case.
    /// </summary>
    public IReadOnlyList<object> WorkflowMessages => [.. SentMessages.Where(message => message is not StatusUpdateInput)];

    public ValueTask AddEventAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default) => default;

    public ValueTask SendMessageAsync(object message, string? targetId = null, CancellationToken cancellationToken = default)
    {
        SentMessages.Add(message);
        return default;
    }

    public ValueTask YieldOutputAsync(object output, CancellationToken cancellationToken = default)
    {
        YieldedOutputs.Add(output);
        return default;
    }

    public ValueTask RequestHaltAsync() => default;

    public ValueTask<T?> ReadStateAsync<T>(string key, string? scopeName = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("State store is not used by these tests");

    public ValueTask<T?> ReadStateAsync<T>(string key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("State store is not used by these tests");

    public ValueTask<T> ReadOrInitStateAsync<T>(string key, Func<T> defaultFactory, string? scopeName, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("State store is not used by these tests");

    public ValueTask<T> ReadOrInitStateAsync<T>(string key, Func<T> defaultFactory, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("State store is not used by these tests");

    public ValueTask<HashSet<string>> ReadStateKeysAsync(string? scopeName, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("State store is not used by these tests");

    public ValueTask QueueStateUpdateAsync<T>(string key, T? value, string? scopeName = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("State store is not used by these tests");

    public ValueTask QueueStateUpdateAsync<T>(string key, T? value, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("State store is not used by these tests");

    public ValueTask QueueClearScopeAsync(string? scopeName, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("State store is not used by these tests");

    public ValueTask QueueClearScopeAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("State store is not used by these tests");

    public IReadOnlyDictionary<string, string> TraceContext => new Dictionary<string, string>();

    public bool ConcurrentRunsEnabled => false;
}