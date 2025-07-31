using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MessageFlowAnalyzer.Models;
using MessageFlowAnalyzer.Analyzers;
using MessageFlowAnalyzer.Exporters;
using MessageFlowAnalyzer.Reports;

namespace MessageFlowAnalyzer.Core
{
    public class AnalyzerService : IAnalyzerService
    {
        private readonly List<string> _testProjectIndicators = new()
        {
            ".Test",
            ".Tests", 
            ".UnitTest",
            ".UnitTests",
            ".IntegrationTest",
            ".IntegrationTests",
            ".FunctionalTest",
            ".FunctionalTests",
            "Test.",
            "Tests.",
            ".Testing",
            ".Specs",
            ".Spec"
        };

        private readonly EventDefinitionAnalyzer _eventAnalyzer;
        private readonly PublisherAnalyzer _publisherAnalyzer;
        private readonly ConsumerAnalyzer _consumerAnalyzer;
        private readonly SubscriptionAnalyzer _subscriptionAnalyzer;
        private readonly ReportGenerator _reportGenerator;

        public AnalyzerService()
        {
            _eventAnalyzer = new EventDefinitionAnalyzer();
            _publisherAnalyzer = new PublisherAnalyzer();
            _consumerAnalyzer = new ConsumerAnalyzer();
            _subscriptionAnalyzer = new SubscriptionAnalyzer();
            _reportGenerator = new ReportGenerator();
        }

        public async Task AnalyzeAllRepositoriesAsync(
            string reposRootPath, 
            bool exportJson = true, 
            bool exportHtml = false, 
            bool exportArango = false, 
            bool exportTinker = false,
            bool includeDetails = false, 
            bool hangfireOnly = false, 
            bool excludeTests = false,
            bool useCecilForPublishers = false)
        {
            Console.WriteLine($"Analyzing message flows in: {reposRootPath}");
            Console.WriteLine(new string('=', 80));

            var repositories = GetAllRepositories(reposRootPath);
            var report = new MessageFlowReport
            {
                AnalyzedAt = DateTime.Now,
                RepositoriesScanned = repositories.Count
            };

            foreach (var repo in repositories)
            {
                Console.WriteLine($"Processing repository: {Path.GetFileName(repo)}");
                var repoResult = await AnalyzeRepositoryAsync(repo, includeDetails, excludeTests);
                
                report.Events.AddRange(repoResult.Events);
                report.Publishers.AddRange(repoResult.Publishers);
                report.Consumers.AddRange(repoResult.Consumers);
                report.Subscriptions.AddRange(repoResult.Subscriptions);
                report.ProjectsScanned += repoResult.ProjectsScanned;
            }

            // Filter for Hangfire-only if requested
            if (hangfireOnly)
            {
                report.Publishers = report.Publishers.Where(p => p.IsInHangfireJob).ToList();
                report.Consumers = report.Consumers.Where(c => c.IsInHangfireJob).ToList();
            }

            // Generate comprehensive report
            _reportGenerator.GenerateMessageFlowReport(report);
            _reportGenerator.GeneratePublisherConsumerMatrix(report);
            _reportGenerator.GenerateHangfireReport(report);

            // Export reports
            if (exportJson)
            {
                var jsonExporter = new JsonExporter();
                await jsonExporter.ExportAsync(report, Path.Combine(reposRootPath, "message-flow-analysis.json"));
            }

            if (exportHtml)
            {
                var htmlExporter = new HtmlExporter();
                await htmlExporter.ExportAsync(report, Path.Combine(reposRootPath, "message-flow-analysis.html"));
            }

            if (exportArango)
            {
                var arangoExporter = new ArangoExporter();
                await arangoExporter.ExportAsync(report, Path.Combine(reposRootPath, "message-flow-arango.aql"));
            }
            
            if (exportTinker)
            {
                var tinkerExporter = new TinkerpopExporter();
                await tinkerExporter.ExportAsync(report, Path.Combine(reposRootPath, "message-flow-tinkerpop.gremlin"));
            }
            
        }

