using BackingTrack.Generation.Steps.TrackRepair;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

using Microsoft.Extensions.Logging.Abstractions;

namespace BackingTrack.Generation.Tests.Steps.TrackRepair;

public class RepairStepTests
{
    private static RepairStep CreateRepairStep() =>
        new(null!, new TrackRepairProviderInfo("test", "test-model", 4096), new TrackRepairMetrics(), NullLogger<RepairStep>.Instance);

    private static TrackRepairPatch EmptyPatch => new([], null, null, null, null);

    [Fact]
    public void ApplyPatch_NoCorrectionsOrReplacements_ReturnsTrackWithSameContent()
    {
        RepairStep repairStep = CreateRepairStep();
        DrumNoteEvent drum = new(0, 1, 40, 100);
        BassNoteEvent bass = new(0, 1, 36, 100);
        ChordVoicingEvent chordVoicing = new(0, 4, [60, 64, 67], 100);
        CandidateTrack originalTrack = new(16, [drum], [bass], [chordVoicing]);

        CandidateTrack result = repairStep.ApplyPatch(originalTrack, EmptyPatch);

        Assert.Equal(16, result.LengthInBeats);
        Assert.Equal([drum], result.Drums);
        Assert.Equal([bass], result.Bass);
        Assert.Equal([chordVoicing], result.ChordVoicings);
    }

    [Fact]
    public void ApplyPatch_DrumNoteCorrectionSetsSingleField_MergesOntoOriginalNoteLeavingOtherFieldsUnchanged()
    {
        RepairStep repairStep = CreateRepairStep();
        DrumNoteEvent drum = new(2, 1, 40, 100);
        CandidateTrack originalTrack = new(16, [drum], [], []);
        NoteCorrection correction = new(ValidationFailureType.InvalidDrumNoteChoice, drum, null, null, [42], null);
        TrackRepairPatch patch = new([correction], null, null, null, null);

        CandidateTrack result = repairStep.ApplyPatch(originalTrack, patch);

        DrumNoteEvent updated = Assert.Single(result.Drums);
        Assert.Equal(42, updated.MidiNote);
        Assert.Equal(2, updated.BeatPosition);
        Assert.Equal(1, updated.DurationInBeats);
        Assert.Equal(100, updated.Velocity);
    }

    [Fact]
    public void ApplyPatch_MultipleCorrectionsTargetingSameOriginalNote_MergeIntoOneNoteInsteadOfDuplicating()
    {
        RepairStep repairStep = CreateRepairStep();
        BassNoteEvent bass = new(4, 1, 36, 100);
        CandidateTrack originalTrack = new(16, [], [bass], []);
        NoteCorrection beatPositionFix = new(ValidationFailureType.BeatPositionOutOfRange, bass, 8, null, null, null);
        NoteCorrection durationFix = new(ValidationFailureType.InvalidNoteDuration, bass, null, 2, null, null);
        TrackRepairPatch patch = new([beatPositionFix, durationFix], null, null, null, null);

        CandidateTrack result = repairStep.ApplyPatch(originalTrack, patch);

        BassNoteEvent updated = Assert.Single(result.Bass);
        Assert.Equal(8, updated.BeatPosition);
        Assert.Equal(2, updated.DurationInBeats);
        Assert.Equal(36, updated.MidiNote);
        Assert.Equal(100, updated.Velocity);
    }

    [Fact]
    public void ApplyPatch_ChordVoicingCorrection_UpdatesMidiNotes()
    {
        RepairStep repairStep = CreateRepairStep();
        ChordVoicingEvent chordVoicing = new(0, 4, [60, 64, 67], 100);
        CandidateTrack originalTrack = new(16, [], [], [chordVoicing]);
        NoteCorrection correction = new(ValidationFailureType.InvalidChordVoicingNoteChoice, chordVoicing, null, null, [61, 64, 67], null);
        TrackRepairPatch patch = new([correction], null, null, null, null);

        CandidateTrack result = repairStep.ApplyPatch(originalTrack, patch);

        ChordVoicingEvent updated = Assert.Single(result.ChordVoicings);
        Assert.Equal([61, 64, 67], updated.MidiNotes);
    }

    [Fact]
    public void ApplyPatch_CorrectionOriginalDoesNotMatchAnyExistingNote_DropsCorrectionLeavingTrackUnchanged()
    {
        RepairStep repairStep = CreateRepairStep();
        DrumNoteEvent existingNote = new(0, 1, 40, 100);
        DrumNoteEvent nonExistentNote = new(4, 1, 41, 100);
        CandidateTrack originalTrack = new(16, [existingNote], [], []);
        NoteCorrection correction = new(ValidationFailureType.InvalidDrumNoteChoice, nonExistentNote, null, null, [42], null);
        TrackRepairPatch patch = new([correction], null, null, null, null);

        CandidateTrack result = repairStep.ApplyPatch(originalTrack, patch);

        Assert.Equal([existingNote], result.Drums);
    }

