namespace BackingTrack.Generation;

public static class Constants
{
    public const int MidiVelocityMin = 1;
    public const int MidiVelocityMax = 127;

    public const int GeneralMidiDrumNoteMin = 35;
    public const int GeneralMidiDrumNoteMax = 81;

    public const int BassMidiNoteMin = 28; // E1
    public const int BassMidiNoteMax = 57; // A3

    public const int ChordVoicingNoteMin = 48; // C3
    public const int ChordVoicingNoteMax = 79; // G5

    public static readonly IReadOnlyList<double> LlmCallDurationBuckets = [1, 2.5, 5, 10, 15, 20, 30, 45, 60, 90, 120, 180, 300, 600, 900];
    public static readonly IReadOnlyList<double> PushNotificationDurationBuckets = [0.05, 0.1, 0.25, 0.5, 0.75, 1, 1.5, 2, 3, 5, 10];
}