namespace MessageFlowAnalyzer.Core;

public interface IAnalyzerService
{
    Task AnalyzeAllRepositoriesAsync(
        string reposRootPath,
        bool exportJson,
        bool exportHtml,
        bool exportArango,
        bool includeDetails,
        bool hangfireOnly,
        bool excludeTests,
        bool useCecilForPublishers);
}