        private List<string> GetAllRepositories(string reposRootPath)
        {
            var repositories = new List<string>();
            
            foreach (var directory in Directory.GetDirectories(reposRootPath))
            {
                // Check if directory contains .NET projects or solutions
                if (Directory.GetFiles(directory, "*.sln", SearchOption.AllDirectories).Any() ||
                    Directory.GetFiles(directory, "*.csproj", SearchOption.AllDirectories).Any())
                {
                    repositories.Add(directory);
                }
            }

            Console.WriteLine($"Found {repositories.Count} repositories with .NET projects");
            return repositories;
        }

        private async Task<MessageFlowReport> AnalyzeRepositoryAsync(string repoPath, bool includeDetails, bool excludeTests)
        {
            var report = new MessageFlowReport();
            var repoName = Path.GetFileName(repoPath);

            // Find all C# files
            var allCsFiles = Directory.GetFiles(repoPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
                .ToList();

            // Filter out test files if requested
            var csFiles = excludeTests ? 
                allCsFiles.Where(f => !IsTestFile(f)).ToList() : 
                allCsFiles;

            var excludedCount = allCsFiles.Count - csFiles.Count;
            Console.WriteLine($"  Found {allCsFiles.Count} C# files ({excludedCount} test files {(excludeTests ? "excluded" : "included")})");

            // Find all project files to determine project boundaries
            var allProjectFiles = Directory.GetFiles(repoPath, "*.csproj", SearchOption.AllDirectories);
            var projectFiles = excludeTests ? 
                allProjectFiles.Where(f => !IsTestProject(f)).ToArray() : 
                allProjectFiles;
            
            report.ProjectsScanned = projectFiles.Length;

            // Step 1: Find all IntegrationEvent definitions
            foreach (var file in csFiles)
            {
                var events = await _eventAnalyzer.AnalyzeAsync(file, repoName);
                report.Events.AddRange(events);
            }

            Console.WriteLine($"  Found {report.Events.Count} integration events");

            // Step 2: Find all publishers (IMessagePublisher.Publish calls)
            foreach (var file in csFiles)
            {
                var publishers = await _publisherAnalyzer.AnalyzeAsync(file, repoName, includeDetails);
                report.Publishers.AddRange(publishers);
            }

            Console.WriteLine($"  Found {report.Publishers.Count} publishers");

            // Step 3: Find all consumers (IIntegrationEventHandler implementations)
            foreach (var file in csFiles)
            {
                var consumers = await _consumerAnalyzer.AnalyzeAsync(file, repoName, includeDetails);
                report.Consumers.AddRange(consumers);
            }

            Console.WriteLine($"  Found {report.Consumers.Count} consumers");

            // Step 4: Find all event subscriptions (service registrations)
            foreach (var file in csFiles)
            {
                var subscriptions = await _subscriptionAnalyzer.AnalyzeAsync(file, repoName, includeDetails);
                report.Subscriptions.AddRange(subscriptions);
            }

            Console.WriteLine($"  Found {report.Subscriptions.Count} event subscriptions");

            return report;
        }

        private bool IsTestProject(string projectPath)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var directoryName = Path.GetFileName(Path.GetDirectoryName(projectPath));
            
            return _testProjectIndicators.Any(indicator =>
                projectName.Contains(indicator, StringComparison.OrdinalIgnoreCase) ||
                directoryName.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsTestFile(string filePath)
        {
            // Check if file is in a test project
            var directory = Path.GetDirectoryName(filePath);
            while (!string.IsNullOrEmpty(directory))
            {
                var projectFiles = Directory.GetFiles(directory, "*.csproj");
                if (projectFiles.Length > 0 && IsTestProject(projectFiles[0]))
                {
                    return true;
                }

                // Also check directory names for test indicators
                var dirName = Path.GetFileName(directory);
                if (_testProjectIndicators.Any(indicator => 
                    dirName.Contains(indicator, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
                
                directory = Path.GetDirectoryName(directory);
            }

            // Check if the file itself has test indicators in name
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            return _testProjectIndicators.Any(indicator =>
                fileName.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }
    }
}