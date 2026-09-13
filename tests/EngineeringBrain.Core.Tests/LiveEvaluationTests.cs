using System.Text.Json;
using System.Text.Json.Nodes;
using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class LiveEvaluationTests
{
    [Fact]
    public async Task LiveStore_NewArtifactWritesSchemaThreeWithExactAssessments()
    {
        using var fixture = await Fixture.CreateAsync();

        var result = await fixture.RunAsync(FakeFactory(fixture));
        var persisted = await new LocalLiveEvaluationStore().LoadAsync(result.SummaryPath);

        Assert.Equal(3, persisted.LiveResultSchemaVersion);
        var assessments = Assert.Single(persisted.Cases).OutboundPolicyAssessments;
        Assert.Equal(2, assessments.Count);
        Assert.All(assessments, assessment =>
            Assert.Equal(OutboundAssessmentKind.Exact, assessment.AssessmentKind));
    }

    [Fact]
    public async Task LiveStore_SchemaTwoLoadsAssessmentAsNotRecorded()
    {
        using var fixture = await Fixture.CreateAsync();
        var result = await fixture.RunAsync(FakeFactory(fixture));
        var legacy = JsonNode.Parse(await File.ReadAllTextAsync(result.SummaryPath))!.AsObject();
        legacy["liveResultSchemaVersion"] = 2;
        foreach (var item in legacy["cases"]!.AsArray())
        {
            item!.AsObject().Remove("outboundPolicyAssessments");
        }
        var legacyJson = legacy.ToJsonString();
        await File.WriteAllTextAsync(result.SummaryPath, legacyJson);

        var persisted = await new LocalLiveEvaluationStore().LoadAsync(result.SummaryPath);

        var assessment = Assert.Single(Assert.Single(persisted.Cases).OutboundPolicyAssessments);
        Assert.Equal(OutboundAssessmentKind.NotRecorded, assessment.AssessmentKind);
        Assert.False(assessment.IsAllowed);
        Assert.Equal(legacyJson, await File.ReadAllTextAsync(result.SummaryPath));
    }

    [Fact]
    public async Task LegacyMissingAssessmentIsNeverInterpretedAsAllowed()
    {
        using var fixture = await Fixture.CreateAsync();
        var result = await fixture.RunAsync(FakeFactory(fixture));
        var legacy = JsonNode.Parse(await File.ReadAllTextAsync(result.SummaryPath))!.AsObject();
        legacy["liveResultSchemaVersion"] = 2;
        legacy["cases"]![0]!.AsObject().Remove("outboundPolicyAssessments");
        await File.WriteAllTextAsync(result.SummaryPath, legacy.ToJsonString());

        var persisted = await new LocalLiveEvaluationStore().LoadAsync(result.SummaryPath);

        Assert.DoesNotContain(
            Assert.Single(persisted.Cases).OutboundPolicyAssessments,
            assessment => assessment.IsAllowed);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsExactAssessmentForEveryProviderCall()
    {
        using var fixture = await Fixture.CreateAsync();

        var result = await fixture.RunAsync(FakeFactory(fixture));

        var item = Assert.Single(result.Cases);
        Assert.Equal(2, item.OutboundPolicyAssessments.Count);
        Assert.All(item.OutboundPolicyAssessments, assessment =>
        {
            Assert.Equal(OutboundAssessmentKind.Exact, assessment.AssessmentKind);
            Assert.True(assessment.IsAllowed);
            Assert.NotNull(assessment.PayloadFingerprint);
        });
    }

    [Fact]
    public async Task ExecuteAsync_BlockedCallOneNeverCreatesProvider()
    {
        const string secret = "fixture-live-call-one-secret";
        using var fixture = await Fixture.CreateAsync(initiative: secret);
        var providersCreated = 0;
        var service = LiveService(fixture, CreateGate(secret), "blocked-call-one");

        var result = await service.RunAsync(
            LiveEvaluationPlanner.Create(fixture.Suite),
            fixture.SuitePath,
            fixture.Memory,
            "Fake",
            (_, _) =>
            {
                providersCreated++;
                return new CountingProvider();
            },
            "model-a",
            "model-b",
            "low",
            "medium");

        Assert.Equal(0, providersCreated);
        var blocked = Assert.Single(result.Cases);
        Assert.Equal(LiveEvaluationExecutionStatus.SecurityBlocked, blocked.Status);
        Assert.Single(blocked.OutboundPolicyAssessments);
    }

    [Fact]
    public async Task ExecuteAsync_BlockedCallTwoDoesNotInvokeSecondCall()
    {
        const string secret = "fixture-live-call-two-secret";
        using var fixture = await Fixture.CreateAsync();
        var memory = await AddRootNoteTextAsync(fixture.Memory, secret);
        var provider = new ScriptedProvider(
            fixture.Suite.Cases[0].GoldenUnderstanding,
            InitiativeAnalysisTestData.Analysis());
        var service = LiveService(fixture, CreateGate(secret), "blocked-call-two");

        var result = await service.RunAsync(
            LiveEvaluationPlanner.Create(fixture.Suite),
            fixture.SuitePath,
            memory,
            "Fake",
            (_, _) => provider,
            "model-a",
            "model-b",
            "low",
            "medium");

        Assert.Equal(1, provider.Calls);
        var blocked = Assert.Single(result.Cases);
        Assert.Equal(LiveEvaluationExecutionStatus.SecurityBlocked, blocked.Status);
        Assert.Equal(2, blocked.OutboundPolicyAssessments.Count);
        Assert.True(blocked.OutboundPolicyAssessments[0].IsAllowed);
        Assert.False(blocked.OutboundPolicyAssessments[1].IsAllowed);
    }

    [Fact]
    public async Task ExecuteAsync_UsesSameCatalogGuardAndEvaluatorAsNormalAnalysis()
    {
        const string secret = "fixture-shared-gate-secret";
        using var fixture = await Fixture.CreateAsync(initiative: secret);
        var gate = CreateGate(secret);
        var normalProvider = new CountingProvider();
        var normalException = await Assert.ThrowsAsync<OutboundSecurityException>(() =>
            new InitiativeAnalysisService(normalProvider, outboundGate: gate).AnalyzeAsync(
                new InitiativeAnalysisRequest(
                    "initiative.md",
                    secret,
                    fixture.Memory,
                    "model-a",
                    "model-b",
                    PersistResult: false)));
        var live = await LiveService(fixture, gate, "shared-gate").RunAsync(
            LiveEvaluationPlanner.Create(fixture.Suite),
            fixture.SuitePath,
            fixture.Memory,
            "Fake",
            (_, _) => new CountingProvider(),
            "model-a",
            "model-b",
            "low",
            "medium");

        var liveAssessment = Assert.Single(Assert.Single(live.Cases).OutboundPolicyAssessments);
        Assert.Equal(
            normalException.Assessment.Results.Select(result => (result.PolicyId, result.Outcome)),
            liveAssessment.Results.Select(result => (result.PolicyId, result.Outcome)));
    }

    [Fact]
    public void Aggregate_CountsCompleteRepositoryAndAllExistingCategories()
    {
        var findings = SystemSecurityPolicyCatalog.Definitions.Select(definition =>
            new OutboundInspectionFinding(
                definition.Category,
                OutboundInspectionReasonCode.CompleteRepositoryPayload,
                OutboundTriggerKind.PayloadKind,
                1,
                false)).ToArray();
        var assessment = new OutboundPolicyEvaluator().Evaluate(OutboundAssessmentKind.Exact, null, findings);
        var result = SuccessfulResult("blocked", 1, [], []) with
        {
            Status = LiveEvaluationExecutionStatus.SecurityBlocked,
            OutboundPolicyAssessments = [assessment]
        };

        var aggregate = LiveEvaluationMetricCalculator.Aggregate([result]);

        Assert.Equal(1, aggregate.CompleteRepositoryOutbound);
        Assert.Equal(1, aggregate.RawSnapshotOutbound);
        Assert.Equal(1, aggregate.SourceBodyOutbound);
        Assert.Equal(1, aggregate.SecretOutbound);
        Assert.Equal(1, aggregate.AbsolutePathOutbound);
    }

    [Fact]
    public async Task RunAsync_UsesSameReviewedProfilesForGoldenAndLiveRetrieval()
    {
        var item = Case("concept") with
        {
            GoldenUnderstanding = Understanding("business analyzer")
        };
        using var fixture = await Fixture.CreateAsync([item]);
        var concepts = ReviewedConceptTestData.Resolution(
            ReviewedConceptTestData.Profile(
                "entity:business-service",
                ReviewedConceptTestData.Concept("business-analyzer")));
        var service = new LiveEvaluationService(
            store: new LocalLiveEvaluationStore(Path.Combine(fixture.Root, "concept-data")),
            clock: () => new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));

        var result = await service.RunAsync(
            LiveEvaluationPlanner.Create(fixture.Suite),
            fixture.SuitePath,
            fixture.Memory,
            concepts,
            "Fake",
            FakeFactory(fixture),
            "model-a",
            "model-b",
            "low",
            "medium");

        var caseResult = Assert.Single(result.Cases);
        var reason = Assert.Single(
            caseResult.Retrieval!.Components.Single(value => value.EntityId == "entity:business-service").MatchReasons,
            value => value.Signal == "reviewed concept");
        Assert.Contains("business-analyzer@", reason.MatchedValue, StringComparison.Ordinal);
        Assert.Equal(caseResult.RetrievalComparison!.Golden, caseResult.RetrievalComparison.Actual);
        Assert.Equal(ReviewedConceptResolutionStatus.Valid, result.ReviewedConceptStatus);
        Assert.Equal("catalog-fingerprint", result.ReviewedConceptCatalogFingerprint);
        Assert.Single(concepts.Profiles);
        Assert.Equal(concepts.Profiles.Count, result.ReviewedConceptProfileCount);
        var persisted = await new LocalLiveEvaluationStore().LoadAsync(result.SummaryPath);
        Assert.Equal(result.ReviewedConceptStatus, persisted.ReviewedConceptStatus);
        Assert.Equal(result.ReviewedConceptCatalogFingerprint, persisted.ReviewedConceptCatalogFingerprint);
        Assert.Equal(result.ReviewedConceptProfileCount, persisted.ReviewedConceptProfileCount);
    }

    [Fact]
    public async Task LiveSuite_LoadsFiveSafeCases()
    {
        var suite = await new LiveEvaluationSuiteSerializer().LoadAsync(FindRepositoryFile("evaluations", "live-suite.json"));

        Assert.Equal(2, suite.LiveEvaluationSchemaVersion);
        Assert.Equal(5, suite.Cases.Count);
        Assert.All(suite.Cases, item => Assert.StartsWith("live-", item.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SecurityRetrievalSuite_DefinesFiveBoundedSchemaTwoCases()
    {
        var suitePath = FindRepositoryFile("evaluations", "security-retrieval-live-suite.json");
        var suite = await new LiveEvaluationSuiteSerializer().LoadAsync(suitePath);
        var expectedIds = new[]
        {
            "live-security-outbound-source-bodies",
            "live-security-environment-secrets",
            "live-security-absolute-path-excerpts",
            "live-security-raw-snapshot-upload",
            "live-security-bounded-context-control"
        };

        Assert.Equal(2, suite.LiveEvaluationSchemaVersion);
        Assert.Equal(expectedIds, suite.Cases.Select(item => item.Id));
        Assert.Equal(5, suite.Cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(10, LiveEvaluationPlanner.Create(suite).ExpectedLogicalCalls);
        var selected = LiveEvaluationPlanner.Create(suite, caseIds: [expectedIds[1]]);
        Assert.Equal(expectedIds[1], Assert.Single(selected.Cases).Id);
        Assert.Equal(2, selected.ExpectedLogicalCalls);

        var suiteRoot = Path.GetDirectoryName(Path.GetFullPath(suitePath))!;
        foreach (var item in suite.Cases)
        {
            var initiativePath = LiveEvaluationSuiteSerializer.ResolveWithin(suiteRoot, item.InitiativePath);
            Assert.True(File.Exists(initiativePath));
            Assert.StartsWith(suiteRoot + Path.DirectorySeparatorChar, initiativePath, StringComparison.OrdinalIgnoreCase);
            var call1Expectations = JsonSerializer.Serialize(new
            {
                item.GoldenUnderstanding,
                item.UnderstandingExpectations
            });
            Assert.DoesNotContain(nameof(OutboundContextGuard), call1Expectations, StringComparison.Ordinal);
            Assert.DoesNotContain(nameof(InitiativeContextBuilder), call1Expectations, StringComparison.Ordinal);
            Assert.DoesNotContain(nameof(OpenAIReasoningProvider), call1Expectations, StringComparison.Ordinal);
            Assert.Equal(["EngineeringBrain.Infrastructure"], item.RepositoryExpectations.RequiredProjects);
            Assert.Empty(item.PolicyExpectations.Activations);
            Assert.Empty(item.PolicyExpectations.AcceptableOutcomes);
            Assert.Equal(0, item.PolicyExpectations.ExpectedBlockedRecommendationEscapeCount);
        }

        foreach (var item in suite.Cases.Take(4))
        {
            Assert.Equal(
                [typeof(OutboundContextGuard).FullName!],
                item.RepositoryExpectations.RequiredEntities);
        }

        var bounded = suite.Cases.Single(item => item.Id == "live-security-bounded-context-control");
        Assert.Equal(
            [typeof(InitiativeContextBuilder).FullName!],
            bounded.RepositoryExpectations.RequiredEntities);
        Assert.DoesNotContain(typeof(OutboundContextGuard).FullName!, bounded.RepositoryExpectations.RequiredEntities);
        Assert.Contains(typeof(OutboundContextGuard).FullName!, bounded.RepositoryExpectations.AcceptableEntities);

        var rawSnapshot = suite.Cases.Single(item => item.Id == "live-security-raw-snapshot-upload");
        Assert.DoesNotContain(rawSnapshot.PolicyExpectations.Activations,
            activation => activation.PolicyId == SystemPolicyCatalog.RemoteCompleteRepositoryId);
    }

    [Fact]
    public async Task SecurityRetrievalSuite_FakeRunIsOfflineAndLeakFree()
    {
        var suitePath = FindRepositoryFile("evaluations", "security-retrieval-live-suite.json");
        var suite = await new LiveEvaluationSuiteSerializer().LoadAsync(suitePath);
        using var fixture = await Fixture.CreateAsync();
        var service = new LiveEvaluationService(
            store: new LocalLiveEvaluationStore(Path.Combine(fixture.Root, "security-data")),
            clock: () => new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));

        var result = await service.RunAsync(
            LiveEvaluationPlanner.Create(suite),
            suitePath,
            fixture.Memory,
            "Fake",
            FakeFactory(fixture),
            "model-a",
            "model-b",
            "low",
            "medium");

        Assert.Equal(5, result.Aggregate.CasesSucceeded);
        Assert.Equal(0, result.Aggregate.CasesFailed);
        Assert.Equal(10, result.Aggregate.Usage.LogicalCalls);
        Assert.Equal(0, result.Aggregate.SourceBodyOutbound);
        Assert.Equal(0, result.Aggregate.SecretOutbound);
        Assert.Equal(0, result.Aggregate.AbsolutePathOutbound);
        Assert.Equal(0, result.Aggregate.RawSnapshotOutbound);
        Assert.Equal(0, result.Aggregate.BlockedRecommendationEscapeCount);
    }

    [Fact]
    public async Task SecurityFixture_SeparatesInitiativeRepositoryAnalysisAndPolicyExpectations()
    {
        var suite = await new LiveEvaluationSuiteSerializer().LoadAsync(FindRepositoryFile("evaluations", "live-suite.json"));
        var item = suite.Cases.Single(value => value.Id == "live-security-repository-upload");
        var call1Expectations = JsonSerializer.Serialize(new
        {
            item.GoldenUnderstanding,
            item.UnderstandingExpectations
        });

        Assert.DoesNotContain("OutboundContextGuard", call1Expectations, StringComparison.Ordinal);
        Assert.DoesNotContain("InitiativeContextBuilder", call1Expectations, StringComparison.Ordinal);
        Assert.Equal(
            ["EngineeringBrain.Infrastructure.OpenAIReasoningProvider"],
            item.RepositoryExpectations.RequiredEntities);
        Assert.DoesNotContain("EngineeringBrain.Infrastructure.OutboundContextGuard", item.RepositoryExpectations.RequiredEntities);
        Assert.Contains("EngineeringBrain.Infrastructure.OutboundContextGuard", item.RepositoryExpectations.AcceptableEntities);
        Assert.Contains("EngineeringBrain.Infrastructure.InitiativeContextBuilder", item.RepositoryExpectations.AcceptableEntities);
        Assert.Contains("EngineeringBrain.Core.IReasoningProvider", item.RepositoryExpectations.AcceptableEntities);
        Assert.Contains("EngineeringBrain.Infrastructure", item.RepositoryExpectations.RequiredProjects);
        Assert.Equal(
            ["repository collection", "remote transmission", "LLM provider integration"],
            item.UnderstandingExpectations.RequiredCapabilities);
        Assert.Equal(
            ["repository packaging", "large payload handling"],
            item.UnderstandingExpectations.AcceptableCapabilities);
        Assert.Equal(
            ["provider API", "authorization or consent", "repository scope", "secret or sensitive-data handling", "retention or deletion", "repository or payload size limits", "precision success criteria"],
            item.UnderstandingExpectations.ExpectedUnknownTopics);
        Assert.Equal(
            [["remote", "transmission"], ["repository", "transmission"], ["repository", "upload"]],
            Assert.Single(item.UnderstandingExpectations.CapabilityAlternatives).Alternatives);
        Assert.Equal(
            [
                "provider API:provider+api|provider|endpoint",
                "authorization or consent:authorization|consent|permission",
                "repository scope:repository+scope|complete+repository|files+included|files+excluded|directories+included|directories+excluded",
                "secret or sensitive-data handling:secret|credentials|sensitive+data|redacted",
                "retention or deletion:retention|deletion|retain",
                "repository or payload size limits:repository+size|payload+limits|upload+limits|large+repository|large+repositories",
                "precision success criteria:precision+evaluated|precision+measured|precision+success|precision+improvement"
            ],
            item.UnderstandingExpectations.UnknownTopicAlternatives.Select(value =>
                $"{value.Id}:{string.Join('|', value.Alternatives.Select(alternative => string.Join('+', alternative)))}"));
        Assert.Contains(InitiativeAnalysisStatus.NeedsClarification, item.AnalysisExpectations.AcceptableStatuses);
        var activation = Assert.Single(item.PolicyExpectations.Activations);
        Assert.Equal(SystemPolicyCatalog.RemoteCompleteRepositoryId, activation.PolicyId);
        Assert.True(activation.ExpectedActive);
        Assert.Contains(PolicyOutcome.Blocked, item.PolicyExpectations.AcceptableOutcomes);
        Assert.Equal(0, item.PolicyExpectations.ExpectedBlockedRecommendationEscapeCount);

        var priorLiveUnderstanding = item.GoldenUnderstanding with
        {
            Unknowns =
            [
                "Which LLM provider and API should be used?",
                "What user authorization or consent is required?",
                "What does complete repository include?",
                "Should sensitive files, secrets, dependencies, generated artifacts, or hidden files be excluded?",
                "How long may the provider retain the uploaded repository?",
                "How should large repositories be handled?",
                "What precision improvement is expected and how will it be measured?"
            ]
        };
        var metrics = new LiveEvaluationMetricCalculator().EvaluateUnderstanding(
            item, priorLiveUnderstanding, ProjectMemoryTestFactory.Create());
        Assert.Equal(1, metrics.UnknownTopicCoverage);
    }

    [Fact]
    public async Task LiveSuite_RejectsVersionOneRatherThanReinterpretingIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"live-suite-v1-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, "{\"liveEvaluationSchemaVersion\":1,\"id\":\"old\",\"description\":\"old\",\"cases\":[]}");

            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new LiveEvaluationSuiteSerializer().LoadAsync(path));

            Assert.Contains("schema 1 is unsupported", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LiveAuthorization_RequiresExplicitRemoteOptIn()
    {
        var read = false;
        var error = Assert.Throws<InvalidOperationException>(() => LiveEvaluationAuthorization.Authorize(false, false, _ =>
        {
            read = true;
            return "secret";
        }));
        Assert.False(read);
        Assert.Contains("eval-live", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveAuthorization_RequiresApiKey()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            LiveEvaluationAuthorization.Authorize(false, true, _ => null));
        Assert.Contains("OPENAI_API_KEY", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveAuthorization_FakeProviderNeedsNoKeyAndCannotMixModes()
    {
        Assert.Null(LiveEvaluationAuthorization.Authorize(true, false, _ => throw new InvalidOperationException()));
        Assert.Throws<InvalidOperationException>(() => LiveEvaluationAuthorization.Authorize(true, true));
    }

    [Fact]
    public void LivePlan_LimitsCasesRunsAndLogicalCalls()
    {
        var item = Case("one");
        var five = Suite([item, Case("two"), Case("three"), Case("four"), Case("five")]);

        Assert.Equal(10, LiveEvaluationPlanner.Create(five).ExpectedLogicalCalls);
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveEvaluationPlanner.Create(five, 4));
        Assert.Throws<InvalidDataException>(() => LiveEvaluationPlanner.Create(five, 3));
        Assert.Equal(6, LiveEvaluationPlanner.Create(five, 3, ["one"]).ExpectedLogicalCalls);
    }

    [Fact]
    public void UnderstandingMetrics_CompareCapabilitiesUnknownsAndSearchTerms()
    {
        var item = Case("case") with
        {
            GoldenUnderstanding = Understanding("business", "execute") with { Unknowns = ["delivery channel"] },
            UnderstandingExpectations = UnderstandingExpectations() with
            {
                RequiredCapabilities = ["business execution"],
                ExpectedUnknownTopics = ["authorization or consent"]
            }
        };
        var actual = Understanding("business", "execute") with
        {
            TechnicalCapabilities = ["business execution"],
            Unknowns = ["What user authorization or consent is required?"],
            SearchTerms = ["business", "execute", "unmappednoise"]
        };

        var metrics = new LiveEvaluationMetricCalculator().EvaluateUnderstanding(item, actual, ProjectMemoryTestFactory.Create());

        Assert.Equal(1, metrics.RequiredCapabilityHitRate);
        Assert.Equal(1, metrics.UnknownTopicCoverage);
        Assert.Empty(metrics.MissingExpectedUnknownTopics);
        Assert.Contains("unmappednoise", metrics.PotentiallyHarmfulSearchTerms);
    }

    [Fact]
    public void UnderstandingMetrics_UseFixtureAlternativesWithinOneActualItem()
    {
        var item = Case("alternatives") with
        {
            UnderstandingExpectations = new LiveUnderstandingExpectations(
                [],
                [],
                ["authorization", "repository scope", "split topic", "missing topic"])
            {
                UnknownTopicAlternatives =
                [
                    Alternatives("authorization", ["authorization"], ["consent"]),
                    Alternatives("repository scope", ["repository", "scope"], ["files", "included"]),
                    Alternatives("split topic", ["alpha", "beta"]),
                    Alternatives("missing topic", ["retention"])
                ]
            }
        };
        var actual = Understanding("business") with
        {
            Unknowns = ["Users grant authorization and consent", "Which files are included?", "alpha", "beta"]
        };

        var metrics = new LiveEvaluationMetricCalculator().EvaluateUnderstanding(
            item, actual, ProjectMemoryTestFactory.Create());

        Assert.Equal(2, metrics.ExpectedUnknownTopicsHit);
        Assert.Equal(0.5, metrics.UnknownTopicCoverage);
        var authorization = Assert.Single(metrics.UnknownTopicMatches!, value => value.Id == "authorization");
        Assert.True(authorization.Matched);
        Assert.Equal(["authorization"], authorization.MatchedAlternative);
        var scope = Assert.Single(metrics.UnknownTopicMatches!, value => value.Id == "repository scope");
        Assert.Equal(["files", "included"], scope.MatchedAlternative);
        Assert.Contains("split topic", metrics.MissingExpectedUnknownTopics);
        Assert.Contains("missing topic", metrics.MissingExpectedUnknownTopics);
    }

    [Fact]
    public void UnderstandingMetrics_CapabilityAlternativesAcceptParaphraseButNotUnrelatedText()
    {
        var item = Case("capabilities") with
        {
            UnderstandingExpectations = new LiveUnderstandingExpectations(
                ["remote transmission", "database migration"],
                [],
                [])
            {
                CapabilityAlternatives =
                [
                    Alternatives("remote transmission", ["remote", "transmission"], ["repository", "transmission"])
                ]
            }
        };
        var actual = Understanding("business") with
        {
            TechnicalCapabilities = ["Automatic transmission of repository data"]
        };

        var metrics = new LiveEvaluationMetricCalculator().EvaluateUnderstanding(
            item, actual, ProjectMemoryTestFactory.Create());

        Assert.Equal(0.5, metrics.RequiredCapabilityHitRate);
        Assert.Equal(["repository", "transmission"], metrics.RequiredCapabilityMatches!
            .Single(value => value.Id == "remote transmission").MatchedAlternative);
        Assert.Contains("database migration", metrics.MissingRequiredCapabilities);

        var unrelated = new LiveEvaluationMetricCalculator().EvaluateUnderstanding(
            item,
            actual with { TechnicalCapabilities = ["Send email notifications"] },
            ProjectMemoryTestFactory.Create());
        Assert.Contains("remote transmission", unrelated.MissingRequiredCapabilities);
    }

    [Fact]
    public async Task SecurityFixture_MatchesLatestRealUnderstandingWithoutHidingMissingPrecisionTopic()
    {
        var suite = await new LiveEvaluationSuiteSerializer().LoadAsync(FindRepositoryFile("evaluations", "live-suite.json"));
        var item = suite.Cases.Single(value => value.Id == "live-security-repository-upload");
        var actual = item.GoldenUnderstanding with
        {
            TechnicalCapabilities =
            [
                "Repository-wide file collection and packaging",
                "Automatic upload or transmission of repository data",
                "LLM context ingestion using repository contents",
                "Configuration or control for enabling automatic repository transmission"
            ],
            SearchTerms = ["repository upload", "complete repository context", "LLM provider integration"],
            Unknowns =
            [
                "Which LLM provider or providers are supported",
                "Whether transmission is enabled by default",
                "How users grant consent or configure the feature",
                "Which files or directories are included or excluded",
                "How secrets, credentials, and other sensitive data are detected or redacted",
                "Maximum repository size or upload limits",
                "Transport protocol and authentication method",
                "Data retention, logging, and deletion policies"
            ]
        };

        var metrics = new LiveEvaluationMetricCalculator().EvaluateUnderstanding(
            item, actual, ProjectMemoryTestFactory.Create());

        Assert.Equal(1, metrics.RequiredCapabilityHitRate);
        Assert.Equal(6, metrics.ExpectedUnknownTopicsHit);
        Assert.Equal(6d / 7d, metrics.UnknownTopicCoverage, 10);
        Assert.Equal(["precision success criteria"], metrics.MissingExpectedUnknownTopics);
        Assert.False(metrics.UnknownTopicMatches!.Single(value => value.Id == "precision success criteria").Matched);
    }

    [Fact]
    public async Task GoldenVsLiveRetrieval_ReportsInterpretationDamage()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var item = Case("case");
        var calculator = new LiveEvaluationMetricCalculator();
        var retriever = new InitiativeCandidateRetriever();
        var golden = calculator.EvaluateRetrieval(
            retriever.Retrieve(item.GoldenUnderstanding, memory.Manifest, memory.SourceSnapshot), item.RepositoryExpectations);
        var actual = calculator.EvaluateRetrieval(
            retriever.Retrieve(Understanding("quantumflux"), memory.Manifest, memory.SourceSnapshot), item.RepositoryExpectations);

        var comparison = LiveEvaluationMetricCalculator.CompareRetrieval(golden, actual);

        Assert.Equal(1, comparison.Golden.RecallAt10);
        Assert.Equal(0, comparison.Actual.RecallAt10);
        Assert.True(comparison.MeanReciprocalRankDelta < 0);
        Assert.Empty(comparison.Golden.MissingRequiredEntities!);
        Assert.Contains("Demo.Business.BusinessService", comparison.Actual.MissingRequiredEntities!);

        var summary = LiveEvaluationMetricCalculator.SummarizeRetrieval(
            [SuccessfulResult("retrieval", 1, [], []) with { RetrievalComparison = comparison }]);
        Assert.NotNull(summary);
        Assert.Equal(comparison.Golden.RecallAt5, summary.Golden.RecallAt5);
        Assert.Equal(comparison.Actual.RecallAt5, summary.Actual.RecallAt5);
        Assert.Equal(summary.Actual.RecallAt5 - summary.Golden.RecallAt5, summary.RecallAt5Delta);
    }

    [Fact]
    public void RetrievalSummary_PreservesZeroGoldenZeroLiveAndZeroDeltaSeparately()
    {
        var zero = new LiveRetrievalMetrics(0, 0, 0, null, 0, 0);
        var comparison = LiveEvaluationMetricCalculator.CompareRetrieval(zero, zero);

        var summary = LiveEvaluationMetricCalculator.SummarizeRetrieval(
            [SuccessfulResult("zero", 1, [], []) with { RetrievalComparison = comparison }]);

        Assert.NotNull(summary);
        Assert.Equal(0, summary.Golden.RecallAt5);
        Assert.Equal(0, summary.Golden.RecallAt10);
        Assert.Equal(0, summary.Golden.MeanReciprocalRank);
        Assert.Equal(0, summary.Actual.RecallAt5);
        Assert.Equal(0, summary.Actual.RecallAt10);
        Assert.Equal(0, summary.Actual.MeanReciprocalRank);
        Assert.Equal(0, summary.RecallAt5Delta);
        Assert.Equal(0, summary.RecallAt10Delta);
        Assert.Equal(0, summary.MeanReciprocalRankDelta);
    }

    [Fact]
    public async Task FakeRun_AggregatesUsageAndPersistsReviewWithoutKey()
    {
        using var fixture = await Fixture.CreateAsync();

        var result = await fixture.RunAsync(FakeFactory(fixture));

        Assert.Equal(2, result.Aggregate.Usage.LogicalCalls);
        Assert.Equal(2, result.Aggregate.Usage.ProviderAttempts);
        Assert.True(result.Aggregate.Usage.ActualInputTokens > 0);
        Assert.Equal(80, result.Aggregate.Usage.ReasoningTokens);
        Assert.True(File.Exists(result.SummaryPath));
        Assert.True(File.Exists(result.ReviewPath));
        Assert.Equal(3, result.LiveResultSchemaVersion);
        var persisted = await new LocalLiveEvaluationStore().LoadAsync(result.SummaryPath);
        Assert.Equal(3, persisted.LiveResultSchemaVersion);
        Assert.NotNull(Assert.Single(persisted.Cases).UnderstandingMetrics!.RequiredCapabilityMatches);
        var review = await File.ReadAllTextAsync(result.ReviewPath);
        Assert.Contains("Initiative understanding [1-5]:", review, StringComparison.Ordinal);
        Assert.Contains("Golden retrieval Recall@5/10/MRR", review, StringComparison.Ordinal);
        Assert.Contains("Live retrieval Recall@5/10/MRR", review, StringComparison.Ordinal);
        Assert.Contains("Understanding expectation matches", review, StringComparison.Ordinal);
        Assert.Contains("missing required retrieval entities", review, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Policy activation accuracy", review, StringComparison.Ordinal);
        Assert.DoesNotContain("OPENAI_API_KEY", review, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FakeSecurityRun_ExercisesGovernanceWithoutNetwork()
    {
        var suite = await new LiveEvaluationSuiteSerializer().LoadAsync(FindRepositoryFile("evaluations", "live-suite.json"));
        var item = suite.Cases.Single(value => value.Id == "live-security-repository-upload") with
        {
            InitiativePath = "initiative.md"
        };
        using var fixture = await Fixture.CreateAsync(
            [item],
            "Allow automatically sending the complete repository to the LLM provider to improve precision.");

        var result = await fixture.RunAsync(FakeFactory(fixture));

        var evaluated = Assert.Single(result.Cases);
        Assert.Equal(PolicyOutcome.Blocked, evaluated.PolicyOutcome);
        Assert.Equal(1, evaluated.PolicyMetrics!.PolicyActivationAccuracy);
        Assert.True(evaluated.PolicyMetrics.PolicyOutcomeCorrect);
        Assert.Equal(0, evaluated.PolicyMetrics.BlockedRecommendationEscapeCount);
        Assert.Contains(evaluated.Recommendations, recommendation =>
            recommendation.ValidatedRecommendation.Recommendation.Decision == RecommendationDecision.Create
            && recommendation.Disposition == RecommendationDisposition.Rejected);
        Assert.Contains(evaluated.Analysis!.Recommendations, recommendation =>
            recommendation.Decision == RecommendationDecision.AvoidModifying);
        Assert.Equal(2, result.Aggregate.Usage.LogicalCalls);
        Assert.Equal(0, result.Aggregate.BlockedRecommendationEscapeCount);
    }

    [Fact]
    public async Task LiveResultStore_RejectsHistoricalSchemaWithoutRewritingIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"live-result-v1-{Guid.NewGuid():N}.json");
        const string historical = "{\"liveResultSchemaVersion\":1}";
        try
        {
            await File.WriteAllTextAsync(path, historical);

            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new LocalLiveEvaluationStore().LoadAsync(path));

            Assert.Contains("schema 1 is unsupported", error.Message, StringComparison.Ordinal);
            Assert.Equal(historical, await File.ReadAllTextAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LiveResultStore_MissingReviewedConceptProvenanceDefaultsToUnknown()
    {
        using var fixture = await Fixture.CreateAsync();
        var result = await fixture.RunAsync(FakeFactory(fixture));
        var historical = JsonNode.Parse(await File.ReadAllTextAsync(result.SummaryPath))!.AsObject();
        historical.Remove("reviewedConceptStatus");
        historical.Remove("reviewedConceptCatalogFingerprint");
        historical.Remove("reviewedConceptProfileCount");
        await File.WriteAllTextAsync(result.SummaryPath, historical.ToJsonString());

        var loaded = await new LocalLiveEvaluationStore().LoadAsync(result.SummaryPath);

        Assert.Equal("Unknown", loaded.ReviewedConceptStatus.ToString());
        Assert.Null(loaded.ReviewedConceptCatalogFingerprint);
        Assert.Equal(0, loaded.ReviewedConceptProfileCount);
    }

    [Fact]
    public async Task LiveResultStore_ExplicitAbsentReviewedConceptProvenanceRoundTripsAsAbsent()
    {
        using var fixture = await Fixture.CreateAsync();

        var result = await fixture.RunAsync(FakeFactory(fixture));
        var loaded = await new LocalLiveEvaluationStore().LoadAsync(result.SummaryPath);

        Assert.Equal(ReviewedConceptResolutionStatus.Absent, loaded.ReviewedConceptStatus);
        Assert.Null(loaded.ReviewedConceptCatalogFingerprint);
        Assert.Equal(0, loaded.ReviewedConceptProfileCount);
    }

    [Fact]
    public async Task ProviderFailure_IsRecordedAndPreviousCaseIsPreserved()
    {
        using var fixture = await Fixture.CreateAsync([Case("first"), Case("second")]);

        var result = await fixture.RunAsync((item, run) => item.Id == "first"
            ? new FakeLiveReasoningProvider(item, fixture.Memory.SourceSnapshot, run, "low", "medium")
            : new ThrowingProvider(structured: false, retries: 1));

        Assert.Equal(2, result.Cases.Count);
        Assert.Equal(LiveEvaluationExecutionStatus.Succeeded, result.Cases[0].Status);
        Assert.Equal(LiveEvaluationExecutionStatus.ProviderFailure, result.Cases[1].Status);
        Assert.Equal(1, result.Aggregate.Usage.Retries);
        Assert.Equal(4, result.Aggregate.Usage.ProviderAttempts);
        Assert.Equal(30, result.Cases[1].Usage.Single().ActualInputTokens);
        Assert.Equal(2, result.Cases[1].Usage.Single().ReasoningTokens);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(result.ResultDirectory, "cases"), "*.json").Length);
    }

    [Fact]
    public async Task StructuredOutputFailure_IsExplicitAndCounted()
    {
        using var fixture = await Fixture.CreateAsync();

        var result = await fixture.RunAsync((_, _) => new ThrowingProvider(structured: true));

        Assert.Equal(LiveEvaluationExecutionStatus.StructuredOutputFailure, result.Cases[0].Status);
        Assert.Equal(1, result.Aggregate.StructuredOutputFailures);
        Assert.Equal("InvalidStructuredOutput", result.Cases[0].ErrorCategory);
    }

    [Fact]
    public async Task SecurityBlocked_PreventsProviderCall()
    {
        using var fixture = await Fixture.CreateAsync(initiative: "api_key=abcdefghijklmnop");
        var provider = new CountingProvider();

        var result = await fixture.RunAsync((_, _) => provider);

        Assert.Equal(LiveEvaluationExecutionStatus.SecurityBlocked, result.Cases[0].Status);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(1, result.Aggregate.SecurityBlocked);
        Assert.Empty(result.Cases[0].Usage);
    }

    [Fact]
    public async Task InvalidEvidence_RemainsInvalidAndNeverCountsAsFabricatedAccepted()
    {
        using var fixture = await Fixture.CreateAsync();
        var analysis = Analysis("entity:not-real");

        var result = await fixture.RunAsync((item, run) => new ScriptedProvider(item.GoldenUnderstanding, analysis));

        Assert.Equal(1, result.Aggregate.InvalidEvidence);
        Assert.Equal(0, result.Aggregate.FabricatedEntitiesAccepted);
        Assert.Equal(
            EvidenceValidationStatus.Invalid,
            result.Cases[0].Recommendations[0].ValidatedRecommendation.ValidationStatus);
    }

    [Fact]
    public async Task ProviderErrors_AreRedactedBeforePersistence()
    {
        using var fixture = await Fixture.CreateAsync();
        const string secret = "supersecretvalue";

        var result = await fixture.RunAsync((_, _) => new RawThrowingProvider($"api_key={secret}"));

        var stored = string.Join('\n', Directory.GetFiles(result.ResultDirectory, "*", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        Assert.DoesNotContain(secret, stored, StringComparison.Ordinal);
        Assert.Contains("TransportFailure", stored, StringComparison.Ordinal);
        Assert.Contains("The reasoning provider request failed.", stored, StringComparison.Ordinal);
    }

    [Fact]
    public void Call2Metrics_CheckDecisionClarificationAndEvidence()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var item = Case("case");
        var evidence = InitiativeAnalysisTestData.EntityEvidence(snapshot, "entity:business-service");
        var recommendation = InitiativeAnalysisTestData.Recommendation(RecommendationDecision.Reuse, [evidence]);
        var analysis = InitiativeAnalysisTestData.Analysis(recommendation) with
        {
            RelevantEntityIds = ["entity:business-service"],
            RelevantProjectIds = ["project:business"]
        };
        var validated = new AnalysisEvidenceValidator().Validate(analysis, snapshot);
        var governed = new PolicyComplianceValidator().Evaluate(validated);

        var metrics = new LiveEvaluationMetricCalculator().EvaluateAnalysis(
            item, analysis, governed.Recommendations, snapshot);

        Assert.True(metrics.ExpectedDecisionTypePresent);
        Assert.Equal(1, metrics.EvidenceValidationRate);
        Assert.Empty(metrics.MissingRelevantEntities);
        Assert.Empty(metrics.MissingRelevantProjects);
    }

    [Fact]
    public void NeedsClarificationComparison_RequiresExpectedStatusAndRelevantQuestion()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var item = Case("ambiguous") with
        {
            GoldenUnderstanding = Understanding("notification") with { Unknowns = ["delivery channel"] },
            AnalysisExpectations = AnalysisExpectations() with
            {
                AcceptableStatuses = [InitiativeAnalysisStatus.NeedsClarification],
                ExpectedClarificationTopics = ["delivery channel"]
            }
        };
        var analysis = InitiativeAnalysisTestData.Analysis() with
        {
            Status = InitiativeAnalysisStatus.NeedsClarification,
            ClarifyingQuestions = ["Which delivery channel is required?"]
        };

        var metrics = new LiveEvaluationMetricCalculator().EvaluateAnalysis(item, analysis, [], snapshot);

        Assert.True(metrics.AnalysisStatusCorrect);
        Assert.True(metrics.ClarifyingQuestionsRelevant);
    }

    [Fact]
    public void PolicyMetrics_CompareActivationOutcomeAndRejectedBlock()
    {
        var expected = new LivePolicyExpectations(
            [new LivePolicyActivationExpectation(SystemPolicyCatalog.RemoteCompleteRepositoryId, true)],
            [PolicyOutcome.Blocked],
            0);
        var governance = Governance(
            PolicyComplianceStatus.Violated,
            RecommendationDisposition.Rejected,
            PolicyOutcome.Blocked);

        var metrics = LiveEvaluationMetricCalculator.EvaluatePolicy(expected, governance);

        Assert.Equal(1, metrics.PolicyActivationAccuracy);
        Assert.True(metrics.PolicyOutcomeCorrect);
        Assert.Equal(0, metrics.BlockedRecommendationEscapeCount);
        Assert.True(metrics.BlockedRecommendationEscapeCountCorrect);
    }

    [Fact]
    public void PolicyMetrics_DetectMissingAndUnexpectedActivationAndWrongOutcome()
    {
        var missingExpected = new LivePolicyExpectations(
            [new LivePolicyActivationExpectation(SystemPolicyCatalog.RemoteCompleteRepositoryId, true)],
            [PolicyOutcome.Blocked],
            0);
        var unexpectedExpected = missingExpected with
        {
            Activations = [new LivePolicyActivationExpectation(SystemPolicyCatalog.RemoteCompleteRepositoryId, false)]
        };
        var inactive = Governance(
            PolicyComplianceStatus.NotApplicable,
            RecommendationDisposition.Accepted,
            PolicyOutcome.Allowed);
        var active = Governance(
            PolicyComplianceStatus.Violated,
            RecommendationDisposition.Rejected,
            PolicyOutcome.Blocked);

        var missing = LiveEvaluationMetricCalculator.EvaluatePolicy(missingExpected, inactive);
        var unexpected = LiveEvaluationMetricCalculator.EvaluatePolicy(unexpectedExpected, active);

        Assert.Equal(0, missing.PolicyActivationAccuracy);
        Assert.False(missing.PolicyOutcomeCorrect);
        Assert.Equal(0, unexpected.PolicyActivationAccuracy);
    }

    [Theory]
    [InlineData(RecommendationDisposition.Accepted)]
    [InlineData(RecommendationDisposition.NeedsReview)]
    public void PolicyMetrics_CountBlockViolationThatEscapesRejection(RecommendationDisposition disposition)
    {
        var expected = new LivePolicyExpectations([], [PolicyOutcome.Blocked], 0);
        var governance = Governance(PolicyComplianceStatus.Violated, disposition, PolicyOutcome.Blocked);

        var metrics = LiveEvaluationMetricCalculator.EvaluatePolicy(expected, governance);

        Assert.Equal(1, metrics.BlockedRecommendationEscapeCount);
        Assert.False(metrics.BlockedRecommendationEscapeCountCorrect);
    }

    [Fact]
    public void DecisionHitCannotHideForbiddenCreateFromPolicyMetrics()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var item = Case("security") with
        {
            AnalysisExpectations = AnalysisExpectations() with
            {
                AcceptableDecisionTypes = [RecommendationDecision.AvoidModifying]
            },
            PolicyExpectations = new LivePolicyExpectations(
                [new LivePolicyActivationExpectation(SystemPolicyCatalog.RemoteCompleteRepositoryId, true)],
                [PolicyOutcome.Blocked],
                0)
        };
        var avoid = InitiativeAnalysisTestData.Recommendation(RecommendationDecision.AvoidModifying, []);
        var create = InitiativeAnalysisTestData.Recommendation(
            RecommendationDecision.Create,
            [],
            [new PolicyRelevantAction(
                PolicyActionOperation.RemoteTransmission,
                PolicyActionBoundary.Remote,
                PolicyContentScope.CompleteRepository,
                PolicyAuthorizationMode.Explicit,
                null,
                null)]);
        var analysis = InitiativeAnalysisTestData.Analysis(avoid, create);
        var governance = new PolicyComplianceValidator().Evaluate([
            new ValidatedRecommendation(avoid, EvidenceValidationStatus.Validated, [], []),
            new ValidatedRecommendation(create, EvidenceValidationStatus.Proposal, [], [])
        ]);

        var call2 = new LiveEvaluationMetricCalculator().EvaluateAnalysis(
            item, analysis, governance.Recommendations, snapshot);
        var policy = LiveEvaluationMetricCalculator.EvaluatePolicy(item.PolicyExpectations, governance);

        Assert.True(call2.ExpectedDecisionTypePresent);
        Assert.Equal(1, call2.ExpectedDecisionHitRate);
        Assert.Equal(PolicyOutcome.Blocked, policy.ActualOutcome);
        Assert.Equal(0, policy.BlockedRecommendationEscapeCount);
        Assert.Contains(governance.Recommendations, recommendation =>
            recommendation.ValidatedRecommendation.Recommendation.Decision == RecommendationDecision.Create
            && recommendation.Disposition == RecommendationDisposition.Rejected);
    }

    [Fact]
    public void ConsistencyMetrics_UseSetOverlapInsteadOfTextEquality()
    {
        var first = SuccessfulResult("case", 1, [RecommendationDecision.Reuse], ["entity:a"]);
        var second = SuccessfulResult("case", 2, [RecommendationDecision.Reuse], ["entity:a", "entity:b"]);

        var metrics = LiveConsistencyCalculator.Calculate([first, second]);

        Assert.Equal(1, metrics.StatusAgreement);
        Assert.Equal(1, metrics.DecisionAgreement);
        Assert.Equal(0.5, metrics.EntityReferenceOverlap);
        Assert.Equal(1, metrics.EvidenceValidityAgreement);
    }

    [Fact]
    public async Task FixedClock_ProducesDeterministicOfflineRunAndArtifact()
    {
        using var fixture = await Fixture.CreateAsync();
        var first = await fixture.RunAsync(FakeFactory(fixture));
        var firstJson = await File.ReadAllTextAsync(first.SummaryPath);
        var second = await fixture.RunAsync(FakeFactory(fixture));

        Assert.Equal(first.RunId, second.RunId);
        Assert.Equal(firstJson, await File.ReadAllTextAsync(second.SummaryPath));
    }

    [Fact]
    public void Pricing_UsesActualAndCachedTokensWithoutHardcodedModelPrices()
    {
        var catalog = new LivePricingCatalog(1, [new LiveModelPricing("Fake", "model-a", 2, 1, 4)]);
        var usage = new ReasoningCallUsage(ReasoningStage.InitiativeUnderstanding, "Fake", "model-a",
            200, 100, 40, 20, 1, 0);

        Assert.Equal(0.00024m, catalog.Estimate(usage));
        Assert.Null(catalog.Estimate(usage with { Model = "unknown" }));
    }

    private static Func<LiveEvaluationCase, int, IReasoningProvider> FakeFactory(Fixture fixture) =>
        (item, run) => new FakeLiveReasoningProvider(item, fixture.Memory.SourceSnapshot, run, "low", "medium");

    private static LiveEvaluationService LiveService(
        Fixture fixture,
        OutboundRequestGate gate,
        string directory) => new(
        gate: gate,
        store: new LocalLiveEvaluationStore(Path.Combine(fixture.Root, directory)),
        clock: () => new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));

    private static OutboundRequestGate CreateGate(params string[] secrets) => new(
        new OutboundContextGuard(new FixedSecretValueSource(secrets)),
        new OutboundPolicyEvaluator());

    private static async Task<ProjectMemorySyncResult> AddRootNoteTextAsync(
        ProjectMemorySyncResult memory,
        string text)
    {
        var rootNote = memory.Manifest.Notes.Single(note => note.Kind == KnowledgeNoteKind.RootIndex);
        var path = Path.Combine(memory.Location, rootNote.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var content = await File.ReadAllTextAsync(path) + Environment.NewLine + text;
        await File.WriteAllTextAsync(path, content);
        var updated = rootNote with { ContentHash = KnowledgeIdentity.ContentHash(content) };
        return memory with
        {
            Manifest = memory.Manifest with
            {
                Notes = memory.Manifest.Notes.Select(note => note.Identity == rootNote.Identity ? updated : note).ToArray()
            }
        };
    }

    private static LiveEvaluationSuite Suite(IReadOnlyList<LiveEvaluationCase> cases) => new(2, "live-suite", "safe", cases);

    private static LiveEvaluationCase Case(string id) => new(
        id,
        "Business service evaluation",
        "initiative.md",
        Understanding("BusinessService", "Execute"),
        UnderstandingExpectations(),
        RepositoryExpectations(),
        AnalysisExpectations(),
        PolicyExpectations());

    private static LiveUnderstandingExpectations UnderstandingExpectations() => new(
        ["BusinessService"],
        ["Execute"],
        []);

    private static LiveLexicalExpectationAlternatives Alternatives(
        string id,
        params IReadOnlyList<string>[] alternatives) => new(id, alternatives);

    private static LiveRepositoryExpectations RepositoryExpectations() => new(
        ["Demo.Business.BusinessService"],
        [],
        ["Business"],
        []);

    private static LiveAnalysisExpectations AnalysisExpectations() => new(
        [InitiativeAnalysisStatus.Complete],
        [RecommendationDecision.Reuse],
        []);

    private static LivePolicyExpectations PolicyExpectations() => new(
        [],
        [PolicyOutcome.Allowed],
        0);

    private static PolicyGovernanceResult Governance(
        PolicyComplianceStatus complianceStatus,
        RecommendationDisposition disposition,
        PolicyOutcome outcome)
    {
        var recommendation = InitiativeAnalysisTestData.Recommendation(RecommendationDecision.Create, []);
        var validated = new ValidatedRecommendation(
            recommendation,
            EvidenceValidationStatus.Proposal,
            [],
            []);
        var policy = new PolicyComplianceResult(
            SystemPolicyCatalog.RemoteCompleteRepositoryId,
            1,
            PolicySourceKind.System,
            PolicySeverity.Block,
            complianceStatus,
            "test",
            new PolicyProvenance("Tests", "LiveEvaluationTests", null, null, null, null, null));
        return new PolicyGovernanceResult(
            [new GovernedRecommendation(validated, [policy], disposition)],
            outcome);
    }

    private static InitiativeUnderstanding Understanding(params string[] terms) =>
        InitiativeAnalysisTestData.Understanding(terms);

    private static InitiativeAnalysis Analysis(string entityId) => new(
        InitiativeAnalysisStatus.Complete,
        "analysis",
        [],
        [entityId],
        [new AnalysisRecommendation(
            RecommendationDecision.Reuse,
            "fabricated",
            "invalid evidence test",
            EpistemicStatus.Fact,
            [new EvidenceReference(EvidenceKind.Entity, "repo-1", "main", entityId, "project:business",
                null, null, null, "src/Business/BusinessService.cs", 1, 2, ResolutionLevel.Semantic)],
            [], [], [])],
        [], [], [], "test");

    private static LiveEvaluationCaseResult SuccessfulResult(
        string caseId,
        int run,
        IReadOnlyList<RecommendationDecision> decisions,
        IReadOnlyList<string> entities)
    {
        var analysis = InitiativeAnalysisTestData.Analysis() with
        {
            RelevantEntityIds = entities,
            Recommendations = decisions.Select(value => InitiativeAnalysisTestData.Recommendation(value, [])).ToArray()
        };
        var governed = new PolicyComplianceValidator().Evaluate(analysis.Recommendations.Select(recommendation =>
            new ValidatedRecommendation(recommendation, EvidenceValidationStatus.Validated, [], [])).ToArray());
        return new LiveEvaluationCaseResult(
            caseId, run, LiveEvaluationExecutionStatus.Succeeded, "initiative.md", "hash",
            Understanding("business"), null, null, null, null, analysis,
            governed.Recommendations, governed.Outcome, null, null, [], [], null, null,
            new LiveHumanReview(null, null, null, null, null, null, null), []);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "EngineeringBrain.sln"))) current = current.Parent;
        return Path.Combine([current!.FullName, .. parts]);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root, string suitePath, ProjectMemorySyncResult memory, LiveEvaluationSuite suite)
        {
            Root = root;
            SuitePath = suitePath;
            Memory = memory;
            Suite = suite;
        }

        public string Root { get; }
        public string SuitePath { get; }
        public ProjectMemorySyncResult Memory { get; }
        public LiveEvaluationSuite Suite { get; }

        public static async Task<Fixture> CreateAsync(
            IReadOnlyList<LiveEvaluationCase>? cases = null,
            string initiative = "Add business execution.")
        {
            var root = Path.Combine(Path.GetTempPath(), $"brain-live-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "initiative.md"), initiative);
            var suitePath = Path.Combine(root, "live-suite.json");
            await File.WriteAllTextAsync(suitePath, "{}");
            var memory = await new ProjectMemoryService(store: new LocalProjectMemoryStore(Path.Combine(root, "memory")))
                .SyncAsync(ProjectMemoryTestFactory.Create());
            return new Fixture(root, suitePath, memory, Suite(cases ?? [Case("case")]));
        }

        public Task<LiveEvaluationRun> RunAsync(Func<LiveEvaluationCase, int, IReasoningProvider> factory)
        {
            var service = new LiveEvaluationService(
                store: new LocalLiveEvaluationStore(Path.Combine(Root, "data")),
                clock: () => new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));
            return service.RunAsync(LiveEvaluationPlanner.Create(Suite), SuitePath, Memory,
                "Fake", factory, "model-a", "model-b", "low", "medium");
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    private sealed class ThrowingProvider(bool structured, int retries = 0) : IReasoningProvider
    {
        public string Name => "Fake";
        public Task<ReasoningResult<T>> GenerateStructuredAsync<T>(ApprovedReasoningRequest request, CancellationToken cancellationToken = default) =>
            throw new ReasoningProviderException(
                structured ? ReasoningProviderFailureCode.InvalidStructuredOutput : ReasoningProviderFailureCode.TransportFailure,
                request.Request.Stage,
                10,
                retries,
                30,
                10,
                5,
                2);
    }

    private sealed class RawThrowingProvider(string message) : IReasoningProvider
    {
        public string Name => "Fake";
        public Task<ReasoningResult<T>> GenerateStructuredAsync<T>(ApprovedReasoningRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);
    }

    private sealed class CountingProvider : IReasoningProvider
    {
        public string Name => "Fake";
        public int Calls { get; private set; }
        public Task<ReasoningResult<T>> GenerateStructuredAsync<T>(ApprovedReasoningRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("should not be called");
        }
    }

    private sealed class ScriptedProvider(InitiativeUnderstanding understanding, InitiativeAnalysis analysis) : IReasoningProvider
    {
        public string Name => "Fake";
        public int Calls { get; private set; }
        public Task<ReasoningResult<T>> GenerateStructuredAsync<T>(ApprovedReasoningRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            var raw = request.Request;
            object value = typeof(T) == typeof(InitiativeUnderstanding) ? understanding : analysis;
            return Task.FromResult(new ReasoningResult<T>((T)value,
                new ReasoningCallUsage(raw.Stage, Name, raw.Model, raw.EstimatedInputTokens,
                    raw.EstimatedInputTokens, 0, 10, 1, 0)));
        }
    }

    private sealed class FixedSecretValueSource(params string[] values) : IOutboundSecretValueSource
    {
        public IReadOnlySet<string> GetValues() => values.ToHashSet(StringComparer.Ordinal);
    }
}
