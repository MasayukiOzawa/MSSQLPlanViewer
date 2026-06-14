using MSSQLPlanViewer.Core.Diagnostics;
using MSSQLPlanViewer.Core.Diagnostics.Rules;
using MSSQLPlanViewer.Core.Models;
using MSSQLPlanViewer.Core.Parsing;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class PlanDiagnosticsServiceTests
{
    private readonly ShowplanParser parser = new();

    [Fact]
    public void Analyze_FindsDiagnosticsFromParsedSample()
    {
        var document = parser.Parse(SamplePlanLoader.Load("diagnostics-2022.sqlplan"));
        var diagnostics = CreateService().Analyze(document);
        var ruleIds = diagnostics.Select(diagnostic => diagnostic.RuleId).ToArray();

        Assert.Contains("CardinalityEstimateSkew", ruleIds);
        Assert.Contains("TempDbSpill", ruleIds);
        Assert.Contains("ExpensiveLookup", ruleIds);
        Assert.Contains("HighImpactMissingIndex", ruleIds);
        Assert.Contains("ImplicitConversion", ruleIds);
        Assert.Contains("MemoryGrantMismatch", ruleIds);
        Assert.Contains("StaleStatistics", ruleIds);
        Assert.Contains("LargeScanWithResidualPredicate", ruleIds);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.RuleId == "MemoryGrantMismatch"
            && diagnostic.Severity == PlanDiagnosticSeverity.Critical);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.RuleId == "LargeScanWithResidualPredicate"
            && diagnostic.NodeId == "1"
            && diagnostic.Severity == PlanDiagnosticSeverity.Warning);
    }

    [Fact]
    public void Analyze_FindsParallelThreadSkewFromParsedSample()
    {
        var document = parser.Parse(SamplePlanLoader.Load("parallel-skew-2022.sqlplan"));

        var diagnostics = CreateService().Analyze(document);

        var skew = Assert.Single(diagnostics, diagnostic => diagnostic.RuleId == "ParallelThreadSkew");
        Assert.Equal("1", skew.NodeId);
        Assert.Equal(PlanDiagnosticSeverity.Critical, skew.Severity);
        Assert.Contains(skew.Evidence, evidence => evidence.Name == "Worker threads" && evidence.Value == "4");
    }

    [Fact]
    public void CardinalityEstimateSkewRule_UsesActualExecutionsInEstimatedTotal()
    {
        var node = TestPlanFactory.Node(
            "1",
            estimatedRows: 10,
            runtimeMetrics: new PlanRuntimeMetrics(25_000, 5, null, null, null, null, null, null));
        var statement = TestPlanFactory.Statement(new[] { node });

        var diagnostic = Assert.Single(new CardinalityEstimateSkewRule().Evaluate(statement, new PlanDiagnosticOptions()));

        Assert.Equal(PlanDiagnosticSeverity.Critical, diagnostic.Severity);
        Assert.Contains(diagnostic.Evidence, evidence => evidence.Name == "Estimated total rows" && evidence.Value == "50");
    }

    [Fact]
    public void ExpensiveLookupRule_DetectsLookupAttribute()
    {
        var node = TestPlanFactory.Node(
            "2",
            physicalOp: "Clustered Index Seek",
            runtimeMetrics: new PlanRuntimeMetrics(10, 120_000, null, null, null, null, null, null),
            xmlAttributes: new[] { new PlanProperty("RelOp.IndexScan.Lookup", "1") });
        var statement = TestPlanFactory.Statement(new[] { node });

        var diagnostic = Assert.Single(new ExpensiveLookupRule().Evaluate(statement, new PlanDiagnosticOptions()));

        Assert.Equal(PlanDiagnosticSeverity.Critical, diagnostic.Severity);
        Assert.Equal("2", diagnostic.NodeId);
    }

    [Fact]
    public void TempDbSpillRule_UsesSpillLevelForCriticalSeverity()
    {
        var node = TestPlanFactory.Node(
            "1",
            physicalOp: "Sort",
            warnings: new PlanWarning("SpillToTempDb", null, "SpillLevel=2"));
        var statement = TestPlanFactory.Statement(new[] { node });

        var diagnostic = Assert.Single(new TempDbSpillRule().Evaluate(statement, new PlanDiagnosticOptions()));

        Assert.Equal(PlanDiagnosticSeverity.Critical, diagnostic.Severity);
        Assert.Equal("1", diagnostic.NodeId);
    }

    [Fact]
    public void ImplicitConversionRule_PrefersPlanAffectingConvertWarning()
    {
        var node = TestPlanFactory.Node(
            "1",
            physicalOp: "Index Scan",
            properties: new[] { new PlanProperty("Predicate", "CONVERT_IMPLICIT(int,[T].[C],0)=(1)") });
        var statement = TestPlanFactory.Statement(
            new[] { node },
            warnings: new[] { new PlanWarning("PlanAffectingConvert", "Seek Plan", null) });

        var diagnostic = Assert.Single(new ImplicitConversionRule().Evaluate(statement, new PlanDiagnosticOptions()));

        Assert.Null(diagnostic.NodeId);
        Assert.Contains("PlanAffectingConvert", diagnostic.Message);
    }

    [Fact]
    public void MemoryGrantMismatchRule_DetectsLowGrantUsage()
    {
        var summary = TestPlanFactory.EmptySummary with
        {
            MemoryGrantInfoProperties = new[]
            {
                new PlanProperty("GrantedMemory", "102400"),
                new PlanProperty("MaxUsedMemory", "2048"),
                new PlanProperty("GrantWaitTime", "0")
            }
        };
        var statement = TestPlanFactory.Statement(
            new[] { TestPlanFactory.Node("0") },
            summary: summary);

        var diagnostic = Assert.Single(new MemoryGrantMismatchRule().Evaluate(statement, new PlanDiagnosticOptions()));

        Assert.Equal(PlanDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Null(diagnostic.NodeId);
    }

    [Fact]
    public void HighImpactMissingIndexRule_UsesStatementLevelDiagnostic()
    {
        var summary = TestPlanFactory.EmptySummary with
        {
            MissingIndexesEntries = new[]
            {
                new MissingIndexEntry("[db].[dbo].[Orders]", "95", "[CustomerId]", "[OrderDate]", "[TotalDue]")
            }
        };
        var statement = TestPlanFactory.Statement(
            new[] { TestPlanFactory.Node("0") },
            summary: summary);

        var diagnostic = Assert.Single(new HighImpactMissingIndexRule().Evaluate(statement, new PlanDiagnosticOptions()));

        Assert.Null(diagnostic.NodeId);
        Assert.Equal(PlanDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("EQUALITY", diagnostic.Recommendation);
    }

    [Fact]
    public void StaleStatisticsRule_DetectsHighModificationCountAndLowSampling()
    {
        var summary = TestPlanFactory.EmptySummary with
        {
            OptimizerStatsUsageEntries = new[]
            {
                new OptimizerStatsUsageEntry(
                    Database: "[db]",
                    Schema: "[dbo]",
                    Table: "[Orders]",
                    Statistics: "[IX_Orders_Date]",
                    LastUpdate: null,
                    StatisticsModificationCount: "500000",
                    LastSample: null,
                    SamplingPercent: "5",
                    Steps: null,
                    Rows: "2000000",
                    UnfilteredRows: null,
                    PersistedSamplePercent: null)
            }
        };
        var statement = TestPlanFactory.Statement(
            new[] { TestPlanFactory.Node("0") },
            summary: summary);

        var diagnostics = new StaleStatisticsRule()
            .Evaluate(statement, new PlanDiagnosticOptions())
            .ToArray();

        Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == PlanDiagnosticSeverity.Warning);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == PlanDiagnosticSeverity.Info);
    }

    [Fact]
    public void LargeScanWithResidualPredicateRule_UsesRowsReadRatioForWarning()
    {
        var metrics = new PlanRuntimeMetrics(50_000, 1, null, null, null, null, null, null)
        {
            Threads = new[]
            {
                new PlanThreadRuntimeMetrics(0, 50_000, 1_000_000, 1, null, null, null, null, null, null)
            }
        };
        var node = TestPlanFactory.Node(
            "1",
            physicalOp: "Index Scan",
            runtimeMetrics: metrics,
            properties: new[] { new PlanProperty("Predicate", "[Orders].[Status]=(1)") });
        var statement = TestPlanFactory.Statement(new[] { node });

        var diagnostic = Assert.Single(new LargeScanWithResidualPredicateRule().Evaluate(statement, new PlanDiagnosticOptions()));

        Assert.Equal(PlanDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("1", diagnostic.NodeId);
    }

    [Fact]
    public void ParallelThreadSkewRule_ExcludesCoordinatorThread()
    {
        var metrics = new PlanRuntimeMetrics(100_000, 4, null, null, null, null, null, null)
        {
            Threads = new[]
            {
                new PlanThreadRuntimeMetrics(0, 0, null, null, null, null, null, null, null, null),
                new PlanThreadRuntimeMetrics(1, 90_000, null, null, null, null, null, null, null, null),
                new PlanThreadRuntimeMetrics(2, 3_000, null, null, null, null, null, null, null, null),
                new PlanThreadRuntimeMetrics(3, 3_500, null, null, null, null, null, null, null, null),
                new PlanThreadRuntimeMetrics(4, 3_500, null, null, null, null, null, null, null, null)
            }
        };
        var node = TestPlanFactory.Node("1", physicalOp: "Index Scan", runtimeMetrics: metrics);
        var statement = TestPlanFactory.Statement(new[] { node });

        var diagnostic = Assert.Single(new ParallelThreadSkewRule().Evaluate(statement, new PlanDiagnosticOptions()));

        Assert.Equal(PlanDiagnosticSeverity.Critical, diagnostic.Severity);
        Assert.Contains(diagnostic.Evidence, evidence => evidence.Name == "Worker threads" && evidence.Value == "4");
    }

    private static PlanDiagnosticsService CreateService() =>
        new(new IPlanDiagnosticRule[]
        {
            new CardinalityEstimateSkewRule(),
            new TempDbSpillRule(),
            new ExpensiveLookupRule(),
            new HighImpactMissingIndexRule(),
            new ImplicitConversionRule(),
            new MemoryGrantMismatchRule(),
            new StaleStatisticsRule(),
            new LargeScanWithResidualPredicateRule(),
            new ParallelThreadSkewRule()
        });
}