    [Fact]
    public void ApplyPatch_NewDrumPartSupplied_ReplacesWholeDrumPart()
    {
        RepairStep repairStep = CreateRepairStep();
        DrumNoteEvent originalDrum = new(0, 1, 40, 100);
        CandidateTrack originalTrack = new(16, [originalDrum], [], []);
        DrumNoteEvent[] newDrumPart = [new(0, 1, 36, 100), new(2, 1, 38, 100)];
        TrackRepairPatch patch = new([], newDrumPart, null, null, null);

        CandidateTrack result = repairStep.ApplyPatch(originalTrack, patch);

        Assert.Equal(newDrumPart, result.Drums);
    }

    [Fact]
    public void ApplyPatch_NewLengthInBeatsSupplied_UpdatesTrackLength()
    {
        RepairStep repairStep = CreateRepairStep();
        CandidateTrack originalTrack = new(16, [], [], []);
        TrackRepairPatch patch = new([], null, null, null, 32);

        CandidateTrack result = repairStep.ApplyPatch(originalTrack, patch);

        Assert.Equal(32, result.LengthInBeats);
    }

    [Fact]
    public void ApplyPatch_NewLengthInBeatsNotSupplied_KeepsOriginalTrackLength()
    {
        RepairStep repairStep = CreateRepairStep();
        CandidateTrack originalTrack = new(16, [], [], []);

        CandidateTrack result = repairStep.ApplyPatch(originalTrack, EmptyPatch);

        Assert.Equal(16, result.LengthInBeats);
    }

    [Fact]
    public void ApplyPatch_NotesSuppliedOutOfOrder_ResultIsOrderedByBeatPosition()
    {
        RepairStep repairStep = CreateRepairStep();
        DrumNoteEvent laterNote = new(4, 1, 40, 100);
        DrumNoteEvent earlierNote = new(0, 1, 38, 100);
        CandidateTrack originalTrack = new(16, [laterNote, earlierNote], [], []);

        CandidateTrack result = repairStep.ApplyPatch(originalTrack, EmptyPatch);

        Assert.Equal([earlierNote, laterNote], result.Drums);
    }

    [Fact]
    public void ApplyPatch_NoteCorrectionTargetsNoteWithinSuppliedWholePartReplacement_CorrectionMergesOntoIt()
    {
        // Whole-part replacement establishes the base list; note corrections are then applied on
        // top of it, so a correction can target a note that only exists in the newly supplied part.
        RepairStep repairStep = CreateRepairStep();
        DrumNoteEvent originalDrum = new(0, 1, 40, 100);
        CandidateTrack originalTrack = new(16, [originalDrum], [], []);
        DrumNoteEvent newDrum = new(0, 1, 36, 100);
        DrumNoteEvent[] newDrumPart = [newDrum];
        NoteCorrection correction = new(ValidationFailureType.InvalidDrumNoteChoice, newDrum, null, null, [38], null);
        TrackRepairPatch patch = new([correction], newDrumPart, null, null, null);

        CandidateTrack result = repairStep.ApplyPatch(originalTrack, patch);

        DrumNoteEvent updated = Assert.Single(result.Drums);
        Assert.Equal(38, updated.MidiNote);
    }

    [Fact]
    public void ApplyPatch_NoteCorrectionTargetsNoteFromDiscardedOriginalPart_CorrectionIsDroppedNotApplied()
    {
        // A correction referencing a note from the pre-replacement part no longer exists once the
        // whole-part replacement establishes the base list, so it is dropped like any other mismatch.
        RepairStep repairStep = CreateRepairStep();
        DrumNoteEvent originalDrum = new(0, 1, 40, 100);
        CandidateTrack originalTrack = new(16, [originalDrum], [], []);
        DrumNoteEvent[] newDrumPart = [new(0, 1, 36, 100)];
        NoteCorrection correction = new(ValidationFailureType.InvalidDrumNoteChoice, originalDrum, null, null, [42], null);
        TrackRepairPatch patch = new([correction], newDrumPart, null, null, null);

        CandidateTrack result = repairStep.ApplyPatch(originalTrack, patch);

        Assert.Equal(newDrumPart, result.Drums);
    }
}
