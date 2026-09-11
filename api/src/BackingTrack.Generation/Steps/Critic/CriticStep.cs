using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Steps.Critic;

public class CriticStep(ICriticAiClient criticAiClient, IOptions<CriticSettings> criticSettings) : ICriticStep
{
    public async Task<CriticFinalReview> ReviewCandidateTrackAsync(CandidateTrack candidateTrack, GenerationJob generationJob, MatchedStyle matchedStyle,
        IReadOnlyList<MatchedChord> matchedChords, IReadOnlyList<MatchedScale> matchedScales, CancellationToken cancellationToken = default)
    {
        CriticVerdictSubmission verdictSubmission = await criticAiClient.GenerateCriticVerdictSubmissionAsync(candidateTrack, generationJob, matchedStyle, matchedChords, matchedScales, cancellationToken);

        CriticFinalReview finalReview = ScoreVerdictSubmission(verdictSubmission);
        return finalReview;
    }
    
    private CriticFinalReview ScoreVerdictSubmission(CriticVerdictSubmission verdictSubmission)
    {       
        // Check all dimensions have been assessed
        List<CriticCitation> citations = [];
        foreach (CriticDimension criticDimension in Enum.GetValues<CriticDimension>()
                     .Where(d => !verdictSubmission.DimensionAssessments.Select(da => da.Dimension).Contains(d)))
        {
            citations.Add(new CriticCitation(criticDimension,
                $"Critic did not asssess the {criticDimension} dimension",
                string.Empty));
        }
        
        // Compile list of citations
        foreach (IGrouping<CriticDimension, CriticDimensionAssessment> dimensionAssessmentGroup in verdictSubmission.DimensionAssessments
                     .Where(a => !a.Pass)
                     .GroupBy(a => a.Dimension))
        {
            foreach (CriticDimensionAssessment dimensionAssessment in dimensionAssessmentGroup)
            {
                string violation = dimensionAssessment.Violation
                                   ?? $"Critic marked {dimensionAssessment.Dimension} as failed but did not provide a violation description";
                string citedItemId = dimensionAssessment.CitedKnowledgeBaseItemId ?? string.Empty;
                
                CriticCitation citation = new(dimensionAssessment.Dimension,
                    violation,
                    citedItemId);
                citations.Add(citation);
            }
        }
        
        // Compute Score
        double sumOfPassingWeights = verdictSubmission.DimensionAssessments
            .GroupBy(a => a.Dimension)
            .Where(group => group.All(a => a.Pass))
            .Select(group => group.Key)
            .Sum(GetDimensionScoringWeight);

        double score = sumOfPassingWeights * (100d / TotalScoringWeights);
        
        bool isPass = score >= criticSettings.Value.PassingScoreThreshold;
        return new(isPass, score, citations);
    }

    private double TotalScoringWeights => Enum.GetValues<CriticDimension>().Sum(GetDimensionScoringWeight);
    
    private double GetDimensionScoringWeight(CriticDimension dimension)
    {
        return dimension switch
        {
            CriticDimension.ChordToneAdherence => 2d,
            CriticDimension.RhythmAndFeel => 1d,
            CriticDimension.VoiceLeading => 1.5d,
            _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, $"No scoring weight defined for {dimension}")
        };
    }
}