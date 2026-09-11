using System.Text.Json.Serialization;

namespace BackingTrack.Generation.Steps.Critic;

[JsonConverter(typeof(JsonStringEnumConverter<CriticDimension>))]
public enum CriticDimension
{
    ChordToneAdherence,
    VoiceLeading,
    RhythmAndFeel
}