using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.TrackValidation;

public record ValidationFailure(ValidationFailureType Type, NoteEvent? NoteEvent, string Reason);