using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.TrackValidation;

public class TrackValidator(ScaleMatcher scaleMatcher) : ITrackValidator
{
    public IReadOnlyList<ValidationFailure> ValidateLengthInBeats(CandidateTrack candidateTrack,  GenerationJob generationJob)
    {
        int beatsPerBar = generationJob.TimeSignatureBeatCount;
        int barCount = generationJob.Chords.Count;
        IList<ValidationFailure> failures = [];

        if (Math.Abs(candidateTrack.LengthInBeats - (beatsPerBar * barCount)) > 0)
            failures.Add(new(Type: ValidationFailureType.InvalidTrackLengthInBeats,null,$"Unexpected {nameof(candidateTrack.LengthInBeats)}. Expected {(beatsPerBar * barCount)} bars but got {candidateTrack.LengthInBeats}"));

        return (IReadOnlyList<ValidationFailure>)failures;
    }

    public IReadOnlyList<ValidationFailure> ValidateDrumNoteChoices(IReadOnlyList<DrumNoteEvent> drumNoteEvents)
    {
        IList<ValidationFailure> failures = [];
        foreach (DrumNoteEvent drumNoteEvent in drumNoteEvents.Where(n => n.MidiNote is < GeneralMidiDrumNoteMin or > GeneralMidiDrumNoteMax))
        {
            failures.Add(new(Type: ValidationFailureType.InvalidDrumNoteChoice,drumNoteEvent,$"Unexpected MIDI note in {nameof(DrumNoteEvent)}, {drumNoteEvent.MidiNote}, at position {drumNoteEvent.BeatPosition}. Expected {GeneralMidiDrumNoteMin} <= Note <= {GeneralMidiDrumNoteMax}"));
        }

        return (IReadOnlyList<ValidationFailure>)failures;
    }

    public IReadOnlyList<ValidationFailure> ValidateBassNoteChoices(IReadOnlyList<BassNoteEvent> bassNoteEvents, 
        IReadOnlyList<MatchedChord> matchedChords, IReadOnlyList<MatchedScale> matchedScales, 
        GenerationJob generationJob)
    {
        // Loop through each chord with associated scales, convert bass MIDI notes to pitch classes
        // Check bass pitches are valid
        IList<ValidationFailure> failures = [];
        int beatsPerBar = generationJob.TimeSignatureBeatCount;
        for (int bar = 1; bar <= matchedChords.Count; bar++)
        {
            MatchedChord matchedChord = matchedChords[bar - 1];
            IReadOnlyList<MatchedScale> scalesForChord = matchedScales.Where(s => s.Chord.Equals(matchedChord)).ToList();
            
            IReadOnlyList<BassNoteEvent> bassNoteEventsInBar 
                = bassNoteEvents.Where(b => (b.BeatPosition >= (bar-1) * beatsPerBar
                    && (b.BeatPosition < (bar * beatsPerBar)))).ToList();

            // Compute valid pitch classes for this bar (chord notes plus scale notes)
            int chordPitchClass = scaleMatcher.RootToPitchClass(matchedChord.Root);

            HashSet<int> validPitchClasses = scaleMatcher
                .TransposeIntervalsByPitchClass(chordPitchClass, matchedChord.ChordEntry.Intervals)
                .ToHashSet();

            foreach (MatchedScale matchedScale in scalesForChord)
            {
                foreach (int pc in scaleMatcher.TransposeIntervalsByPitchClass(chordPitchClass, matchedScale.ScaleEntry.Intervals))
                    validPitchClasses.Add(pc);
            }
            
            foreach (BassNoteEvent bassNoteEvent in bassNoteEventsInBar)
            {
                if(!validPitchClasses.Contains(bassNoteEvent.MidiNote % 12))
                    failures.Add(new ValidationFailure(Type: ValidationFailureType.InvalidBassNoteChoice, bassNoteEvent,$"Unexpected {nameof(BassNoteEvent)} MIDI note in bar {bar} at position {bassNoteEvent.BeatPosition}. Note: {bassNoteEvent.MidiNote}, Pitch class {bassNoteEvent.MidiNote % 12}. Expected pitch classes: [{string.Join(',', validPitchClasses.Order())}]"));
            }
        }
        
        // Also Check no bass notes fall outside expected range
        foreach (BassNoteEvent bassNoteEvent in bassNoteEvents.Where(b => b.MidiNote is < BassMidiNoteMin or > BassMidiNoteMax)) 
            failures.Add(new ValidationFailure(Type: ValidationFailureType.BassNoteOutOfExpectedRange,bassNoteEvent,$"Unexpected {nameof(BassNoteEvent)} MIDI note, {bassNoteEvent.MidiNote}. Expected range Expected {BassMidiNoteMin} <= Note <= {BassMidiNoteMax}"));
        
        return (IReadOnlyList<ValidationFailure>)failures;
    }

