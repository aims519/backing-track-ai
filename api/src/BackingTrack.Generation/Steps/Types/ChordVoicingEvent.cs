namespace BackingTrack.Generation.Steps.Types;

public record ChordVoicingEvent(double BeatPosition, double DurationInBeats, IReadOnlyList<int> MidiNotes, int Velocity)
    : NoteEvent(BeatPosition, DurationInBeats, Velocity)
{
    public virtual bool Equals(ChordVoicingEvent? other) =>
        other is not null &&
        BeatPosition.Equals(other.BeatPosition) &&
        DurationInBeats.Equals(other.DurationInBeats) &&
        Velocity == other.Velocity &&
        MidiNotes.SequenceEqual(other.MidiNotes);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(BeatPosition);
        hash.Add(DurationInBeats);
        hash.Add(Velocity);
        foreach (int note in MidiNotes)
            hash.Add(note);
        return hash.ToHashCode();
    }
}