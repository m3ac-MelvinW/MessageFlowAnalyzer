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
    public class HybridAnalyzerService : IAnalyzerService
    {
        private readonly List<string> _testProjectIndicators = new()
        {
            ".Test", ".Tests", ".UnitTest", ".UnitTests",
            ".IntegrationTest", ".IntegrationTests",
            ".FunctionalTest", ".FunctionalTests",
            "Test.", "Tests.", ".Testing", ".Specs", ".Spec"
        };

        private readonly EventDefinitionAnalyzer _eventAnalyzer;
        private readonly CecilPublisherAnalyzer _cecilPublisherAnalyzer;
        private readonly PublisherAnalyzer _sourcePublisherAnalyzer;
        private readonly ConsumerAnalyzer _consumerAnalyzer;
        private readonly SubscriptionAnalyzer _subscriptionAnalyzer;
        private readonly ReportGenerator _reportGenerator;

        public HybridAnalyzerService()
        {
            _eventAnalyzer = new EventDefinitionAnalyzer();
            _cecilPublisherAnalyzer = new CecilPublisherAnalyzer();
            _sourcePublisherAnalyzer = new PublisherAnalyzer();
            _consumerAnalyzer = new ConsumerAnalyzer();
            _subscriptionAnalyzer = new SubscriptionAnalyzer();
            _reportGenerator = new ReportGenerator();
        }

        public async Task AnalyzeAllRepositoriesAsync(
            string reposRootPath, 
            bool exportJson = true, 
            bool exportHtml = false, 
            bool exportArango = false, 
            bool includeDetails = false, 
            bool hangfireOnly = false, 
            bool excludeTests = false,
            bool useCecilForPublishers = true)  // New option!
        {
            Console.WriteLine($"Analyzing message flows in: {reposRootPath}");
            Console.WriteLine($"Using {(useCecilForPublishers ? "Mono.Cecil" : "Source Parsing")} for publisher analysis");
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
                var repoResult = await AnalyzeRepositoryAsync(repo, includeDetails, excludeTests, useCecilForPublishers);
                
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
        }

        private async Task<MessageFlowReport> AnalyzeRepositoryAsync(string repoPath, bool includeDetails, bool excludeTests, bool useCecilForPublishers)
        {
            var report = new MessageFlowReport();
            var repoName = Path.GetFileName(repoPath);

            // Find all C# files for source-based analysis
            var allCsFiles = Directory.GetFiles(repoPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
                .ToList();

            var csFiles = excludeTests ? 
                allCsFiles.Where(f => !IsTestFile(f)).ToList() : 
                allCsFiles;

            // Find all assemblies for Cecil-based analysis
            var allDllFiles = Directory.GetFiles(repoPath, "*.dll", SearchOption.AllDirectories)
                .Where(f => !f.Contains("\\bin\\Debug\\") && !f.Contains("\\bin\\Release\\"))
                .Where(f => f.Contains("\\bin\\") || f.Contains("\\output\\"))
                .ToList();

            var dllFiles = excludeTests ?
                allDllFiles.Where(f => !IsTestAssembly(f)).ToList() :
                allDllFiles;

            Console.WriteLine($"  Found {csFiles.Count} C# files and {dllFiles.Count} assemblies");

            var allProjectFiles = Directory.GetFiles(repoPath, "*.csproj", SearchOption.AllDirectories);
            var projectFiles = excludeTests ? 
                allProjectFiles.Where(f => !IsTestProject(f)).ToArray() : 
                allProjectFiles;
            
            report.ProjectsScanned = projectFiles.Length;

            // Step 1: Find all IntegrationEvent definitions (Source-based - most reliable)
            foreach (var file in csFiles)
            {
                var events = await _eventAnalyzer.AnalyzeAsync(file, repoName);
                report.Events.AddRange(events);
            }
            Console.WriteLine($"  Found {report.Events.Count} integration events");

            // Step 2: Find all publishers - Choose approach
            if (useCecilForPublishers && dllFiles.Any())
            {
                Console.WriteLine("  Using Mono.Cecil for publisher analysis...");
                foreach (var dllFile in dllFiles)
                {
                    var publishers = await _cecilPublisherAnalyzer.AnalyzeAsync(dllFile, repoName, includeDetails);
                    report.Publishers.AddRange(publishers);
                }
            }
            else
            {
                Console.WriteLine("  Using source parsing for publisher analysis...");
                foreach (var file in csFiles)
                {
                    var publishers = await _sourcePublisherAnalyzer.AnalyzeAsync(file, repoName, includeDetails);
                    report.Publishers.AddRange(publishers);
                }
            }
            Console.WriteLine($"  Found {report.Publishers.Count} publishers");

            // Step 3: Find all consumers (Source-based - easier to find interfaces)
            foreach (var file in csFiles)
            {
                var consumers = await _consumerAnalyzer.AnalyzeAsync(file, repoName, includeDetails);
                report.Consumers.AddRange(consumers);
            }
            Console.WriteLine($"  Found {report.Consumers.Count} consumers");

            // Step 4: Find all event subscriptions (Source-based - easier to find DI registrations)
            foreach (var file in csFiles)
            {
                var subscriptions = await _subscriptionAnalyzer.AnalyzeAsync(file, repoName, includeDetails);
                report.Subscriptions.AddRange(subscriptions);
            }
            Console.WriteLine($"  Found {report.Subscriptions.Count} event subscriptions");

            return report;
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

        private bool IsTestAssembly(string assemblyPath)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
            return _testProjectIndicators.Any(indicator =>
                assemblyName.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }
    }
}