using BackingTrack.Core.Config;

using Microsoft.Extensions.Options;

namespace BackingTrack.Generation;

// Source-generated (no reflection, so Native AOT-safe) validators for the DataAnnotations on the
// settings classes in Core. Core stays free of a Microsoft.Extensions.Options reference, so the
// generated IValidateOptions<T> implementations live here, next to the registrations that use them.
[OptionsValidator]
internal sealed partial class OpenAiSettingsValidator : IValidateOptions<OpenAiSettings>;

[OptionsValidator]
internal sealed partial class CriticSettingsValidator : IValidateOptions<CriticSettings>;

[OptionsValidator]
internal sealed partial class TrackStorageSettingsValidator : IValidateOptions<TrackStorageSettings>;