import Foundation

/// The musical styles the generation pipeline's RAG step has reference material for
/// (`api/.../KnowledgeBase/Content/styles.jsonl`). The API's `Style` field is free-form text, but
/// picking from this list keeps the request aligned with what the knowledge base can actually
/// ground the generation in. Free-form nuance goes in a separate notes field on the form.
enum MusicStyle: String, CaseIterable, Identifiable, Sendable {
    case blues
    case rock
    case pop
    case funk
    case reggae
    case jazz

    var id: String { rawValue }

    /// Capitalised name for the picker.
    var displayName: String { rawValue.capitalized }

    /// Value used to seed the API request's `Style` field (before any notes are appended).
    var apiValue: String { rawValue }
}

/// Inverse of `composedStyle`: `"Blues - slow, brushes"` → `(.blues, "slow, brushes")`,
/// `"Rock"` → `(.rock, "")`. Splits on the *first* `" - "` only, so notes may themselves
/// contain that separator. A left token that isn't a known style yields `(nil, "")` — the
/// caller then keeps the whole stored string as notes.
func decomposeStyle(_ stored: String) -> (MusicStyle?, String) {
    let token: String
    let notes: String
    if let separator = stored.range(of: " - ") {
        token = String(stored[..<separator.lowerBound])
        notes = String(stored[separator.upperBound...])
    } else {
        token = stored
        notes = ""
    }
    guard let style = MusicStyle(rawValue: token.lowercased()) else { return (nil, "") }
    return (style, notes)
}
