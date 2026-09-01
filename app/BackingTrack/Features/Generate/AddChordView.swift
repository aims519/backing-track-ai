import SwiftUI

/// Sheet for composing one chord from a root note and a quality, then adding it to the
/// progression. Two wheel pickers side by side let the chord be "dialled in", with a live
/// preview of the resulting symbol above them.
struct AddChordView: View {
    // `@Environment(\.dismiss)` closes the sheet from within — no `Binding<Bool>` back to the
    // presenter needed (same pattern as `SettingsView`).
    @Environment(\.dismiss) private var dismiss

    /// Invoked with the chosen root + quality when the user taps Add. The parent owns the chord
    /// list, so this view just reports the selection and closes.
    let onAdd: (NoteName, ChordQuality) -> Void

    @State private var root: NoteName = .c
    @State private var quality: ChordQuality = .major

    private var preview: Chord { Chord(root: root, quality: quality) }

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                Text(preview.displaySymbol)
                    .font(.system(size: 52, weight: .semibold, design: .rounded))
                    .frame(maxWidth: .infinity)
                    .padding(.top, 12)

                Text("\(root.displayName) \(quality.pickerName)")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .padding(.bottom, 8)

                HStack(spacing: 0) {
                    Text("Root").frame(maxWidth: .infinity)
                    Text("Quality").frame(maxWidth: .infinity)
                }
                .font(.caption)
                .foregroundStyle(.secondary)

                HStack(spacing: 0) {
                    // `.labelsHidden` (applied to the HStack below) suppresses the accessibility
                    // labels' on-screen rendering — a wheel picker doesn't show them anyway, and
                    // the caption row above already names the columns.
                    Picker("Root", selection: $root) {
                        ForEach(NoteName.allCases) { note in
                            Text(note.displayName).tag(note)
                        }
                    }
                    .pickerStyle(.wheel)

                    Picker("Quality", selection: $quality) {
                        ForEach(ChordQuality.allCases) { quality in
                            Text(quality.pickerName).tag(quality)
                        }
                    }
                    .pickerStyle(.wheel)
                }
                .labelsHidden()

                Spacer(minLength: 0)
            }
            .navigationTitle("Add Chord")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Add") {
                        onAdd(root, quality)
                        dismiss()
                    }
                }
            }
        }
    }
}

#Preview {
    // The trailing closure is the `onAdd` handler — a no-op here since the preview has no list.
    AddChordView { _, _ in }
}
