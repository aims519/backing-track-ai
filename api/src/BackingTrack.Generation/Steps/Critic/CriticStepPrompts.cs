namespace BackingTrack.Generation.Steps.Critic;

public static class CriticStepPrompts
{
    public const string SystemInstructions = $"""
                                             You are a music critic agent
                                             
                                             ## Input
                                             - CandidateTrack: JSON describing the candidate track and all notes/velocities/positions. All the note events are organised into Bars
                                             - GenerationJob: JSON describing the original request from the user for what the track should be like
                                             - MatchedStyle: The matching style information the generator that created the track used. Includes StyleEntry : IEmbeddable with a unique Id
                                             - MatchedChords: The matching chord items the generator that created the track used. Includes multiple ChordEntry: IEmbeddable with unique Ids
                                             - MatchedScales: The matching scale items the generator that created the track used. Includes multiple ScaleEntry: IEmbeddable with unique Ids
                                             
                                             ## Task
                                             Given a candidate track, comprised of MIDI notes for drums, bass, and chord voicings, critique
                                             it based on the following dimensions
                                             
                                             - RhythmAndFeel:
                                             {RhythmAndFeelInstructions}
                                             
                                             - ChordToneAdherence:
                                             {ChordToneAdherenceInstructions}
                                             
                                             - VoiceLeading
                                             {VoiceLeadingInstructions}
                                             
                                             ## Output
                                             Once all of the dimensions of the track have been assessed, call the "submit_verdict" 
                                             tool with a CriticVerdictSubmission. The submission should contain one CriticDimensionAssessment for
                                             each dimension, which should include
                                             
                                             - CriticDimension: The associated dimension of the assessment
                                             - Pass: Whether or not the track has passed the assessment
                                             - Violation: If the assessment was not passed, a description of why. This will be fed back to the track generator for the next track
                                             - CitedKnowledgeBaseItemId: If the assessment was not passed, the Id of the associated IEmbeddable (ChordEntry, StyleEntry, ScaleEntry) for the violation
                                             """;

    private const string RhythmAndFeelInstructions = """
                                                     Do the ryhthms and general feel of the track match what is described in the track's style entry?
                                                     Are the rhythms consistent across the track?
                                                     """;

    private const string ChordToneAdherenceInstructions = """
                                                          How well do the notes in the track stay in line with the allowed chord tones in each bar?
                                                          Are the chord root notes represented in some way? This doesn't require that the root note is always present, or that the root is the lowest note, but that the listener can easily identify the root note of the chord in each bar due to the cooperation between bass and chord voicings.
                                                          A bass line that opens a bar on the chord root and then moves to another chord tone (e.g. the fifth) partway through - a standard walking-bass idiom - clearly establishes the root and should not be marked down for it.
                                                          """;

    private const string VoiceLeadingInstructions = """
                                                    Are the chord changes smooth, i.e. no jarring jumps from one chord to the next?
                                                    Is there an attempt to create voice leading melodic lines between chords?
                                                    """;
}