    public IReadOnlyList<ValidationFailure> ValidateChordVoicingNoteChoices(IReadOnlyList<ChordVoicingEvent> chordVoicingEvents, IReadOnlyList<MatchedChord> matchedChords,
        IReadOnlyList<MatchedScale> matchedScales, GenerationJob generationJob)
    {
        // Loop through each chord with associated scales, convert bass MIDI notes to pitch classes
        // Check bass pitches are valid
        IList<ValidationFailure> failures = [];
        int beatsPerBar = generationJob.TimeSignatureBeatCount;
        for (int bar = 1; bar <= matchedChords.Count; bar++)
        {
            MatchedChord matchedChord = matchedChords[bar - 1];
            IReadOnlyList<MatchedScale> scalesForChord = matchedScales.Where(s => s.Chord.Equals(matchedChord)).ToList();
            
            IReadOnlyList<ChordVoicingEvent> chordVoicingEventsInBar 
                = chordVoicingEvents.Where(b => (b.BeatPosition >= (bar-1) * beatsPerBar
                    && (b.BeatPosition < (bar * beatsPerBar)))).ToList();

            // Compute valid pitch classes for this bar (chord notes plus scale notes)
            int chordPitchClass = scaleMatcher.RootToPitchClass(matchedChord.Root);

            HashSet<int> validPitchClasses = scaleMatcher
                .TransposeIntervalsByPitchClass(chordPitchClass, matchedChord.ChordEntry.Intervals)
                .ToHashSet();

            foreach (MatchedScale matchedScale in scalesForChord)
            {
                foreach (int pc in scaleMatcher.TransposeIntervalsByPitchClass(chordPitchClass, matchedScale.ScaleEntry.Intervals))
                    validPitchClasses.Add(pc);
            }
            
            foreach (ChordVoicingEvent chordVoicingEvent in chordVoicingEventsInBar)
            {
                foreach (int midiNote in chordVoicingEvent.MidiNotes)
                {
                    if(!validPitchClasses.Contains(midiNote % 12))
                        failures.Add(new ValidationFailure(ValidationFailureType.InvalidChordVoicingNoteChoice, chordVoicingEvent,$"Unexpected {nameof(ChordVoicingEvent)} MIDI note in bar {bar} at position {chordVoicingEvent.BeatPosition}. Note: {midiNote}, Pitch class: {midiNote % 12}. Expected pitch classes: [{string.Join(',', validPitchClasses.Order())}]"));
                }
            }
        }
        
        // Also Check no chord voicing notes fall outside expected range
        foreach (ChordVoicingEvent chordVoicingEvent in chordVoicingEvents)
        {
            foreach (int midiNote in chordVoicingEvent.MidiNotes.Where(n => n is < ChordVoicingNoteMin or > ChordVoicingNoteMax))
            {
                failures.Add(new ValidationFailure(Type: ValidationFailureType.ChordVoicingNoteOutOfExpectedRange, chordVoicingEvent,$"Unexpected {nameof(ChordVoicingEvent)} MIDI note, {midiNote}. Expected range Expected {ChordVoicingNoteMin} <= Note <= {ChordVoicingNoteMax}"));
            }
        } 
        
        return (IReadOnlyList<ValidationFailure>)failures;
    }

