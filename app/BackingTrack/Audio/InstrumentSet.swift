/// The bundled SoundFont presets each voice plays for a style. Mapped on the client from the
/// picked `MusicStyle` rather than chosen by the API, so the track contract stays instrument-free.
///
/// Numbers are 0-indexed General MIDI programs, checked against the bundled GeneralUser GS.
/// `drumProgram` selects a kit in the percussion bank (loaded with
/// `kAUSampler_DefaultPercussionBankMSB`), not a melodic program.
struct InstrumentSet: Equatable {
    var bassProgram: UInt8
    var drumProgram: UInt8
    var chordProgram: UInt8

    /// A `nil` style — a history row whose stored style isn't one of the presets — gets the
    /// original, pre-per-style sound. No `default` in the switch, so adding a `MusicStyle` case
    /// fails to compile until it's given instruments here.
    ///
    /// Chord instruments are all keyboards: the generator writes block piano voicings, which
    /// sound wrong on a guitar preset with every note of the chord struck at once. The acoustic
    /// grands (programs 0 and 1) were tried and didn't sit well in the mix.
    static func forStyle(_ style: MusicStyle?) -> InstrumentSet {
        // Finger Bass, Standard kit, Electric Grand Piano
        guard let style else { return InstrumentSet(bassProgram: 33, drumProgram: 0, chordProgram: 2) }

        switch style {
        case .blues:
            // Finger Bass, Room kit, Tine Electric Piano
            return InstrumentSet(bassProgram: 33, drumProgram: 8, chordProgram: 4)
        case .rock:
            // Pick Bass, Room kit, Electric Grand Piano. The Power kit and Rock Organ were tried
            // and judged too aggressive.
            return InstrumentSet(bassProgram: 34, drumProgram: 8, chordProgram: 2)
        case .pop:
            // Finger Bass, Standard kit, Electric Grand Piano
            return InstrumentSet(bassProgram: 33, drumProgram: 0, chordProgram: 2)
        case .funk:
            // Slap Bass 1, Room kit, Clavinet
            return InstrumentSet(bassProgram: 36, drumProgram: 8, chordProgram: 7)
        case .reggae:
            // Finger Bass, Standard kit, Tonewheel Organ
            return InstrumentSet(bassProgram: 33, drumProgram: 0, chordProgram: 16)
        case .jazz:
            // Acoustic Bass, Brush kit, Electric Grand Piano
            return InstrumentSet(bassProgram: 32, drumProgram: 40, chordProgram: 2)
        }
    }
}
