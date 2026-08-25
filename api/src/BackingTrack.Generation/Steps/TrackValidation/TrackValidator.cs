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
            failures.Add(new($"Unexpected {nameof(candidateTrack.LengthInBeats)}. Expected {(beatsPerBar * barCount)} bars but got {candidateTrack.LengthInBeats}"));

        return (IReadOnlyList<ValidationFailure>)failures;
    }

    public IReadOnlyList<ValidationFailure> ValidateDrumNoteChoices(IReadOnlyList<DrumNoteEvent> drumNoteEvents)
    {
        IList<ValidationFailure> failures = [];
        foreach (DrumNoteEvent drumNoteEvent in drumNoteEvents.Where(n => n.MidiNote is < GeneralMidiDrumNoteMin or > GeneralMidiDrumNoteMax))
        {
            failures.Add(new($"Unexpected MIDI note in {nameof(DrumNoteEvent)}, {drumNoteEvent.MidiNote}, at position {drumNoteEvent.BeatPosition}. Expected {GeneralMidiDrumNoteMin} <= Note <= {GeneralMidiDrumNoteMax}"));
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
                    failures.Add(new ValidationFailure($"Unexpected {nameof(BassNoteEvent)} MIDI note in bar {bar}. Note: {bassNoteEvent.MidiNote}"));
            }
        }
        
        // Also Check no bass notes fall outside expected range
        foreach (BassNoteEvent bassNoteEvent in bassNoteEvents.Where(b => b.MidiNote is < BassMidiNoteMin or > BassMidiNoteMax)) 
            failures.Add(new ValidationFailure($"Unexpected {nameof(BassNoteEvent)} MIDI note, {bassNoteEvent.MidiNote}. Expected range Expected {BassMidiNoteMin} <= Note <= {BassMidiNoteMax}"));
        
        return (IReadOnlyList<ValidationFailure>)failures;
    }

    private const int GeneralMidiDrumNoteMin = 35;
    private const int GeneralMidiDrumNoteMax = 81;
    
    private const int BassMidiNoteMin = 28; // E1
    private const int BassMidiNoteMax = 57; // A3

    private string MIDINumberToNote(int midiNumber)
    {
        return (midiNumber % 12) switch
        {
            0 => "C",
            1 => "C#",
            2 => "D",
            3 => "D#",
            4 => "E",
            5 => "F",
            6 => "F#",
            7 => "G",
            8 => "G#",
            9 => "A",
            10 => "A#",
            11 => "B",
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}