    public IReadOnlyList<ValidationFailure> ValidateMidiVelocities(CandidateTrack candidateTrack)
    {
        IList<ValidationFailure> failures = [];

        foreach (DrumNoteEvent drumNoteEvent in candidateTrack.Drums.Where(n => n.Velocity is < MidiVelocityMin or > MidiVelocityMax))
            failures.Add(new ValidationFailure(Type: ValidationFailureType.InvalidMidiVelocity, drumNoteEvent, $"Unexpected {nameof(DrumNoteEvent)} velocity, {drumNoteEvent.Velocity}. Expected {MidiVelocityMin} <= Velocity <= {MidiVelocityMax}"));
        
        foreach (BassNoteEvent bassNoteEvent in candidateTrack.Bass.Where(n => n.Velocity is < MidiVelocityMin or > MidiVelocityMax))
            failures.Add(new ValidationFailure(Type: ValidationFailureType.InvalidMidiVelocity,bassNoteEvent, $"Unexpected {nameof(BassNoteEvent)} velocity, {bassNoteEvent.Velocity}. Expected {MidiVelocityMin} <= Velocity <= {MidiVelocityMax}"));

        foreach (ChordVoicingEvent chordVoicingEvent in candidateTrack.ChordVoicings.Where(n => n.Velocity is < MidiVelocityMin or > MidiVelocityMax))
            failures.Add(new ValidationFailure(Type: ValidationFailureType.InvalidMidiVelocity, chordVoicingEvent, $"Unexpected {nameof(ChordVoicingEvent)} velocity, {chordVoicingEvent.Velocity}. Expected {MidiVelocityMin} <= Velocity <= {MidiVelocityMax}"));
        
        return (IReadOnlyList<ValidationFailure>)failures;
    }

    public IReadOnlyList<ValidationFailure> ValidateNoPartEmpty(CandidateTrack candidateTrack)
    {
        IList<ValidationFailure> failures = [];
        
        if(candidateTrack.Drums.Count == 0)
            failures.Add(new ValidationFailure(Type: ValidationFailureType.PartEmpty, null, $"{nameof(DrumNoteEvent)} list is empty"));
            
        if(candidateTrack.Bass.Count == 0)
            failures.Add(new ValidationFailure(Type: ValidationFailureType.PartEmpty, null,$"{nameof(BassNoteEvent)} list is empty"));
        
        if(candidateTrack.ChordVoicings.Count == 0)
            failures.Add(new ValidationFailure(Type: ValidationFailureType.PartEmpty, null, $"{nameof(ChordVoicingEvent)} list is empty"));
        
        return (IReadOnlyList<ValidationFailure>)failures;
    }

