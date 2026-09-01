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
