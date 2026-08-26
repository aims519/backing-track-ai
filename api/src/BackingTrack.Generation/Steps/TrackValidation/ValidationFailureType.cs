namespace BackingTrack.Generation.Steps.TrackValidation;

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