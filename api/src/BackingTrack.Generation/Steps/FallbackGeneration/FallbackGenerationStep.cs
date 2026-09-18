using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.FallbackGeneration;

public class FallbackGenerationStep(ScaleMatcher scaleMatcher) : IFallbackGenerationStep
{
    private const int DefaultBassNoteVelocity = 80;
    private const int DefaultDrumNoteVelocity = 80;
    private const int DefaultChordVelocity = 80;
    private const int DrumClosedHiHatMidiNote = 42;
    private const int DrumBassDrumMidiNote = 36;
    private const int DrumSnareMidiNote = 40;
    
    public Task<CandidateTrack> GenerateFallbackCandidateTrackAsync(string keyRoot, 
        KeyMode keyMode, 
        int tempo, 
        int timeSignatureBeatCount,
        int timeSignatureBeatUnit, 
        MatchedStyle style, 
        IReadOnlyList<MatchedChord> chords, 
        IReadOnlyList<MatchedScale> scales,
        CancellationToken cancellationToken = default)
    {
        // Bass: chord root notes on the beat pulse
        IReadOnlyList<BassNoteEvent> bassNotes = ConstructBassNoteEvents(chords, timeSignatureBeatCount);
        
        // Drums: Kick on beat 1/ Snare on last beat/ hi-hat every beat
        IReadOnlyList<DrumNoteEvent> drumNotes = ConstructDrumNoteEvents(chords.Count, timeSignatureBeatCount);
        
        // Chords: one chord per bar, sustained for the bar duration, every note in chord
        IReadOnlyList<ChordVoicingEvent> chordNotes = ConstructChordVoicingEvents(chords, timeSignatureBeatCount);

        int lengthInBeats = timeSignatureBeatCount * chords.Count;
        CandidateTrack track = new(lengthInBeats,
            drumNotes,
            bassNotes,
            chordNotes);
        
        return Task.FromResult(track);
    }

    /// <summary>
    /// Bass part generated with one note on each beat of the bar, always the chord root
    /// </summary>
    /// <param name="chords">Ordered list of chords for the track</param>
    /// <param name="beatsPerBar">Number of beats in a bar of the track</param>
    /// <returns></returns>
    private IReadOnlyList<BassNoteEvent> ConstructBassNoteEvents(IReadOnlyList<MatchedChord> chords, int beatsPerBar)
    {
        List<BassNoteEvent> bassNoteEvents = [];

        for (int chordIndex = 0; chordIndex < chords.Count; chordIndex++)
        {
            MatchedChord chord = chords[chordIndex];
            int rootPitchClass = scaleMatcher.RootToPitchClass(chord.Root);
            int midiNote = PitchClassToBassMidiNote(rootPitchClass);
            
            int startingBeat = beatsPerBar * chordIndex;
            for (int beat = startingBeat; beat < (startingBeat + beatsPerBar); beat++)
            {
                BassNoteEvent note = new(beat, 1, midiNote, DefaultBassNoteVelocity);
                bassNoteEvents.Add(note);
            }
        }
        
        return bassNoteEvents;
    }

    
    /// <summary>
    /// Example:
    /// C (pitch class 0)
    /// 0 + ((2+1) * 12) = 36
    ///
    /// B (pitch class 11)
    /// 11 + ((2+1) * 12) = 47
    /// </summary>
    /// <param name="pitchClass"></param>
    /// <returns></returns>
    private int PitchClassToBassMidiNote(int pitchClass)
    {
        const int octave = 2;
        int note =  pitchClass + ((octave + 1) * 12);

        if(note < Constants.BassMidiNoteMin)
            note = pitchClass + ((octave + 1 + 1) * 12);
        else if(note > Constants.BassMidiNoteMax)
            note = pitchClass + ((octave + 1 - 1) * 12);
        
        return note;
    }
    
    private IReadOnlyList<DrumNoteEvent> ConstructDrumNoteEvents(int numberOfBars, int beatsPerBar)
    {
        List<DrumNoteEvent> drumNoteEvents = [];

        for (int bar = 1; bar <= numberOfBars; bar++)
        {
            int startingBeat = beatsPerBar * (bar - 1);
            
            // Bass Drum
            DrumNoteEvent bassDrumNote = new(startingBeat, 1, DrumBassDrumMidiNote, DefaultDrumNoteVelocity);
            drumNoteEvents.Add(bassDrumNote);

            // Hi Hat
            for (int beat = startingBeat; beat < (startingBeat + beatsPerBar); beat++)
            {
                DrumNoteEvent hiHatNote = new(beat, 1, DrumClosedHiHatMidiNote, DefaultDrumNoteVelocity);
                drumNoteEvents.Add(hiHatNote);
            }
            
            // Snare
            int lastBeat = startingBeat + beatsPerBar - 1;
            DrumNoteEvent snareNote =  new(lastBeat, 1, DrumSnareMidiNote, DefaultDrumNoteVelocity);
            drumNoteEvents.Add(snareNote);
        }
        
        return drumNoteEvents;
    }
    
    private IReadOnlyList<ChordVoicingEvent> ConstructChordVoicingEvents(IReadOnlyList<MatchedChord> chords, int beatsPerBar)
    {
        List<ChordVoicingEvent> chordVoicingEvents = [];

        for (int chordIndex = 0; chordIndex < chords.Count; chordIndex++)
        {
            MatchedChord chord = chords[chordIndex];
            int rootPitchClass = scaleMatcher.RootToPitchClass(chord.Root);
            int[] transposedChordIntervals = scaleMatcher.TransposeIntervalsByPitchClass(rootPitchClass, chord.ChordEntry.Intervals);
            int[] chordNotes = transposedChordIntervals.Select(PitchClassToChordMidiNote).ToArray();
            
            int beatPosition = beatsPerBar * chordIndex;
            ChordVoicingEvent chordVoicingEvent = new(beatPosition,
                beatsPerBar,
                chordNotes,
                DefaultChordVelocity);
            chordVoicingEvents.Add(chordVoicingEvent);
        }
        
        return chordVoicingEvents;
    }

    /// <summary>
    /// Example:
    /// C (pitch class 0)
    /// 0 + ((4+1) * 12) = 60
    ///
    /// B (pitch class 11)
    /// 11 + ((4+1) * 12) = 71
    /// </summary>
    /// <param name="pitchClass"></param>
    /// <returns></returns>
    private int PitchClassToChordMidiNote(int pitchClass)
    {
        const int octave = 4;
        int note =  pitchClass + ((octave + 1) * 12);

        if(note < Constants.ChordVoicingNoteMin)
            note = pitchClass + ((octave + 1 + 1) * 12);
        else if(note > Constants.ChordVoicingNoteMax)
            note = pitchClass + ((octave + 1 - 1) * 12);
        
        return note;
    }
}