    public IReadOnlyList<ValidationFailure> ValidateBeatPositionsAndDurations(CandidateTrack candidateTrack, GenerationJob generationJob)
    {
        IList<ValidationFailure> failures = [];
        
        int beatsPerBar = generationJob.TimeSignatureBeatCount;
        int numberOfBars = generationJob.Chords.Count;
        const int minBeatPosition = 0;
        int maxBeatPosition = beatsPerBar * numberOfBars;

        // Check all beatPositions are in the allowed range
        foreach (DrumNoteEvent drumNoteEvent in candidateTrack.Drums.Where(n => n.BeatPosition < minBeatPosition || n.BeatPosition > maxBeatPosition))
            failures.Add(new ValidationFailure(Type: ValidationFailureType.BeatPositionOutOfRange, drumNoteEvent, $"Unexpected {nameof(DrumNoteEvent)} {nameof(DrumNoteEvent.BeatPosition)}, {drumNoteEvent.BeatPosition}. Expected {minBeatPosition} <= {nameof(DrumNoteEvent.BeatPosition)} <= {maxBeatPosition}"));
        
        foreach (BassNoteEvent bassNoteEvent in candidateTrack.Bass.Where(n => n.BeatPosition < minBeatPosition || n.BeatPosition > maxBeatPosition))
            failures.Add(new ValidationFailure(Type: ValidationFailureType.BeatPositionOutOfRange,bassNoteEvent, $"Unexpected {nameof(BassNoteEvent)} {nameof(BassNoteEvent.BeatPosition)}, {bassNoteEvent.BeatPosition}. Expected {minBeatPosition} <= {nameof(BassNoteEvent.BeatPosition)} <= {maxBeatPosition}"));

        foreach (ChordVoicingEvent chordVoicingEvent in candidateTrack.ChordVoicings.Where(n => n.BeatPosition < minBeatPosition || n.BeatPosition > maxBeatPosition))
            failures.Add(new ValidationFailure(Type: ValidationFailureType.BeatPositionOutOfRange,chordVoicingEvent, $"Unexpected {nameof(ChordVoicingEvent)} {nameof(ChordVoicingEvent.BeatPosition)}, {chordVoicingEvent.BeatPosition}. Expected {minBeatPosition} <= {nameof(ChordVoicingEvent.BeatPosition)} <= {maxBeatPosition}"));
        
        // Check all durations are > 0
        foreach (DrumNoteEvent drumNoteEvent in candidateTrack.Drums.Where(n => n.DurationInBeats <= 0))
            failures.Add(new ValidationFailure(Type: ValidationFailureType.InvalidNoteDuration, drumNoteEvent,$"Unexpected {nameof(DrumNoteEvent)} {nameof(DrumNoteEvent.DurationInBeats)}, {drumNoteEvent.DurationInBeats}. Expected > 0"));
        
        foreach (BassNoteEvent bassNoteEvent in candidateTrack.Bass.Where(n => n.DurationInBeats <= 0))
            failures.Add(new ValidationFailure(Type: ValidationFailureType.InvalidNoteDuration,bassNoteEvent, $"Unexpected {nameof(BassNoteEvent)} {nameof(BassNoteEvent.DurationInBeats)}, {bassNoteEvent.DurationInBeats}. Expected > 0"));

        foreach (ChordVoicingEvent chordVoicingEvent in candidateTrack.ChordVoicings.Where(n => n.DurationInBeats <= 0))
            failures.Add(new ValidationFailure(Type: ValidationFailureType.InvalidNoteDuration, chordVoicingEvent, $"Unexpected {nameof(ChordVoicingEvent)} {nameof(ChordVoicingEvent.DurationInBeats)}, {chordVoicingEvent.DurationInBeats}. Expected > 0"));
        
        // Check no notes extend beyond track end
        foreach (DrumNoteEvent drumNoteEvent in candidateTrack.Drums.Where(n => (n.BeatPosition + n.DurationInBeats) > maxBeatPosition))
            failures.Add(new ValidationFailure(Type: ValidationFailureType.NoteBeyondTrackEnd, drumNoteEvent, $"Unexpected {nameof(DrumNoteEvent)} {nameof(DrumNoteEvent.BeatPosition)} + {nameof(DrumNoteEvent.DurationInBeats)}, ({drumNoteEvent.BeatPosition} + {drumNoteEvent.DurationInBeats}). Expected < {maxBeatPosition}"));
        
        foreach (BassNoteEvent bassNoteEvent in candidateTrack.Bass.Where(n => (n.BeatPosition + n.DurationInBeats) > maxBeatPosition))
            failures.Add(new ValidationFailure(Type: ValidationFailureType.NoteBeyondTrackEnd, bassNoteEvent,$"Unexpected {nameof(BassNoteEvent)} {nameof(BassNoteEvent.BeatPosition)} + {nameof(BassNoteEvent.DurationInBeats)}, ({bassNoteEvent.BeatPosition} + {bassNoteEvent.DurationInBeats}). Expected < {maxBeatPosition}"));
        
        foreach (ChordVoicingEvent chordVoicingNoteEvent in candidateTrack.ChordVoicings.Where(n => (n.BeatPosition + n.DurationInBeats) > maxBeatPosition))
            failures.Add(new ValidationFailure(Type: ValidationFailureType.NoteBeyondTrackEnd, chordVoicingNoteEvent,$"Unexpected {nameof(ChordVoicingEvent)} {nameof(ChordVoicingEvent.BeatPosition)} + {nameof(ChordVoicingEvent.DurationInBeats)}, ({chordVoicingNoteEvent.BeatPosition} + {chordVoicingNoteEvent.DurationInBeats}). Expected < {maxBeatPosition}"));
        
        return (IReadOnlyList<ValidationFailure>)failures;
    }

    private const int MidiVelocityMin = 1;
    private const int MidiVelocityMax = 127;
    
    private const int GeneralMidiDrumNoteMin = 35;
    private const int GeneralMidiDrumNoteMax = 81;
    
    private const int BassMidiNoteMin = 28; // E1
    private const int BassMidiNoteMax = 57; // A3

    private const int ChordVoicingNoteMin = 48; // C3
    private const int ChordVoicingNoteMax = 79; // G5
}