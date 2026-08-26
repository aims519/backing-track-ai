using System.Text.Json.Serialization;

namespace BackingTrack.Generation.Steps.TrackValidation;

[JsonConverter(typeof(JsonStringEnumConverter<ValidationFailureType>))]
public enum ValidationFailureType
{
    InvalidTrackLengthInBeats,
    InvalidDrumNoteChoice,
    InvalidBassNoteChoice,
    InvalidChordVoicingNoteChoice,
    BassNoteOutOfExpectedRange,
    ChordVoicingNoteOutOfExpectedRange,
    InvalidMidiVelocity,
    PartEmpty,
    BeatPositionOutOfRange,
    InvalidNoteDuration,
    NoteBeyondTrackEnd
}