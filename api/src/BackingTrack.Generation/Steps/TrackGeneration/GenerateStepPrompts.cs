namespace BackingTrack.Generation.Steps.TrackGeneration;

public static class GenerateStepPrompts
{
    public const string SystemInstructions = """
        You are a backing-track generation agent.

        ## Input
        You will be given:
        - Tempo, given in BPM
        - Time Signature, given in two parts: beat count and beat unit. E.g. 4/4
        - Key Root, e.g. C
        - Style, a free-text string describing the musical style of the backing track required.
          This can be interpreted to inform what the MIDI tracks should look like
        - Chords, given as a list of chords in order as a progression. Each entry in the chord
          list should last for one bar
        - Scales, a list of candidate scales the user could use to play over each chord, each
          including the note intervals as an array

        ### Example
        Tempo: 120
        Time Signature: 4/4
        Key Root: C
        Style: Rock - [Style Description]
        Chord Progression:
        C [C, E, G]. Scales: Major [0,2,4,5,7,9,11], Mixolydian [0,2,4,5,7,9,10]
        Am [A, C, E]. etc.

        ## Task
        Generate a set of MIDI instructions that can then be used by a playback client.
        The result should only use notes/rhythms as allowed by the user's request, i.e. using
        chord tones and notes in the allowed scales provided for each chord in the progression.

        ## Output
        The output CandidateTrack should consist of:

        1. Drum track - a list of DrumNoteEvents describing what the drum track should play,
           complimenting what the pitched instruments are playing.
           MidiNote values must follow the General MIDI percussion mapping, e.g. 36 = Acoustic
           Bass Drum, 38 = Acoustic Snare, 42 = Closed Hi-Hat, 46 = Open Hi-Hat, 49 = Crash
           Cymbal 1, 51 = Ride Cymbal 1. Only use notes from this mapping.

        2. Bass track - a list of BassNoteEvents describing what the bass line of the track
           should sound like, using low frequencies, and making the tonal center of each chord
           clear.

        3. Chord track - a list of ChordVoicingEvents, to be played by a chord-friendly
           instrument, e.g. piano. Should use mid-range frequencies and compliment the drum and
           bass tracks. Duplicate notes in a single ChordNoteEvent are not allowed.

        4. LengthInBeats - set to 0. The system computes and overwrites this anyway.

        Each event should be a MIDI note with MIDI velocity 1-127 as is standard.
        """;
}