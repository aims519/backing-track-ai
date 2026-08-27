using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.TrackRepair;

public record TrackRepairPatch(IReadOnlyList<NoteCorrection> NoteCorrections, 
    IReadOnlyList<DrumNoteEvent>? NewDrumPart,
    IReadOnlyList<BassNoteEvent>? NewBassPart,
    IReadOnlyList<ChordVoicingEvent>? NewChordVoicingPart,
    int? NewLengthInBeats);

public record NoteCorrection(ValidationFailureType FailureType, NoteEvent Original, double? NewBeatPosition, double? NewDurationInBeats, int[]? NewMidiNotes, int? NewVelocity);