using System.Diagnostics;
using System.Text.Json;

using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using OpenAI.Realtime;

namespace BackingTrack.Generation.Steps.TrackRepair;

public class RepairStep([FromKeyedServices("repair")] AIAgent aiAgent, TrackRepairProviderInfo providerInfo,
    TrackRepairMetrics metrics, ILogger<RepairStep> logger) : IRepairStep
{
    public async Task<CandidateTrack> RepairCandidateTrackAsync(CandidateTrack previousAttempt, IReadOnlyList<ValidationFailure> failures,
        CancellationToken cancellationToken = default)
    {
        using Activity? activity = TrackGenerationActivitySource.Instance.StartActivity($"{nameof(RepairStep)}.RepairCandidateTrackAsync");
        activity?.SetTag("track_repair.provider", providerInfo.Provider);
        activity?.SetTag("track_repair.model", providerInfo.Model);
        
        string prompt = GenerateRequestPrompt(previousAttempt, failures);
        
        ChatClientAgentRunOptions runOptions = new(new ChatOptions
        {
            MaxOutputTokens = providerInfo.MaxOutputTokens,
        });
        
        Stopwatch stopwatch = Stopwatch.StartNew();
        AgentResponse<TrackRepairPatch> agentResponse;
        try
        {
            agentResponse = await aiAgent.RunAsync<TrackRepairPatch>(prompt,
                serializerOptions: new JsonSerializerOptions { TypeInfoResolver = TrackRepairJsonContext.Default },
                options: runOptions,
                cancellationToken: cancellationToken);
        }
        catch(Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            metrics.RecordFailure(providerInfo.Provider, providerInfo.Model, stopwatch.Elapsed);
            throw;
        }
        stopwatch.Stop();

        logger.LogInformation("Generated Candidate Track Repair Patch. Input tokens: {InputCount}. Output tokens: {OutputCount}",
            agentResponse.Usage?.InputTokenCount, agentResponse.Usage?.OutputTokenCount);
        logger.LogDebug("Repair patch: {Patch}", JsonSerializer.Serialize(agentResponse.Result, new JsonSerializerOptions { TypeInfoResolver = TrackRepairJsonContext.Default }));

        activity?.SetTag("track_repair.input_tokens", agentResponse.Usage?.InputTokenCount);
        activity?.SetTag("track_repair.output_tokens", agentResponse.Usage?.OutputTokenCount);
        metrics.RecordSuccess(providerInfo.Provider, providerInfo.Model, stopwatch.Elapsed, agentResponse.Usage?.InputTokenCount, agentResponse.Usage?.OutputTokenCount);

        if (IsEmpty(agentResponse.Result))
        {
            logger.LogWarning("Job repair returned an empty patch with no corrections; returning previous attempt unchanged");
            return previousAttempt;
        }

        CandidateTrack updatedTrack = ApplyPatch(previousAttempt, agentResponse.Result);

        return updatedTrack;
    }
    
    private static bool IsEmpty(TrackRepairPatch? patch) =>
        patch is null ||
        ((patch.NoteCorrections is null || patch.NoteCorrections.Count == 0) &&
        patch.NewDrumPart is null &&
        patch.NewBassPart is null &&
        patch.NewChordVoicingPart is null &&
        patch.NewLengthInBeats is null);

    private string GenerateRequestPrompt(CandidateTrack previousAttempt, IReadOnlyList<ValidationFailure> failures)
    {
        return $"""
               Here is the previous attempt at generating a candidate track:
               {JsonSerializer.Serialize(previousAttempt,  new JsonSerializerOptions { TypeInfoResolver = CandidateTrackJsonContext.Default })}
               
               Here are the failure messages that require patching:
               {string.Join('\n', JsonSerializer.Serialize(failures,  new JsonSerializerOptions { TypeInfoResolver = TrackRepairJsonContext.Default }))}
               """;
    }

    internal CandidateTrack ApplyPatch(CandidateTrack originalTrack, TrackRepairPatch trackPatch)
    {
        // Establish each part's base list: a supplied whole-part replacement, or the original part
        IList<DrumNoteEvent> drumNoteEvents = (trackPatch.NewDrumPart ?? originalTrack.Drums).ToList();
        IList<BassNoteEvent> bassNoteEvents = (trackPatch.NewBassPart ?? originalTrack.Bass).ToList();
        IList<ChordVoicingEvent> chordVoicingEvents = (trackPatch.NewChordVoicingPart ?? originalTrack.ChordVoicings).ToList();

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
                            logger.LogWarning("Attempted to apply a patch for a {Type} that doesn't exist. BeatPosition: {Position}",
                                nameof(DrumNoteEvent), drumOriginal.BeatPosition);
                            continue;
                        }

                        DrumNoteEvent merged = group.Aggregate(drumOriginal, (acc, correction) => acc with
                        {
                            BeatPosition = correction.NewBeatPosition ?? acc.BeatPosition,
                            DurationInBeats = correction.NewDurationInBeats ?? acc.DurationInBeats,
                            MidiNote = correction.NewMidiNotes?.FirstOrDefault() ?? acc.MidiNote,
                            Velocity = correction.NewVelocity ?? acc.Velocity
                        });

                        drumNoteEvents.Add(merged);
                        continue;
                    }
                case BassNoteEvent bassOriginal:
                    {
                        if (!bassNoteEvents.Remove(bassOriginal))
                        {
                            logger.LogWarning("Attempted to apply a patch for a {Type} that doesn't exist. BeatPosition: {Position}",
                                nameof(BassNoteEvent), bassOriginal.BeatPosition);
                            continue;
                        }

                        BassNoteEvent merged = group.Aggregate(bassOriginal, (acc, correction) => acc with
                        {
                            BeatPosition = correction.NewBeatPosition ?? acc.BeatPosition,
                            DurationInBeats = correction.NewDurationInBeats ?? acc.DurationInBeats,
                            MidiNote = correction.NewMidiNotes?.FirstOrDefault() ?? acc.MidiNote,
                            Velocity = correction.NewVelocity ?? acc.Velocity
                        });

                        bassNoteEvents.Add(merged);
                        continue;
                    }
                case ChordVoicingEvent chordVoicingOriginal:
                    {
                        if (!chordVoicingEvents.Remove(chordVoicingOriginal))
                        {
                            logger.LogWarning("Attempted to apply a patch for a {Type} that doesn't exist. BeatPosition: {Position}",
                                nameof(ChordVoicingEvent), chordVoicingOriginal.BeatPosition);
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
            }
        }
        
        drumNoteEvents = drumNoteEvents.OrderBy(e => e.BeatPosition).ToList();
        bassNoteEvents = bassNoteEvents.OrderBy(e => e.BeatPosition).ToList();
        chordVoicingEvents = chordVoicingEvents.OrderBy(e => e.BeatPosition).ToList();
        
        CandidateTrack updatedTrack = new(trackPatch.NewLengthInBeats ?? originalTrack.LengthInBeats, (IReadOnlyList<DrumNoteEvent>)drumNoteEvents, 
            (IReadOnlyList<BassNoteEvent>)bassNoteEvents,
            (IReadOnlyList<ChordVoicingEvent>)chordVoicingEvents);

        return updatedTrack;
    }
}