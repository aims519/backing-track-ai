using System.Threading.Channels;

namespace BackingTrack.Generation.Jobs;

public sealed class GenerationJobQueue(Channel<Guid> channel)
{
    public ChannelReader<Guid> Reader { get; } = channel.Reader;
    public ChannelWriter<Guid> Writer { get; } = channel.Writer;
}