using BackingTrack.Core.Config;

using Microsoft.Extensions.AI;

using OpenAI.Responses;

namespace BackingTrack.Generation;

public static class OpenAiServiceTierExtensions
{
    // Only takes effect on the Responses API: OpenAIResponsesChatClient.AsCreateResponseOptions folds
    // this raw CreateResponseOptions into the request first, then still layers ChatOptions.Tools,
    // MaxOutputTokens etc. on top - so it composes safely with the ChatOptions built alongside it.
    // On the Chat Completions adapter the factory's result is silently ignored (type mismatch), which
    // is fine since Flex/service_tier isn't offered there for these model families anyway.
    public static void ApplyServiceTier(this ChatOptions options, OpenAiServiceTier? serviceTier)
    {
        if (serviceTier is null)
            return;

#pragma warning disable OPENAI001 // Responses-API service tier is still evaluation-only upstream, same as the ChatClient adapter in ServiceCollectionExtensions.
        ResponseServiceTier tier = serviceTier.Value switch
        {
            OpenAiServiceTier.Auto => ResponseServiceTier.Auto,
            OpenAiServiceTier.Default => ResponseServiceTier.Default,
            OpenAiServiceTier.Flex => ResponseServiceTier.Flex,
            OpenAiServiceTier.Scale => ResponseServiceTier.Scale,
            _ => throw new ArgumentOutOfRangeException(nameof(serviceTier), serviceTier, null),
        };

        options.RawRepresentationFactory = _ => new CreateResponseOptions { ServiceTier = tier };
#pragma warning restore OPENAI001
    }

    public static string ToMetricTag(this OpenAiServiceTier? serviceTier)
    {
        return serviceTier switch
        {
            OpenAiServiceTier.Flex => "flex",
            OpenAiServiceTier.Auto => "auto",
            OpenAiServiceTier.Default => "default",
            OpenAiServiceTier.Scale => "scale",
            _ => "not_specified"
        };
    }
}