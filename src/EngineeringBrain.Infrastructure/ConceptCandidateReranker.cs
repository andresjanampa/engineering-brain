using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class ConceptCandidateReranker
{
    public const int PointsPerMatchedToken = 2;
    public const int MaximumCandidateContribution = 6;

    private readonly InitiativeTermNormalizer _normalizer;

    public ConceptCandidateReranker(InitiativeTermNormalizer? normalizer = null)
    {
        _normalizer = normalizer ?? new InitiativeTermNormalizer();
    }

    public IReadOnlyList<ComponentCandidate> Rerank(
        IReadOnlyList<ComponentCandidate> lexicalCandidates,
        IReadOnlyList<string> normalizedQueryTerms,
        IReadOnlyList<ComponentConceptProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(lexicalCandidates);
        ArgumentNullException.ThrowIfNull(normalizedQueryTerms);
        ArgumentNullException.ThrowIfNull(profiles);

        if (profiles.Count == 0)
        {
            return lexicalCandidates;
        }

        var terms = normalizedQueryTerms.ToHashSet(StringComparer.Ordinal);
        var profilesByEntity = profiles
            .GroupBy(item => item.EntityId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(item => item.Concepts)
                    .GroupBy(item => item.ConceptId, StringComparer.Ordinal)
                    .Select(concepts => concepts
                        .OrderBy(item => item.DeclarationFingerprint, StringComparer.Ordinal)
                        .First())
                    .OrderBy(item => item.ConceptId, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var scored = lexicalCandidates.Select((candidate, lexicalRank) =>
        {
            if (!profilesByEntity.TryGetValue(candidate.EntityId, out var concepts))
            {
                return new ScoredCandidate(candidate, candidate.Score, ExactTier(candidate), lexicalRank);
            }

            var remaining = MaximumCandidateContribution;
            var reasons = candidate.MatchReasons.ToList();
            foreach (var concept in concepts)
            {
                if (remaining == 0 || !Qualifies(concept, terms, out var matchedTokens))
                {
                    continue;
                }

                var points = Math.Min(remaining, matchedTokens.Count * PointsPerMatchedToken);
                if (points == 0)
                {
                    continue;
                }

                var fingerprintPrefix = concept.DeclarationFingerprint[..Math.Min(
                    12,
                    concept.DeclarationFingerprint.Length)];
                reasons.Add(new MatchReason(
                    "reviewed concept",
                    $"{concept.ConceptId}@{fingerprintPrefix}",
                    points));
                remaining -= points;
            }

            var contribution = MaximumCandidateContribution - remaining;
            var reranked = candidate with
            {
                Score = candidate.Score + contribution,
                MatchReasons = OrderReasons(reasons)
            };
            return new ScoredCandidate(reranked, candidate.Score, ExactTier(candidate), lexicalRank);
        }).ToList();

        scored.Sort(CompareFinal);
        RestoreExactIdentityPrecedence(scored);
        return scored.Select(item => item.Candidate).ToArray();
    }

    private bool Qualifies(
        ResolvedReviewedConcept concept,
        IReadOnlySet<string> queryTerms,
        out IReadOnlyList<string> matchedTokens)
    {
        var conceptTokens = _normalizer.Tokenize(concept.ConceptId);
        matchedTokens = conceptTokens.Where(queryTerms.Contains).ToArray();
        var phraseQualified = conceptTokens.Count switch
        {
            0 => false,
            1 => matchedTokens.Count == 1,
            2 => matchedTokens.Count == 2,
            3 => matchedTokens.Count >= 2,
            _ => matchedTokens.Count >= 3
        };
        if (!phraseQualified)
        {
            return false;
        }

        if (concept.AnchorPolicy != ReviewedConceptAnchorPolicy.Clear)
        {
            return true;
        }

        var anchorMatched = concept.AnchorTokens.Any(group =>
            group.Count > 0 && group.All(queryTerms.Contains));
        var qualificationSupportMatched = concept.QualificationSupportTokens.Any(queryTerms.Contains);
        return anchorMatched && qualificationSupportMatched;
    }

    private static void RestoreExactIdentityPrecedence(List<ScoredCandidate> candidates)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var weakerIndex = 0; weakerIndex < candidates.Count; weakerIndex++)
            {
                for (var strongerIndex = weakerIndex + 1; strongerIndex < candidates.Count; strongerIndex++)
                {
                    var weaker = candidates[weakerIndex];
                    var stronger = candidates[strongerIndex];
                    var exactPrecedenceApplies = stronger.ExactTier > weaker.ExactTier
                        || (stronger.ExactTier > 0 && stronger.ExactTier == weaker.ExactTier);
                    if (!exactPrecedenceApplies || stronger.LexicalRank >= weaker.LexicalRank)
                    {
                        continue;
                    }

                    candidates.RemoveAt(strongerIndex);
                    candidates.Insert(weakerIndex, stronger);
                    changed = true;
                    break;
                }

                if (changed)
                {
                    break;
                }
            }
        }
    }

    private static int CompareFinal(ScoredCandidate left, ScoredCandidate right)
    {
        var score = right.Candidate.Score.CompareTo(left.Candidate.Score);
        if (score != 0)
        {
            return score;
        }

        var fullName = StringComparer.Ordinal.Compare(left.Candidate.FullName, right.Candidate.FullName);
        return fullName != 0
            ? fullName
            : StringComparer.Ordinal.Compare(left.Candidate.EntityId, right.Candidate.EntityId);
    }

    private static int ExactTier(ComponentCandidate candidate)
    {
        if (candidate.MatchReasons.Any(reason => reason.Signal == "exact full name"))
        {
            return 2;
        }

        return candidate.MatchReasons.Any(reason => reason.Signal == "exact component name") ? 1 : 0;
    }

    private static IReadOnlyList<MatchReason> OrderReasons(IEnumerable<MatchReason> reasons) => reasons
        .OrderByDescending(reason => reason.Points)
        .ThenBy(reason => reason.Signal, StringComparer.Ordinal)
        .ThenBy(reason => reason.MatchedValue, StringComparer.Ordinal)
        .ToArray();

    private sealed record ScoredCandidate(
        ComponentCandidate Candidate,
        int LexicalScore,
        int ExactTier,
        int LexicalRank);
}
