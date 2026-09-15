using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Steps.Critic;

/// <summary>
/// A wrapper around MatchedScale for the critic's benefit, so it doesn't have to transpose the
/// scale's raw interval numbers onto the chord's root itself - a computation it gets wrong in
/// practice (see the good-modal-color-tones-from-matched-scale critic eval fixture: it rejected
/// notes that were, in fact, inside the matched scale, and once misread which note it was even
/// judging).
/// </summary>
public sealed class CriticMatchedScale(MatchedScale matchedScale, ScaleMatcher scaleMatcher)
{
    public string ScaleId => matchedScale.ScaleEntry.Id;
    public string ChordRoot => matchedScale.Chord.Root;
    public string ChordId => matchedScale.Chord.ChordEntry.Id;

    /// <summary>The actual note names this scale allows, spelled out so no interval arithmetic is required.</summary>
    public IReadOnlyList<string> AllowedToneNames => scaleMatcher.GetChordToneNames(matchedScale.Chord.Root, matchedScale.ScaleEntry.Intervals);
}