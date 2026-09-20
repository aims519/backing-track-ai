using System.Diagnostics;
using System.Text.Json;

using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Steps.TrackRepair;

public class RepairStep([FromKeyedServices("repair")] IChatClient chatClient, TrackRepairProviderInfo providerInfo,
    TrackRepairMetrics metrics, ILogger<RepairStep> logger) : IRepairStep
{
    // Hand-authored rather than generated from TrackRepairPatch: System.Text.Json's schema exporter
    // (via AIAgent.RunAsync<T>, which always builds its own schema and offers no transform hook)
    // produces a schema the OpenAI Responses API's strict validator rejects — polymorphic "$type"
    // discriminators without an explicit "type" keyword, and "$ref"s that don't point at a top-level
    // "$defs" entry. This schema is written directly against what the Responses/Chat Completions
    // strict-schema subset actually accepts. Keep it in sync by hand if TrackRepairPatch's shape changes.
    private static readonly JsonElement RepairPatchSchema = LoadSchema();

    private static JsonElement LoadSchema()
    {
        // JsonDocument.Parse, not JsonSerializer.Deserialize<JsonElement>: the latter is a
        // reflection-based generic call that trips IL2026/IL3050 under Native AOT (see ADR 0016).
        string path = Path.Combine(AppContext.BaseDirectory, "Steps", "TrackRepair", "track-repair-patch.schema.json");
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    public async Task<CandidateTrack> RepairCandidateTrackAsync(CandidateTrack previousAttempt, IReadOnlyList<ValidationFailure> failures,
        CancellationToken cancellationToken = default)
    {
        using Activity? activity = TrackGenerationActivitySource.Instance.StartActivity($"{nameof(RepairStep)}.RepairCandidateTrackAsync");
        activity?.SetTag("track_repair.provider", providerInfo.Provider);
        activity?.SetTag("track_repair.model", providerInfo.Model);

        string prompt = GenerateRequestPrompt(previousAttempt, failures);
        ChatMessage[] messages =
        [
            new(ChatRole.System, RepairStepPrompts.SystemInstructions),
            new(ChatRole.User, prompt)
        ];

        ChatOptions chatOptions = new()
        {
            MaxOutputTokens = providerInfo.MaxOutputTokens,
            ResponseFormat = new ChatResponseFormatJson(RepairPatchSchema, "TrackRepairPatch", null),
        };

        Stopwatch stopwatch = Stopwatch.StartNew();
        ChatResponse chatResponse;
        try
        {
            chatResponse = await chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            metrics.RecordFailure(providerInfo.Provider, providerInfo.Model, stopwatch.Elapsed);
            throw;
        }
        stopwatch.Stop();

        TrackRepairPatch? patch = JsonSerializer.Deserialize(chatResponse.Text, TrackRepairJsonContext.Default.TrackRepairPatch);

        logger.LogInformation("Generated Candidate Track Repair Patch. Input tokens: {InputCount}. Output tokens: {OutputCount}",
            chatResponse.Usage?.InputTokenCount, chatResponse.Usage?.OutputTokenCount);
        logger.LogDebug("Repair patch: {Patch}", chatResponse.Text);

        activity?.SetTag("track_repair.input_tokens", chatResponse.Usage?.InputTokenCount);
        activity?.SetTag("track_repair.output_tokens", chatResponse.Usage?.OutputTokenCount);
        metrics.RecordSuccess(providerInfo.Provider, providerInfo.Model, stopwatch.Elapsed, chatResponse.Usage?.InputTokenCount, chatResponse.Usage?.OutputTokenCount);

        if (IsEmpty(patch))
        {
            logger.LogWarning("Job repair returned an empty patch with no corrections; returning previous attempt unchanged");
            return previousAttempt;
        }

        CandidateTrack updatedTrack = ApplyPatch(previousAttempt, patch);

        return updatedTrack;
    }

    private static bool IsEmpty([System.Diagnostics.CodeAnalysis.NotNullWhen(false)] TrackRepairPatch? patch) =>
        patch is null ||
        ((patch.NoteCorrections is null || patch.NoteCorrections.Count == 0) &&
        patch.NewDrumPart is null &&
        patch.NewBassPart is null &&
        patch.NewChordVoicingPart is null);

    private void LogUnmatchedCorrection(string noteType, double beatPosition, bool partReplaced)
    {
        if (partReplaced)
            logger.LogDebug("Dropping a {Type} correction at BeatPosition {Position}: its part was wholesale-replaced so the original note is gone",
                noteType, beatPosition);
        else
            logger.LogWarning("Attempted to apply a patch for a {Type} that doesn't exist. BeatPosition: {Position}",
                noteType, beatPosition);
    }

    private string GenerateRequestPrompt(CandidateTrack previousAttempt, IReadOnlyList<ValidationFailure> failures)
    {
        return $"""
               Here is the previous attempt at generating a candidate track:
               {JsonSerializer.Serialize(previousAttempt, CandidateTrackJsonContext.Default.CandidateTrack)}

               Here are the failure messages that require patching:
               {JsonSerializer.Serialize(failures, TrackRepairJsonContext.Default.IReadOnlyListValidationFailure)}
               """;
    }

    internal CandidateTrack ApplyPatch(CandidateTrack originalTrack, TrackRepairPatch trackPatch)
    {
        // Establish each part's base list: a supplied whole-part replacement, or the original part
        IList<DrumNoteEvent> drumNoteEvents = (trackPatch.NewDrumPart ?? originalTrack.Drums).ToList();
        IList<BassNoteEvent> bassNoteEvents = (trackPatch.NewBassPart ?? originalTrack.Bass).ToList();
        IList<ChordVoicingEvent> chordVoicingEvents = (trackPatch.NewChordVoicingPart ?? originalTrack.ChordVoicings).ToList();

        // Note corrections apply on top of that base. A correction whose Original is no longer
        // present is dropped: expected (logged at debug) when its part was wholesale-replaced -
        // a SilentBar repair often replaces a part and still emits corrections for the old notes
        // despite the prompt - and a genuine miss (logged at warning) otherwise.
        bool drumPartReplaced = trackPatch.NewDrumPart is not null;
        bool bassPartReplaced = trackPatch.NewBassPart is not null;
        bool chordPartReplaced = trackPatch.NewChordVoicingPart is not null;

        // Apply note corrections on top of that base
        IEnumerable<IGrouping<NoteEvent, NoteCorrection>> correctionsByOriginal = (trackPatch.NoteCorrections ?? []).GroupBy(c => c.Original);
        foreach (IGrouping<NoteEvent, NoteCorrection> group in correctionsByOriginal)
        {
            switch (group.Key)
            {
                case DrumNoteEvent drumOriginal:
                    {
                        if (!drumNoteEvents.Remove(drumOriginal))
                        {
                            LogUnmatchedCorrection(nameof(DrumNoteEvent), drumOriginal.BeatPosition, drumPartReplaced);
                            continue;
                        }

                        DrumNoteEvent merged = group.Aggregate(drumOriginal, (acc, correction) => acc with
                        {
                            BeatPosition = correction.NewBeatPosition ?? acc.BeatPosition,
                            DurationInBeats = correction.NewDurationInBeats ?? acc.DurationInBeats,
                            MidiNote = correction.NewMidiNotes is null ? acc.MidiNote : (correction.NewMidiNotes.Length == 0 ? acc.MidiNote : correction.NewMidiNotes.First()),
                            Velocity = correction.NewVelocity ?? acc.Velocity
                        });

                        drumNoteEvents.Add(merged);
                        continue;
                    }
                case BassNoteEvent bassOriginal:
                    {
                        if (!bassNoteEvents.Remove(bassOriginal))
                        {
                            LogUnmatchedCorrection(nameof(BassNoteEvent), bassOriginal.BeatPosition, bassPartReplaced);
                            continue;
                        }

                        BassNoteEvent merged = group.Aggregate(bassOriginal, (acc, correction) => acc with
                        {
                            BeatPosition = correction.NewBeatPosition ?? acc.BeatPosition,
                            DurationInBeats = correction.NewDurationInBeats ?? acc.DurationInBeats,
                            MidiNote = correction.NewMidiNotes is null ? acc.MidiNote : (correction.NewMidiNotes.Length == 0 ? acc.MidiNote : correction.NewMidiNotes.First()),
                            Velocity = correction.NewVelocity ?? acc.Velocity
                        });

                        bassNoteEvents.Add(merged);
                        continue;
                    }
                case ChordVoicingEvent chordVoicingOriginal:
                    {
                        if (!chordVoicingEvents.Remove(chordVoicingOriginal))
                        {
                            LogUnmatchedCorrection(nameof(ChordVoicingEvent), chordVoicingOriginal.BeatPosition, chordPartReplaced);
                            continue;
                        }

                        ChordVoicingEvent merged = group.Aggregate(chordVoicingOriginal, (acc, correction) => acc with
                        {
                            BeatPosition = correction.NewBeatPosition ?? acc.BeatPosition,
                            DurationInBeats = correction.NewDurationInBeats ?? acc.DurationInBeats,
                            MidiNotes = correction.NewMidiNotes ?? acc.MidiNotes,
                            Velocity = correction.NewVelocity ?? acc.Velocity
                        });

                        chordVoicingEvents.Add(merged);
                        continue;
                    }
                default:
                    throw new ArgumentException($"Unknown original note type passed in a {nameof(NoteCorrection)}");
            }
        }

        drumNoteEvents = drumNoteEvents.OrderBy(e => e.BeatPosition).ToList();
        bassNoteEvents = bassNoteEvents.OrderBy(e => e.BeatPosition).ToList();
        chordVoicingEvents = chordVoicingEvents.OrderBy(e => e.BeatPosition).ToList();

        CandidateTrack updatedTrack = new(originalTrack.LengthInBeats, (IReadOnlyList<DrumNoteEvent>)drumNoteEvents,
            (IReadOnlyList<BassNoteEvent>)bassNoteEvents,
            (IReadOnlyList<ChordVoicingEvent>)chordVoicingEvents);

        return updatedTrack;
    }
}