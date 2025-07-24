using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text;

public class MessageFlowAnalyzer
{
    public class MessageEventDefinition
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public string FilePath { get; set; }
        public string Repository { get; set; }
        public string Project { get; set; }
        public List<string> Properties { get; set; } = new();
        public string MessageDataClass { get; set; }
        public List<string> BaseProperties { get; set; } = new();
    }

    public class MessagePublisher
    {
        public string EventName { get; set; }
        public string Repository { get; set; }
        public string Project { get; set; }
        public string FilePath { get; set; }
        public string ClassName { get; set; }
        public string MethodName { get; set; }
        public int LineNumber { get; set; }
        public string CodeContext { get; set; }
        public bool IsInHangfireJob { get; set; }
        public string HangfireJobClass { get; set; }
    }

    public class MessageEventSubscription
    {
        public string EventName { get; set; }
        public string Repository { get; set; }
        public string Project { get; set; }
        public string FilePath { get; set; }
        public string SubscriptionType { get; set; } // "ServiceCollection", "EventBus", etc.
        public int LineNumber { get; set; }
        public string CodeContext { get; set; }
        public bool IsInHangfireJob { get; set; }
    }

    public class MessageConsumer
    {
        public string EventName { get; set; }
        public string Repository { get; set; }
        public string Project { get; set; }
        public string FilePath { get; set; }
        public string HandlerClass { get; set; }
        public string HandlerMethod { get; set; }
        public bool IsInHangfireJob { get; set; }
        public List<string> HandlerLogic { get; set; } = new();
    }

    public class MessageFlowReport
    {
        public List<MessageEventDefinition> Events { get; set; } = new();
        public List<MessagePublisher> Publishers { get; set; } = new();
        public List<MessageConsumer> Consumers { get; set; } = new();
        public List<MessageEventSubscription> Subscriptions { get; set; } = new();
        public DateTime AnalyzedAt { get; set; }
        public int RepositoriesScanned { get; set; }
        public int ProjectsScanned { get; set; }
    }

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

    private readonly List<string> _hangfireIndicators = new()
    {
        "BackgroundJob",
        "RecurringJob",
        "[AutomaticRetry]",
        "[Queue(",
        "IJob",
        "[JobDisplayName"
    };

    private readonly List<string> _csharpFileExtensions = new()
    {
        "*.cs"
    };

    public static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: MessageFlowAnalyzer.exe <repos-root-path>");
            Console.WriteLine("  Options:");
            Console.WriteLine("    --export-json          Export results to JSON");
            Console.WriteLine("    --export-html          Export results to HTML");
            Console.WriteLine("    --export-cypher        Export Neo4j Cypher script");
            Console.WriteLine("    --export-arango        Export ArangoDB AQL script");
            Console.WriteLine("    --include-details      Include detailed code context");
            Console.WriteLine("    --hangfire-only        Only show Hangfire-related messages");
            Console.WriteLine("    --exclude-tests        Exclude test projects from analysis");
            return;
        }

        string reposRootPath = args[0];
        bool exportJson = args.Contains("--export-json");
        bool exportHtml = args.Contains("--export-html");
        bool exportCypher = args.Contains("--export-cypher");
        bool exportArango = args.Contains("--export-arango");
        bool includeDetails = args.Contains("--include-details");
        bool hangfireOnly = args.Contains("--hangfire-only");
        bool excludeTests = args.Contains("--exclude-tests");

        var analyzer = new MessageFlowAnalyzer();
        await analyzer.AnalyzeAllRepositoriesAsync(reposRootPath, exportJson, exportHtml, exportCypher, exportArango, includeDetails, hangfireOnly, excludeTests);
    }

    public async Task AnalyzeAllRepositoriesAsync(string reposRootPath, bool exportJson = true, bool exportHtml = false, bool exportCypher = false, bool exportArango = false, bool includeDetails = false, bool hangfireOnly = false, bool excludeTests = false)
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
        GenerateMessageFlowReport(report);
        GeneratePublisherConsumerMatrix(report);
        GenerateHangfireReport(report);

        if (exportJson)
        {
            await ExportToJsonAsync(report, Path.Combine(reposRootPath, "message-flow-analysis.json"));
        }

        if (exportHtml)
        {
            await ExportToHtmlAsync(report, Path.Combine(reposRootPath, "message-flow-analysis.html"));
        }

        if (exportCypher)
        {
            await ExportToCypherAsync(report, Path.Combine(reposRootPath, "message-flow-neo4j.cypher"));
        }

        if (exportArango)
        {
            await ExportToArangoAsync(report, Path.Combine(reposRootPath, "message-flow-arango.aql"));
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
            var events = await AnalyzeEventDefinitionsAsync(file, repoName);
            report.Events.AddRange(events);
        }

        Console.WriteLine($"  Found {report.Events.Count} integration events");

        // Step 2: Find all publishers (IMessagePublisher.Publish calls)
        foreach (var file in csFiles)
        {
            var publishers = await AnalyzePublishersAsync(file, repoName, includeDetails);
            report.Publishers.AddRange(publishers);
        }

        Console.WriteLine($"  Found {report.Publishers.Count} publishers");

        // Step 3: Find all consumers (IIntegrationEventHandler implementations)
        foreach (var file in csFiles)
        {
            var consumers = await AnalyzeConsumersAsync(file, repoName, includeDetails);
            report.Consumers.AddRange(consumers);
        }

        Console.WriteLine($"  Found {report.Consumers.Count} consumers");

        // Step 4: Find all event subscriptions (service registrations)
        foreach (var file in csFiles)
        {
            var subscriptions = await AnalyzeEventSubscriptionsAsync(file, repoName, includeDetails);
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

    private async Task<List<MessageEventDefinition>> AnalyzeEventDefinitionsAsync(string filePath, string repoName)
    {
        var events = new List<MessageEventDefinition>();
        var content = await File.ReadAllTextAsync(filePath);
        var lines = content.Split('\n');

        // Look for classes that inherit from IntegrationEvent
        var classRegex = new Regex(@"public\s+class\s+(\w+)\s*:\s*IntegrationEvent", RegexOptions.IgnoreCase);
        var namespaceRegex = new Regex(@"namespace\s+([\w\.]+)");
        var propertyRegex = new Regex(@"public\s+(\w+(?:<.*?>)?)\s+(\w+)\s*{\s*get", RegexOptions.IgnoreCase);

        string currentNamespace = "";
        string currentClass = "";
        var currentProperties = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Track namespace
            var nsMatch = namespaceRegex.Match(line);
            if (nsMatch.Success)
            {
                currentNamespace = nsMatch.Groups[1].Value;
            }

            // Find IntegrationEvent classes
            var classMatch = classRegex.Match(line);
            if (classMatch.Success)
            {
                currentClass = classMatch.Groups[1].Value;
                currentProperties.Clear();

                // Look ahead to find properties and nested message data class
                string messageDataClass = null;
                for (int j = i + 1; j < lines.Length && j < i + 50; j++)
                {
                    var nextLine = lines[j].Trim();
                    
                    if (nextLine.StartsWith("}") && !nextLine.Contains("{"))
                        break;

                    // Find properties
                    var propMatch = propertyRegex.Match(nextLine);
                    if (propMatch.Success)
                    {
                        currentProperties.Add($"{propMatch.Groups[1].Value} {propMatch.Groups[2].Value}");
                    }

                    // Look for nested message data class
                    if (nextLine.Contains("public class") && nextLine.Contains("Data"))
                    {
                        var dataClassMatch = Regex.Match(nextLine, @"public\s+class\s+(\w+)");
                        if (dataClassMatch.Success)
                        {
                            messageDataClass = dataClassMatch.Groups[1].Value;
                        }
                    }
                }

                events.Add(new MessageEventDefinition
                {
                    Name = currentClass,
                    FullName = $"{currentNamespace}.{currentClass}",
                    FilePath = filePath,
                    Repository = repoName,
                    Project = GetProjectNameFromPath(filePath),
                    Properties = new List<string>(currentProperties),
                    MessageDataClass = messageDataClass,
                    BaseProperties = new List<string> { "Id", "CreatedAt", "Version" } // Common IntegrationEvent properties
                });
            }
        }

        return events;
    }

    private async Task<List<MessagePublisher>> AnalyzePublishersAsync(string filePath, string repoName, bool includeDetails)
    {
        var publishers = new List<MessagePublisher>();
        var content = await File.ReadAllTextAsync(filePath);
        var lines = content.Split('\n');

        var publishRegex = new Regex(@"_messagePublisher\.Publish\s*\(\s*(\w+)\s*\)", RegexOptions.IgnoreCase);
        var publishRegex2 = new Regex(@"\.Publish\s*\(\s*new\s+(\w+IntegrationEvent)\s*\(", RegexOptions.IgnoreCase);
        var classRegex = new Regex(@"public\s+(?:class|interface)\s+(\w+)");

        string currentClass = "";
        string currentMethod = "";
        bool isInHangfireJob = IsHangfireRelated(content);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Track current class
            var classMatch = classRegex.Match(line);
            if (classMatch.Success)
            {
                currentClass = classMatch.Groups[1].Value;
            }

            // Track current method
            if (line.Contains("public") && (line.Contains("async") || line.Contains("Task") || line.Contains("void")))
            {
                var methodMatch = Regex.Match(line, @"(?:public|private|protected)\s+(?:async\s+)?(?:Task<?.*?>?|void)\s+(\w+)\s*\(");
                if (methodMatch.Success)
                {
                    currentMethod = methodMatch.Groups[1].Value;
                }
            }

            // Find publish calls
            var publishMatch = publishRegex.Match(line);
            var publishMatch2 = publishRegex2.Match(line);

            if (publishMatch.Success || publishMatch2.Success)
            {
                string eventName = publishMatch.Success ? 
                    ExtractEventNameFromVariable(lines, i, publishMatch.Groups[1].Value) :
                    publishMatch2.Groups[1].Value;

                var context = includeDetails ? GetCodeContext(lines, i, 3) : line.Trim();

                publishers.Add(new MessagePublisher
                {
                    EventName = eventName,
                    Repository = repoName,
                    Project = GetProjectNameFromPath(filePath),
                    FilePath = filePath,
                    ClassName = currentClass,
                    MethodName = currentMethod,
                    LineNumber = i + 1,
                    CodeContext = context,
                    IsInHangfireJob = isInHangfireJob,
                    HangfireJobClass = isInHangfireJob ? currentClass : null
                });
            }
        }

        return publishers;
    }

    private async Task<List<MessageConsumer>> AnalyzeConsumersAsync(string filePath, string repoName, bool includeDetails)
    {
        var consumers = new List<MessageConsumer>();
        var content = await File.ReadAllTextAsync(filePath);
        var lines = content.Split('\n');

        // Only look for actual IIntegrationEventHandler implementations, not service registrations
        var handlerRegex = new Regex(@"IIntegrationEventHandler<(\w+)>", RegexOptions.IgnoreCase);
        var classRegex = new Regex(@"public\s+class\s+(\w+)");
        var handleMethodRegex = new Regex(@"public\s+(?:async\s+)?(?:Task|void)\s+Handle\s*\(");

        // Skip files that are clearly startup/configuration files
        var fileName = Path.GetFileName(filePath).ToLower();
        if (fileName.Contains("startup") || fileName.Contains("program") || fileName.Contains("configuration"))
        {
            return consumers; // Don't analyze these as consumers
        }

        string currentClass = "";
        bool isInHangfireJob = IsHangfireRelated(content);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Find handler implementations (actual classes, not registrations)
            var handlerMatch = handlerRegex.Match(line);
            if (handlerMatch.Success)
            {
                var eventName = handlerMatch.Groups[1].Value;
                
                // Find the class name (look backwards or forwards)
                for (int j = Math.Max(0, i - 5); j <= Math.Min(lines.Length - 1, i + 5); j++)
                {
                    var classMatch = classRegex.Match(lines[j]);
                    if (classMatch.Success)
                    {
                        currentClass = classMatch.Groups[1].Value;
                        break;
                    }
                }

                // Only add if this is an actual class implementation, not a service registration
                if (!string.IsNullOrEmpty(currentClass) && !IsServiceRegistrationContext(lines, i))
                {
                    // Find the Handle method and extract some logic
                    var handlerLogic = new List<string>();
                    if (includeDetails)
                    {
                        for (int j = i; j < lines.Length && j < i + 50; j++)
                        {
                            if (handleMethodRegex.Match(lines[j]).Success)
                            {
                                // Extract next 10-15 lines of the Handle method
                                for (int k = j + 1; k < lines.Length && k < j + 15; k++)
                                {
                                    var logicLine = lines[k].Trim();
                                    if (logicLine.StartsWith("}") && !logicLine.Contains("{"))
                                        break;
                                    if (!string.IsNullOrWhiteSpace(logicLine))
                                        handlerLogic.Add(logicLine);
                                }
                                break;
                            }
                        }
                    }

                    consumers.Add(new MessageConsumer
                    {
                        EventName = eventName,
                        Repository = repoName,
                        Project = GetProjectNameFromPath(filePath),
                        FilePath = filePath,
                        HandlerClass = currentClass,
                        HandlerMethod = "Handle",
                        IsInHangfireJob = isInHangfireJob,
                        HandlerLogic = handlerLogic
                    });
                }
            }
        }

        return consumers;
    }

    private async Task<List<MessageEventSubscription>> AnalyzeEventSubscriptionsAsync(string filePath, string repoName, bool includeDetails)
    {
        var subscriptions = new List<MessageEventSubscription>();
        var content = await File.ReadAllTextAsync(filePath);
        var lines = content.Split('\n');

        // Look for service collection registrations and event bus subscriptions
        var serviceRegistrationRegex = new Regex(@"\.AddTransient<IIntegrationEventHandler<(\w+)>", RegexOptions.IgnoreCase);
        var serviceRegistrationRegex2 = new Regex(@"\.AddScoped<IIntegrationEventHandler<(\w+)>", RegexOptions.IgnoreCase);
        var serviceRegistrationRegex3 = new Regex(@"\.AddSingleton<IIntegrationEventHandler<(\w+)>", RegexOptions.IgnoreCase);
        var eventBusSubscriptionRegex = new Regex(@"\.Subscribe<(\w+)>", RegexOptions.IgnoreCase);
        var eventBusSubscriptionRegex2 = new Regex(@"\.SubscribeAsync<(\w+)>", RegexOptions.IgnoreCase);

        bool isInHangfireJob = IsHangfireRelated(content);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Check for service collection registrations
            var serviceMatch = serviceRegistrationRegex.Match(line) ?? 
                              serviceRegistrationRegex2.Match(line) ?? 
                              serviceRegistrationRegex3.Match(line);
            
            if (serviceMatch != null && serviceMatch.Success)
            {
                var eventName = serviceMatch.Groups[1].Value;
                var context = includeDetails ? GetCodeContext(lines, i, 2) : line.Trim();

                subscriptions.Add(new MessageEventSubscription
                {
                    EventName = eventName,
                    Repository = repoName,
                    Project = GetProjectNameFromPath(filePath),
                    FilePath = filePath,
                    SubscriptionType = "ServiceCollection",
                    LineNumber = i + 1,
                    CodeContext = context,
                    IsInHangfireJob = isInHangfireJob
                });
            }

            // Check for event bus subscriptions
            var eventBusMatch = eventBusSubscriptionRegex.Match(line) ?? 
                               eventBusSubscriptionRegex2.Match(line);
            
            if (eventBusMatch != null && eventBusMatch.Success)
            {
                var eventName = eventBusMatch.Groups[1].Value;
                var context = includeDetails ? GetCodeContext(lines, i, 2) : line.Trim();

                subscriptions.Add(new MessageEventSubscription
                {
                    EventName = eventName,
                    Repository = repoName,
                    Project = GetProjectNameFromPath(filePath),
                    FilePath = filePath,
                    SubscriptionType = "EventBus",
                    LineNumber = i + 1,
                    CodeContext = context,
                    IsInHangfireJob = isInHangfireJob
                });
            }
        }

        return subscriptions;
    }

    private bool IsServiceRegistrationContext(string[] lines, int currentIndex)
    {
        // Check if we're in a service registration context (like Startup.cs ConfigureServices method)
        for (int i = Math.Max(0, currentIndex - 10); i <= Math.Min(lines.Length - 1, currentIndex + 3); i++)
        {
            var line = lines[i].ToLower();
            if (line.Contains("configureservices") || 
                line.Contains("addtransient") || 
                line.Contains("addscoped") || 
                line.Contains("addsingleton") ||
                line.Contains("services."))
            {
                return true;
            }
        }
        return false;
    }

    private string ExtractEventNameFromVariable(string[] lines, int publishLineIndex, string variableName)
    {
        // Look backwards to find where the variable was declared
        for (int i = publishLineIndex - 1; i >= Math.Max(0, publishLineIndex - 20); i--)
        {
            var line = lines[i];
            if (line.Contains($"new ") && line.Contains(variableName))
            {
                var match = Regex.Match(line, @"new\s+(\w+IntegrationEvent)");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            if (line.Contains($"{variableName} =") || line.Contains($"var {variableName}"))
            {
                var match = Regex.Match(line, @"new\s+(\w+IntegrationEvent)");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }
        return $"Unknown({variableName})";
    }

    private string GetCodeContext(string[] lines, int centerIndex, int contextLines)
    {
        var start = Math.Max(0, centerIndex - contextLines);
        var end = Math.Min(lines.Length - 1, centerIndex + contextLines);
        
        var context = new List<string>();
        for (int i = start; i <= end; i++)
        {
            var prefix = i == centerIndex ? ">>> " : "    ";
            context.Add($"{prefix}{lines[i].Trim()}");
        }
        return string.Join("\n", context);
    }

    private bool IsHangfireRelated(string content)
    {
        return _hangfireIndicators.Any(indicator => 
            content.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private string GetProjectNameFromPath(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(directory))
        {
            var projectFiles = Directory.GetFiles(directory, "*.csproj");
            if (projectFiles.Length > 0)
            {
                return Path.GetFileNameWithoutExtension(projectFiles[0]);
            }
            directory = Path.GetDirectoryName(directory);
        }
        return "Unknown";
    }

    private void GenerateMessageFlowReport(MessageFlowReport report)
    {
        Console.WriteLine("\nMESSAGE FLOW ANALYSIS REPORT:");
        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"Repositories Scanned: {report.RepositoriesScanned}");
        Console.WriteLine($"Projects Scanned: {report.ProjectsScanned}");
        Console.WriteLine($"Integration Events Found: {report.Events.Count}");
        Console.WriteLine($"Publishers Found: {report.Publishers.Count}");
        Console.WriteLine($"Consumers Found: {report.Consumers.Count}");
        Console.WriteLine($"Subscriptions Found: {report.Subscriptions.Count}");
        Console.WriteLine();

        // Group events with their publishers and consumers
        foreach (var evt in report.Events.OrderBy(e => e.Name))
        {
            var publishers = report.Publishers.Where(p => 
                p.EventName.Equals(evt.Name, StringComparison.OrdinalIgnoreCase) ||
                p.EventName.Contains(evt.Name)).ToList();
            
            var consumers = report.Consumers.Where(c => 
                c.EventName.Equals(evt.Name, StringComparison.OrdinalIgnoreCase) ||
                c.EventName.Contains(evt.Name)).ToList();

            var subscriptions = report.Subscriptions.Where(s => 
                s.EventName.Equals(evt.Name, StringComparison.OrdinalIgnoreCase) ||
                s.EventName.Contains(evt.Name)).ToList();

            Console.WriteLine($"[EVENT] {evt.Name}");
            Console.WriteLine($"   Repository: {evt.Repository}");
            Console.WriteLine($"   Project: {evt.Project}");
            if (!string.IsNullOrEmpty(evt.MessageDataClass))
            {
                Console.WriteLine($"   Message Data Class: {evt.MessageDataClass}");
            }
            
            if (publishers.Any())
            {
                Console.WriteLine($"   [PUBLISHED BY]:");
                foreach (var pub in publishers)
                {
                    var hangfireInfo = pub.IsInHangfireJob ? " [HANGFIRE]" : "";
                    Console.WriteLine($"      - {pub.Repository}/{pub.Project} - {pub.ClassName}.{pub.MethodName}(){hangfireInfo}");
                }
            }
            else
            {
                Console.WriteLine($"   [PUBLISHED BY]: *** NO PUBLISHERS FOUND ***");
            }

            if (consumers.Any())
            {
                Console.WriteLine($"   [CONSUMED BY]:");
                foreach (var cons in consumers)
                {
                    var hangfireInfo = cons.IsInHangfireJob ? " [HANGFIRE]" : "";
                    var handlerName = string.IsNullOrEmpty(cons.HandlerClass) ? "Unknown Handler" : cons.HandlerClass;
                    Console.WriteLine($"      - {cons.Repository}/{cons.Project} - {handlerName}{hangfireInfo}");
                }
            }
            else
            {
                Console.WriteLine($"   [CONSUMED BY]: *** NO CONSUMERS FOUND ***");
            }

            if (subscriptions.Any())
            {
                Console.WriteLine($"   [SUBSCRIPTIONS]:");
                foreach (var sub in subscriptions)
                {
                    var hangfireInfo = sub.IsInHangfireJob ? " [HANGFIRE]" : "";
                    Console.WriteLine($"      - {sub.Repository}/{sub.Project} - {sub.SubscriptionType}{hangfireInfo}");
                }
            }
            Console.WriteLine();
        }
    }

    private void GeneratePublisherConsumerMatrix(MessageFlowReport report)
    {
        Console.WriteLine("\nPUBLISHER-CONSUMER MATRIX:");
        Console.WriteLine(new string('=', 80));

        var allEventNames = report.Events.Select(e => e.Name)
            .Union(report.Publishers.Select(p => p.EventName))
            .Union(report.Consumers.Select(c => c.EventName))
            .Distinct()
            .OrderBy(name => name);

        foreach (var eventName in allEventNames)
        {
            var publishers = report.Publishers.Where(p => 
                p.EventName.Contains(eventName, StringComparison.OrdinalIgnoreCase)).ToList();
            var consumers = report.Consumers.Where(c => 
                c.EventName.Contains(eventName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (publishers.Any() || consumers.Any())
            {
                Console.WriteLine($"\n[FLOW] {eventName}:");
                
                // Show all publishing locations
                if (publishers.Any())
                {
                    var publishingRepos = publishers.GroupBy(p => p.Repository);
                    Console.WriteLine($"   Publishers ({publishers.Count}):");
                    foreach (var repo in publishingRepos)
                    {
                        Console.WriteLine($"      Repository: {repo.Key}");
                        foreach (var pub in repo)
                        {
                            var hangfire = pub.IsInHangfireJob ? " [HF]" : "";
                            Console.WriteLine($"         - {pub.Project}/{pub.ClassName}.{pub.MethodName}(){hangfire}");
                        }
                    }
                }

                // Show all consuming locations
                if (consumers.Any())
                {
                    var consumingRepos = consumers.GroupBy(c => c.Repository);
                    Console.WriteLine($"   Consumers ({consumers.Count}):");
                    foreach (var repo in consumingRepos)
                    {
                        Console.WriteLine($"      Repository: {repo.Key}");
                        foreach (var cons in repo)
                        {
                            var hangfire = cons.IsInHangfireJob ? " [HF]" : "";
                            var handlerName = string.IsNullOrEmpty(cons.HandlerClass) ? "Unknown Handler" : cons.HandlerClass;
                            Console.WriteLine($"         - {cons.Project}/{handlerName}{hangfire}");
                        }
                    }
                }

                // Highlight orphaned events
                if (!publishers.Any())
                {
                    Console.WriteLine($"   *** WARNING: No publishers found - orphaned event? ***");
                }
                if (!consumers.Any())
                {
                    Console.WriteLine($"   *** WARNING: No consumers found - dead letter? ***");
                }
            }
        }
    }

    private void GenerateHangfireReport(MessageFlowReport report)
    {
        Console.WriteLine("\nHANGFIRE MESSAGE ANALYSIS:");
        Console.WriteLine(new string('=', 80));

        var hangfirePublishers = report.Publishers.Where(p => p.IsInHangfireJob).ToList();
        var hangfireConsumers = report.Consumers.Where(c => c.IsInHangfireJob).ToList();

        Console.WriteLine($"Hangfire Publishers: {hangfirePublishers.Count}");
        Console.WriteLine($"Hangfire Consumers: {hangfireConsumers.Count}");
        Console.WriteLine();

        if (hangfirePublishers.Any())
        {
            Console.WriteLine("[HANGFIRE PUBLISHERS]:");
            foreach (var pub in hangfirePublishers.GroupBy(p => p.EventName))
            {
                Console.WriteLine($"   Event: {pub.Key}");
                foreach (var publisher in pub)
                {
                    Console.WriteLine($"      - {publisher.Repository}/{publisher.Project} - {publisher.HangfireJobClass}");
                }
            }
            Console.WriteLine();
        }

        if (hangfireConsumers.Any())
        {
            Console.WriteLine("[HANGFIRE CONSUMERS]:");
            foreach (var cons in hangfireConsumers.GroupBy(c => c.EventName))
            {
                Console.WriteLine($"   Event: {cons.Key}");
                foreach (var consumer in cons)
                {
                    var handlerName = string.IsNullOrEmpty(consumer.HandlerClass) ? "Unknown Handler" : consumer.HandlerClass;
                    Console.WriteLine($"      - {consumer.Repository}/{consumer.Project} - {handlerName}");
                }
            }
        }

        if (!hangfirePublishers.Any() && !hangfireConsumers.Any())
        {
            Console.WriteLine("No Hangfire-related message flows detected");
        }
    }

    private async Task ExportToArangoAsync(MessageFlowReport report, string outputPath)
    {
        var aql = GenerateArangoScript(report);
        await File.WriteAllTextAsync(outputPath, aql);
        Console.WriteLine($"\nArangoDB AQL script exported to: {outputPath}");
    }

    private string GenerateArangoScript(MessageFlowReport report)
    {
        var aql = new StringBuilder();
        
        // Header comments
        aql.AppendLine("// Message Flow Analysis - ArangoDB AQL Script");
        aql.AppendLine($"// Generated: {report.AnalyzedAt:yyyy-MM-dd HH:mm:ss}");
        aql.AppendLine($"// Repositories: {report.RepositoriesScanned}, Events: {report.Events.Count}, Publishers: {report.Publishers.Count}, Consumers: {report.Consumers.Count}, Subscriptions: {report.Subscriptions.Count}");
        aql.AppendLine();
        aql.AppendLine("// Instructions:");
        aql.AppendLine("// 1. Create a new database in ArangoDB (e.g., 'messageflow')");
        aql.AppendLine("// 2. Run each section in the ArangoDB Web Interface");
        aql.AppendLine("// 3. Use the Graph viewer to visualize relationships");
        aql.AppendLine();

        // Create collections
        aql.AppendLine("// ===== CREATE COLLECTIONS =====");
        aql.AppendLine("// Run these commands in the ArangoDB shell or web interface");
        aql.AppendLine();
        aql.AppendLine("// Create document collections (nodes)");
        aql.AppendLine("db._createDocumentCollection('repositories');");
        aql.AppendLine("db._createDocumentCollection('services');");
        aql.AppendLine("db._createDocumentCollection('events');");
        aql.AppendLine("db._createDocumentCollection('publishers');");
        aql.AppendLine("db._createDocumentCollection('consumers');");
        aql.AppendLine("db._createDocumentCollection('subscriptions');");
        aql.AppendLine();
        aql.AppendLine("// Create edge collections (relationships)");
        aql.AppendLine("db._createEdgeCollection('contains');      // Repository -> Service");
        aql.AppendLine("db._createEdgeCollection('defines');       // Service -> Event");
        aql.AppendLine("db._createEdgeCollection('publishes');     // Publisher -> Event");
        aql.AppendLine("db._createEdgeCollection('consumes');      // Consumer -> Event");
        aql.AppendLine("db._createEdgeCollection('subscribes');    // Subscription -> Event");
        aql.AppendLine("db._createEdgeCollection('hasPublisher');  // Service -> Publisher");
        aql.AppendLine("db._createEdgeCollection('hasConsumer');   // Service -> Consumer");
        aql.AppendLine("db._createEdgeCollection('hasSubscription'); // Service -> Subscription");
        aql.AppendLine();

        // Clear existing data
        aql.AppendLine("// ===== CLEAR EXISTING DATA (OPTIONAL) =====");
        aql.AppendLine("// Uncomment to clear existing data");
        aql.AppendLine("// FOR doc IN repositories REMOVE doc IN repositories");
        aql.AppendLine("// FOR doc IN services REMOVE doc IN services");
        aql.AppendLine("// FOR doc IN events REMOVE doc IN events");
        aql.AppendLine("// FOR doc IN publishers REMOVE doc IN publishers");
        aql.AppendLine("// FOR doc IN consumers REMOVE doc IN consumers");
        aql.AppendLine("// FOR doc IN subscriptions REMOVE doc IN subscriptions");
        aql.AppendLine("// FOR doc IN contains REMOVE doc IN contains");
        aql.AppendLine("// FOR doc IN defines REMOVE doc IN defines");
        aql.AppendLine("// FOR doc IN publishes REMOVE doc IN publishes");
        aql.AppendLine("// FOR doc IN consumes REMOVE doc IN consumes");
        aql.AppendLine("// FOR doc IN subscribes REMOVE doc IN subscribes");
        aql.AppendLine("// FOR doc IN hasPublisher REMOVE doc IN hasPublisher");
        aql.AppendLine("// FOR doc IN hasConsumer REMOVE doc IN hasConsumer");
        aql.AppendLine("// FOR doc IN hasSubscription REMOVE doc IN hasSubscription");
        aql.AppendLine();

        // Insert Repository documents
        aql.AppendLine("// ===== INSERT REPOSITORIES =====");
        var repositories = report.Events.Select(e => e.Repository)
            .Union(report.Publishers.Select(p => p.Repository))
            .Union(report.Consumers.Select(c => c.Repository))
            .Union(report.Subscriptions.Select(s => s.Repository))
            .Distinct()
            .OrderBy(r => r);

        foreach (var repo in repositories)
        {
            aql.AppendLine("INSERT {");
            aql.AppendLine($"    _key: \"{SanitizeArangoKey(repo)}\",");
            aql.AppendLine($"    name: \"{EscapeArangoString(repo)}\",");
            aql.AppendLine($"    type: \"Repository\"");
            aql.AppendLine("} INTO repositories OPTIONS { overwrite: true };");
            aql.AppendLine();
        }

        // Insert Service documents
        aql.AppendLine("// ===== INSERT SERVICES =====");
        var services = report.Events.Select(e => new { Repository = e.Repository, Project = e.Project })
            .Union(report.Publishers.Select(p => new { Repository = p.Repository, Project = p.Project }))
            .Union(report.Consumers.Select(c => new { Repository = c.Repository, Project = c.Project }))
            .Union(report.Subscriptions.Select(s => new { Repository = s.Repository, Project = s.Project }))
            .Distinct()
            .OrderBy(s => s.Repository).ThenBy(s => s.Project);

        foreach (var service in services)
        {
            var isHangfireService = report.Publishers.Any(p => p.Repository == service.Repository && p.Project == service.Project && p.IsInHangfireJob) ||
                                   report.Consumers.Any(c => c.Repository == service.Repository && c.Project == service.Project && c.IsInHangfireJob) ||
                                   report.Subscriptions.Any(s => s.Repository == service.Repository && s.Project == service.Project && s.IsInHangfireJob);

            var serviceType = isHangfireService ? "Background" : "Service";
            var serviceKey = $"{SanitizeArangoKey(service.Repository)}_{SanitizeArangoKey(service.Project)}";
            
            aql.AppendLine("INSERT {");
            aql.AppendLine($"    _key: \"{serviceKey}\",");
            aql.AppendLine($"    name: \"{EscapeArangoString(service.Project)}\",");
            aql.AppendLine($"    fullName: \"{EscapeArangoString(service.Repository)}/{EscapeArangoString(service.Project)}\",");
            aql.AppendLine($"    type: \"{serviceType}\",");
            aql.AppendLine($"    repository: \"{EscapeArangoString(service.Repository)}\"");
            aql.AppendLine("} INTO services OPTIONS { overwrite: true };");
            aql.AppendLine();
        }

        // Insert Event documents
        aql.AppendLine("// ===== INSERT EVENTS =====");
        foreach (var evt in report.Events.OrderBy(e => e.Name))
        {
            aql.AppendLine("INSERT {");
            aql.AppendLine($"    _key: \"{SanitizeArangoKey(evt.Name)}\",");
            aql.AppendLine($"    name: \"{EscapeArangoString(evt.Name)}\",");
            aql.AppendLine($"    fullName: \"{EscapeArangoString(evt.FullName)}\",");
            aql.AppendLine($"    repository: \"{EscapeArangoString(evt.Repository)}\",");
            aql.AppendLine($"    project: \"{EscapeArangoString(evt.Project)}\",");
            aql.AppendLine($"    type: \"IntegrationEvent\"");
            
            if (!string.IsNullOrEmpty(evt.MessageDataClass))
            {
                aql.AppendLine($"    , messageDataClass: \"{EscapeArangoString(evt.MessageDataClass)}\"");
            }
            
            if (evt.Properties.Any())
            {
                var properties = string.Join(", ", evt.Properties.Select(p => $"\"{EscapeArangoString(p)}\""));
                aql.AppendLine($"    , properties: [{properties}]");
            }
            
            aql.AppendLine("} INTO events OPTIONS { overwrite: true };");
            aql.AppendLine();
        }

        // Insert Publisher documents
        aql.AppendLine("// ===== INSERT PUBLISHERS =====");
        var publisherIndex = 0;
        foreach (var pub in report.Publishers.OrderBy(p => p.EventName).ThenBy(p => p.Repository).ThenBy(p => p.ClassName))
        {
            var publisherKey = $"pub_{SanitizeArangoKey(pub.Repository)}_{SanitizeArangoKey(pub.Project)}_{SanitizeArangoKey(pub.ClassName)}_{SanitizeArangoKey(pub.MethodName)}_{publisherIndex++}";
            
            aql.AppendLine("INSERT {");
            aql.AppendLine($"    _key: \"{publisherKey}\",");
            aql.AppendLine($"    className: \"{EscapeArangoString(pub.ClassName)}\",");
            aql.AppendLine($"    methodName: \"{EscapeArangoString(pub.MethodName)}\",");
            aql.AppendLine($"    repository: \"{EscapeArangoString(pub.Repository)}\",");
            aql.AppendLine($"    project: \"{EscapeArangoString(pub.Project)}\",");
            aql.AppendLine($"    lineNumber: {pub.LineNumber},");
            aql.AppendLine($"    eventName: \"{EscapeArangoString(pub.EventName)}\",");
            aql.AppendLine($"    isHangfireJob: {pub.IsInHangfireJob.ToString().ToLower()}");
            
            if (!string.IsNullOrEmpty(pub.HangfireJobClass))
            {
                aql.AppendLine($"    , hangfireJobClass: \"{EscapeArangoString(pub.HangfireJobClass)}\"");
            }
            
            aql.AppendLine("} INTO publishers OPTIONS { overwrite: true };");
            aql.AppendLine();
        }

        // Insert Consumer documents
        aql.AppendLine("// ===== INSERT CONSUMERS =====");
        var consumerIndex = 0;
        foreach (var cons in report.Consumers.OrderBy(c => c.EventName).ThenBy(c => c.Repository).ThenBy(c => c.HandlerClass))
        {
            var handlerClass = string.IsNullOrEmpty(cons.HandlerClass) ? "UnknownHandler" : cons.HandlerClass;
            var consumerKey = $"cons_{SanitizeArangoKey(cons.Repository)}_{SanitizeArangoKey(cons.Project)}_{SanitizeArangoKey(handlerClass)}_{consumerIndex++}";
            
            aql.AppendLine("INSERT {");
            aql.AppendLine($"    _key: \"{consumerKey}\",");
            aql.AppendLine($"    handlerClass: \"{EscapeArangoString(handlerClass)}\",");
            aql.AppendLine($"    handlerMethod: \"{EscapeArangoString(cons.HandlerMethod)}\",");
            aql.AppendLine($"    repository: \"{EscapeArangoString(cons.Repository)}\",");
            aql.AppendLine($"    project: \"{EscapeArangoString(cons.Project)}\",");
            aql.AppendLine($"    eventName: \"{EscapeArangoString(cons.EventName)}\",");
            aql.AppendLine($"    isHangfireJob: {cons.IsInHangfireJob.ToString().ToLower()}");
            aql.AppendLine("} INTO consumers OPTIONS { overwrite: true };");
            aql.AppendLine();
        }

        // Insert Subscription documents
        aql.AppendLine("// ===== INSERT SUBSCRIPTIONS =====");
        var subscriptionIndex = 0;
        foreach (var sub in report.Subscriptions.OrderBy(s => s.EventName).ThenBy(s => s.Repository).ThenBy(s => s.SubscriptionType))
        {
            var subscriptionKey = $"sub_{SanitizeArangoKey(sub.Repository)}_{SanitizeArangoKey(sub.Project)}_{SanitizeArangoKey(sub.SubscriptionType)}_{subscriptionIndex++}";
            
            aql.AppendLine("INSERT {");
            aql.AppendLine($"    _key: \"{subscriptionKey}\",");
            aql.AppendLine($"    subscriptionType: \"{EscapeArangoString(sub.SubscriptionType)}\",");
            aql.AppendLine($"    repository: \"{EscapeArangoString(sub.Repository)}\",");
            aql.AppendLine($"    project: \"{EscapeArangoString(sub.Project)}\",");
            aql.AppendLine($"    lineNumber: {sub.LineNumber},");
            aql.AppendLine($"    eventName: \"{EscapeArangoString(sub.EventName)}\",");
            aql.AppendLine($"    isHangfireJob: {sub.IsInHangfireJob.ToString().ToLower()}");
            aql.AppendLine("} INTO subscriptions OPTIONS { overwrite: true };");
            aql.AppendLine();
        }

        // Create relationships
        aql.AppendLine("// ===== CREATE RELATIONSHIPS =====");
        aql.AppendLine();
        
        // Repository -> Service relationships
        aql.AppendLine("// Repository -> Service relationships (contains)");
        foreach (var service in services)
        {
            var repoKey = SanitizeArangoKey(service.Repository);
            var serviceKey = $"{SanitizeArangoKey(service.Repository)}_{SanitizeArangoKey(service.Project)}";
            
            aql.AppendLine("INSERT {");
            aql.AppendLine($"    _from: \"repositories/{repoKey}\",");
            aql.AppendLine($"    _to: \"services/{serviceKey}\"");
            aql.AppendLine("} INTO contains;");
        }
        aql.AppendLine();

        // Service -> Event relationships (defines)
        aql.AppendLine("// Service -> Event relationships (defines)");
        foreach (var evt in report.Events)
        {
            var serviceKey = $"{SanitizeArangoKey(evt.Repository)}_{SanitizeArangoKey(evt.Project)}";
            var eventKey = SanitizeArangoKey(evt.Name);
            
            aql.AppendLine("INSERT {");
            aql.AppendLine($"    _from: \"services/{serviceKey}\",");
            aql.AppendLine($"    _to: \"events/{eventKey}\"");
            aql.AppendLine("} INTO defines;");
        }
        aql.AppendLine();

        // Publisher relationships
        aql.AppendLine("// Publisher relationships");
        publisherIndex = 0;
        foreach (var pub in report.Publishers.OrderBy(p => p.EventName).ThenBy(p => p.Repository).ThenBy(p => p.ClassName))
        {
            var publisherKey = $"pub_{SanitizeArangoKey(pub.Repository)}_{SanitizeArangoKey(pub.Project)}_{SanitizeArangoKey(pub.ClassName)}_{SanitizeArangoKey(pub.MethodName)}_{publisherIndex++}";
            var serviceKey = $"{SanitizeArangoKey(pub.Repository)}_{SanitizeArangoKey(pub.Project)}";
            var eventKey = SanitizeArangoKey(pub.EventName);
            
            // Service -> Publisher
            aql.AppendLine("INSERT {");
            aql.AppendLine($"    _from: \"services/{serviceKey}\",");
            aql.AppendLine($"    _to: \"publishers/{publisherKey}\"");
            aql.AppendLine("} INTO hasPublisher;");
            
            // Publisher -> Event
            aql.AppendLine("INSERT {");
            aql.AppendLine($"    _from: \"publishers/{publisherKey}\",");
            aql.AppendLine($"    _to: \"events/{eventKey}\"");
            aql.AppendLine("} INTO publishes;");
            aql.AppendLine();
        }

        // Consumer relationships
        aql.AppendLine("// Consumer relationships");
        consumerIndex = 0;
        foreach (var cons in report.Consumers.OrderBy(c => c.EventName).ThenBy(c => c.Repository).ThenBy(c => c.HandlerClass))
        {
            var handlerClass = string.IsNullOrEmpty(cons.HandlerClass) ? "UnknownHandler" : cons.HandlerClass;
            var consumerKey = $"cons_{SanitizeArangoKey(cons.Repository)}_{SanitizeArangoKey(cons.Project)}_{SanitizeArangoKey(handlerClass)}_{consumerIndex++}";
            var serviceKey = $"{SanitizeArangoKey(cons.Repository)}_{SanitizeArangoKey(cons.Project)}";
            var eventKey = SanitizeArangoKey(cons.EventName);
            
            // Service -> Consumer
            aql.AppendLine("INSERT {");
            aql.AppendLine($"    _from: \"services/{serviceKey}\",");
            aql.AppendLine($"    _to: \"consumers/{consumerKey}\"");
            aql.AppendLine("} INTO hasConsumer;");
            
            // Consumer -> Event
            aql.AppendLine("INSERT {");
            aql.AppendLine($"    _from: \"consumers/{consumerKey}\",");
            aql.AppendLine($"    _to: \"events/{eventKey}\"");
            aql.AppendLine("} INTO consumes;");
            aql.AppendLine();
        }

        // Subscription relationships
        aql.AppendLine("// Subscription relationships");
        subscriptionIndex = 0;
        foreach (var sub in report.Subscriptions.OrderBy(s => s.EventName).ThenBy(s => s.Repository).ThenBy(s => s.SubscriptionType))
        {
            var subscriptionKey = $"sub_{SanitizeArangoKey(sub.Repository)}_{SanitizeArangoKey(sub.Project)}_{SanitizeArangoKey(sub.SubscriptionType)}_{subscriptionIndex++}";
            var serviceKey = $"{SanitizeArangoKey(sub.Repository)}_{SanitizeArangoKey(sub.Project)}";
            var eventKey = SanitizeArangoKey(sub.EventName);
            
            // Service -> Subscription
            aql.AppendLine("INSERT {");
            aql.AppendLine($"    _from: \"services/{serviceKey}\",");
            aql.AppendLine($"    _to: \"subscriptions/{subscriptionKey}\"");
            aql.AppendLine("} INTO hasSubscription;");
            
            // Subscription -> Event
            aql.AppendLine("INSERT {");
            aql.AppendLine($"    _from: \"subscriptions/{subscriptionKey}\",");
            aql.AppendLine($"    _to: \"events/{eventKey}\"");
            aql.AppendLine("} INTO subscribes;");
            aql.AppendLine();
        }

        // Add useful queries
        aql.AppendLine("// ===== USEFUL QUERIES =====");
        aql.AppendLine();
        aql.AppendLine("// Show all events with their publishers, consumers, and subscriptions");
        aql.AppendLine("// FOR event IN events");
        aql.AppendLine("//     LET publishers = (");
        aql.AppendLine("//         FOR pub IN publishers");
        aql.AppendLine("//             FILTER pub.eventName == event.name");
        aql.AppendLine("//             RETURN CONCAT(pub.className, '.', pub.methodName)");
        aql.AppendLine("//     )");
        aql.AppendLine("//     LET consumers = (");
        aql.AppendLine("//         FOR cons IN consumers");
        aql.AppendLine("//             FILTER cons.eventName == event.name");
        aql.AppendLine("//             RETURN cons.handlerClass");
        aql.AppendLine("//     )");
        aql.AppendLine("//     LET subscriptions = (");
        aql.AppendLine("//         FOR sub IN subscriptions");
        aql.AppendLine("//             FILTER sub.eventName == event.name");
        aql.AppendLine("//             RETURN CONCAT(sub.project, ' (', sub.subscriptionType, ')')");
        aql.AppendLine("//     )");
        aql.AppendLine("//     RETURN {");
        aql.AppendLine("//         event: event.name,");
        aql.AppendLine("//         publishers: publishers,");
        aql.AppendLine("//         consumers: consumers,");
        aql.AppendLine("//         subscriptions: subscriptions");
        aql.AppendLine("//     }");
        aql.AppendLine();
        
        aql.AppendLine("// Find orphaned events (no publishers)");
        aql.AppendLine("// FOR event IN events");
        aql.AppendLine("//     LET hasPublisher = LENGTH(FOR pub IN publishers FILTER pub.eventName == event.name RETURN 1) > 0");
        aql.AppendLine("//     FILTER !hasPublisher");
        aql.AppendLine("//     RETURN { orphanedEvent: event.name }");
        aql.AppendLine();
        
        aql.AppendLine("// Find dead letter events (no consumers)");
        aql.AppendLine("// FOR event IN events");
        aql.AppendLine("//     LET hasConsumer = LENGTH(FOR cons IN consumers FILTER cons.eventName == event.name RETURN 1) > 0");
        aql.AppendLine("//     FILTER !hasConsumer");
        aql.AppendLine("//     RETURN { deadLetterEvent: event.name }");
        aql.AppendLine();

        aql.AppendLine("// Find events with subscriptions but no consumers (potential configuration issues)");
        aql.AppendLine("// FOR event IN events");
        aql.AppendLine("//     LET hasConsumer = LENGTH(FOR cons IN consumers FILTER cons.eventName == event.name RETURN 1) > 0");
        aql.AppendLine("//     LET hasSubscription = LENGTH(FOR sub IN subscriptions FILTER sub.eventName == event.name RETURN 1) > 0");
        aql.AppendLine("//     FILTER hasSubscription && !hasConsumer");
        aql.AppendLine("//     RETURN { event: event.name, issue: 'Subscription without handler' }");
        aql.AppendLine();
        
        aql.AppendLine("// Show message flow between services (graph traversal)");
        aql.AppendLine("// FOR service IN services");
        aql.AppendLine("//     FOR vertex, edge, path IN 1..3 OUTBOUND service hasPublisher, publishes, consumes, hasConsumer");
        aql.AppendLine("//         FILTER IS_SAME_COLLECTION('services', vertex)");
        aql.AppendLine("//         RETURN {");
        aql.AppendLine("//             from: service.fullName,");
        aql.AppendLine("//             to: vertex.fullName,");
        aql.AppendLine("//             path: path");
        aql.AppendLine("//         }");
        aql.AppendLine();
        
        aql.AppendLine("// Find Hangfire-related flows");
        aql.AppendLine("// FOR doc IN UNION(");
        aql.AppendLine("//     (FOR pub IN publishers FILTER pub.isHangfireJob == true RETURN pub),");
        aql.AppendLine("//     (FOR cons IN consumers FILTER cons.isHangfireJob == true RETURN cons),");
        aql.AppendLine("//     (FOR sub IN subscriptions FILTER sub.isHangfireJob == true RETURN sub)");
        aql.AppendLine("// )");
        aql.AppendLine("// RETURN doc");
        aql.AppendLine();
        
        aql.AppendLine("// Services with most message interactions");
        aql.AppendLine("// FOR service IN services");
        aql.AppendLine("//     LET publisherCount = LENGTH(FOR pub IN publishers FILTER pub.repository == service.repository && pub.project == service.name RETURN 1)");
        aql.AppendLine("//     LET consumerCount = LENGTH(FOR cons IN consumers FILTER cons.repository == service.repository && cons.project == service.name RETURN 1)");
        aql.AppendLine("//     LET subscriptionCount = LENGTH(FOR sub IN subscriptions FILTER sub.repository == service.repository && sub.project == service.name RETURN 1)");
        aql.AppendLine("//     SORT (publisherCount + consumerCount + subscriptionCount) DESC");
        aql.AppendLine("//     RETURN {");
        aql.AppendLine("//         service: service.fullName,");
        aql.AppendLine("//         publishers: publisherCount,");
        aql.AppendLine("//         consumers: consumerCount,");
        aql.AppendLine("//         subscriptions: subscriptionCount,");
        aql.AppendLine("//         total: publisherCount + consumerCount + subscriptionCount");
        aql.AppendLine("//     }");
        aql.AppendLine();
        
        aql.AppendLine("// Create a graph for visualization (run this after all inserts)");
        aql.AppendLine("// var graph_module = require('@arangodb/general-graph');");
        aql.AppendLine("// graph_module._create('MessageFlowGraph', [");
        aql.AppendLine("//   graph_module._relation('contains', ['repositories'], ['services']),");
        aql.AppendLine("//   graph_module._relation('defines', ['services'], ['events']),");
        aql.AppendLine("//   graph_module._relation('publishes', ['publishers'], ['events']),");
        aql.AppendLine("//   graph_module._relation('consumes', ['consumers'], ['events']),");
        aql.AppendLine("//   graph_module._relation('subscribes', ['subscriptions'], ['events']),");
        aql.AppendLine("//   graph_module._relation('hasPublisher', ['services'], ['publishers']),");
        aql.AppendLine("//   graph_module._relation('hasConsumer', ['services'], ['consumers']),");
        aql.AppendLine("//   graph_module._relation('hasSubscription', ['services'], ['subscriptions'])");
        aql.AppendLine("// ]);");

        return aql.ToString();
    }

    private string SanitizeArangoKey(string input)
    {
        if (string.IsNullOrEmpty(input)) return "unknown";
        
        // ArangoDB keys can only contain letters, numbers, underscores and hyphens
        // They cannot start with numbers
        var sanitized = Regex.Replace(input, @"[^a-zA-Z0-9_\-]", "_").Trim('_');
        
        // Ensure it doesn't start with a number
        if (char.IsDigit(sanitized[0]))
        {
            sanitized = "k_" + sanitized;
        }
        
        // Ensure it's not empty and not too long (max 254 chars)
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = "unknown";
        }
        else if (sanitized.Length > 250)
        {
            sanitized = sanitized.Substring(0, 250);
        }
        
        return sanitized;
    }

    private string EscapeArangoString(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        
        // Escape double quotes and backslashes for ArangoDB strings
        return input.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private async Task ExportToCypherAsync(MessageFlowReport report, string outputPath)
    {
        var cypher = GenerateCypherScript(report);
        await File.WriteAllTextAsync(outputPath, cypher);
        Console.WriteLine($"\nNeo4j Cypher script exported to: {outputPath}");
    }

    private string GenerateCypherScript(MessageFlowReport report)
    {
        var cypher = new StringBuilder();
        
        // Header comments
        cypher.AppendLine("// Message Flow Analysis - Neo4j Cypher Script");
        cypher.AppendLine($"// Generated: {report.AnalyzedAt:yyyy-MM-dd HH:mm:ss}");
        cypher.AppendLine($"// Repositories: {report.RepositoriesScanned}, Events: {report.Events.Count}, Publishers: {report.Publishers.Count}, Consumers: {report.Consumers.Count}");
        cypher.AppendLine();
        cypher.AppendLine("// Clear existing data (uncomment if needed)");
        cypher.AppendLine("// MATCH (n) DETACH DELETE n;");
        cypher.AppendLine();

        // Create constraints and indexes for better performance
        cypher.AppendLine("// Create constraints and indexes");
        cypher.AppendLine("CREATE CONSTRAINT event_name IF NOT EXISTS FOR (e:Event) REQUIRE e.name IS UNIQUE;");
        cypher.AppendLine("CREATE CONSTRAINT service_name IF NOT EXISTS FOR (s:Service) REQUIRE s.name IS UNIQUE;");
        cypher.AppendLine("CREATE CONSTRAINT repository_name IF NOT EXISTS FOR (r:Repository) REQUIRE r.name IS UNIQUE;");
        cypher.AppendLine("CREATE INDEX event_type_idx IF NOT EXISTS FOR (e:Event) ON (e.type);");
        cypher.AppendLine("CREATE INDEX service_type_idx IF NOT EXISTS FOR (s:Service) ON (s.type);");
        cypher.AppendLine();

        // Create Repository nodes
        cypher.AppendLine("// Create Repository nodes");
        var repositories = report.Events.Select(e => e.Repository)
            .Union(report.Publishers.Select(p => p.Repository))
            .Union(report.Consumers.Select(c => c.Repository))
            .Distinct()
            .OrderBy(r => r);

        foreach (var repo in repositories)
        {
            cypher.AppendLine($"MERGE (repo_{SanitizeNodeName(repo)}:Repository {{name: '{EscapeCypherString(repo)}'}});");
        }
        cypher.AppendLine();

        // Create Service/Project nodes
        cypher.AppendLine("// Create Service/Project nodes");
        var services = report.Events.Select(e => new { Repository = e.Repository, Project = e.Project })
            .Union(report.Publishers.Select(p => new { Repository = p.Repository, Project = p.Project }))
            .Union(report.Consumers.Select(c => new { Repository = c.Repository, Project = c.Project }))
            .Union(report.Subscriptions.Select(s => new { Repository = s.Repository, Project = s.Project }))
            .Distinct()
            .OrderBy(s => s.Repository).ThenBy(s => s.Project);

        foreach (var service in services)
        {
            var isHangfireService = report.Publishers.Any(p => p.Repository == service.Repository && p.Project == service.Project && p.IsInHangfireJob) ||
                                   report.Consumers.Any(c => c.Repository == service.Repository && c.Project == service.Project && c.IsInHangfireJob) ||
                                   report.Subscriptions.Any(s => s.Repository == service.Repository && s.Project == service.Project && s.IsInHangfireJob);

            var serviceType = isHangfireService ? "Background" : "Service";
            
            cypher.AppendLine($"MERGE (service_{SanitizeNodeName(service.Repository)}_{SanitizeNodeName(service.Project)}:Service {{");
            cypher.AppendLine($"    name: '{EscapeCypherString(service.Project)}',");
            cypher.AppendLine($"    fullName: '{EscapeCypherString(service.Repository)}/{EscapeCypherString(service.Project)}',");
            cypher.AppendLine($"    type: '{serviceType}',");
            cypher.AppendLine($"    repository: '{EscapeCypherString(service.Repository)}'");
            cypher.AppendLine("});");
        }
        cypher.AppendLine();

        // Create relationships between repositories and services
        cypher.AppendLine("// Create Repository -> Service relationships");
        foreach (var service in services)
        {
            cypher.AppendLine($"MERGE (repo_{SanitizeNodeName(service.Repository)})-[:CONTAINS]->(service_{SanitizeNodeName(service.Repository)}_{SanitizeNodeName(service.Project)});");
        }
        cypher.AppendLine();

        // Create Event nodes
        cypher.AppendLine("// Create Event nodes");
        foreach (var evt in report.Events.OrderBy(e => e.Name))
        {
            cypher.AppendLine($"MERGE (event_{SanitizeNodeName(evt.Name)}:Event {{");
            cypher.AppendLine($"    name: '{EscapeCypherString(evt.Name)}',");
            cypher.AppendLine($"    fullName: '{EscapeCypherString(evt.FullName)}',");
            cypher.AppendLine($"    repository: '{EscapeCypherString(evt.Repository)}',");
            cypher.AppendLine($"    project: '{EscapeCypherString(evt.Project)}',");
            cypher.AppendLine($"    type: 'IntegrationEvent'");
            
            if (!string.IsNullOrEmpty(evt.MessageDataClass))
            {
                cypher.AppendLine($"    , messageDataClass: '{EscapeCypherString(evt.MessageDataClass)}'");
            }
            
            if (evt.Properties.Any())
            {
                var properties = string.Join(", ", evt.Properties.Select(p => $"'{EscapeCypherString(p)}'"));
                cypher.AppendLine($"    , properties: [{properties}]");
            }
            
            cypher.AppendLine("});");
        }
        cypher.AppendLine();

        // Create Event -> Service relationships (where event is defined)
        cypher.AppendLine("// Create Event -> Service relationships (definition)");
        foreach (var evt in report.Events)
        {
            cypher.AppendLine($"MERGE (event_{SanitizeNodeName(evt.Name)})-[:DEFINED_IN]->(service_{SanitizeNodeName(evt.Repository)}_{SanitizeNodeName(evt.Project)});");
        }
        cypher.AppendLine();

        // Create Publisher nodes and relationships
        cypher.AppendLine("// Create Publisher relationships");
        foreach (var pub in report.Publishers.OrderBy(p => p.EventName).ThenBy(p => p.Repository).ThenBy(p => p.ClassName))
        {
            var publisherNodeName = $"publisher_{SanitizeNodeName(pub.Repository)}_{SanitizeNodeName(pub.Project)}_{SanitizeNodeName(pub.ClassName)}_{SanitizeNodeName(pub.MethodName)}_{pub.LineNumber}";
            
            // Create publisher node
            cypher.AppendLine($"MERGE ({publisherNodeName}:Publisher {{");
            cypher.AppendLine($"    className: '{EscapeCypherString(pub.ClassName)}',");
            cypher.AppendLine($"    methodName: '{EscapeCypherString(pub.MethodName)}',");
            cypher.AppendLine($"    repository: '{EscapeCypherString(pub.Repository)}',");
            cypher.AppendLine($"    project: '{EscapeCypherString(pub.Project)}',");
            cypher.AppendLine($"    lineNumber: {pub.LineNumber},");
            cypher.AppendLine($"    isHangfireJob: {pub.IsInHangfireJob.ToString().ToLower()}");
            
            if (!string.IsNullOrEmpty(pub.HangfireJobClass))
            {
                cypher.AppendLine($"    , hangfireJobClass: '{EscapeCypherString(pub.HangfireJobClass)}'");
            }
            
            cypher.AppendLine("});");
            
            // Create relationships
            cypher.AppendLine($"MERGE ({publisherNodeName})-[:PUBLISHES]->(event_{SanitizeNodeName(pub.EventName)});");
            cypher.AppendLine($"MERGE (service_{SanitizeNodeName(pub.Repository)}_{SanitizeNodeName(pub.Project)})-[:HAS_PUBLISHER]->({publisherNodeName});");
        }
        cypher.AppendLine();

        // Create Consumer nodes and relationships
        cypher.AppendLine("// Create Consumer relationships");
        foreach (var cons in report.Consumers.OrderBy(c => c.EventName).ThenBy(c => c.Repository).ThenBy(c => c.HandlerClass))
        {
            var handlerClass = string.IsNullOrEmpty(cons.HandlerClass) ? "UnknownHandler" : cons.HandlerClass;
            var consumerNodeName = $"consumer_{SanitizeNodeName(cons.Repository)}_{SanitizeNodeName(cons.Project)}_{SanitizeNodeName(handlerClass)}";
            
            // Create consumer node
            cypher.AppendLine($"MERGE ({consumerNodeName}:Consumer {{");
            cypher.AppendLine($"    handlerClass: '{EscapeCypherString(handlerClass)}',");
            cypher.AppendLine($"    handlerMethod: '{EscapeCypherString(cons.HandlerMethod)}',");
            cypher.AppendLine($"    repository: '{EscapeCypherString(cons.Repository)}',");
            cypher.AppendLine($"    project: '{EscapeCypherString(cons.Project)}',");
            cypher.AppendLine($"    isHangfireJob: {cons.IsInHangfireJob.ToString().ToLower()}");
            cypher.AppendLine("});");
            
            // Create relationships
            cypher.AppendLine($"MERGE ({consumerNodeName})-[:CONSUMES]->(event_{SanitizeNodeName(cons.EventName)});");
            cypher.AppendLine($"MERGE (service_{SanitizeNodeName(cons.Repository)}_{SanitizeNodeName(cons.Project)})-[:HAS_CONSUMER]->({consumerNodeName});");
        }
        cypher.AppendLine();

        // Create Subscription nodes and relationships
        cypher.AppendLine("// Create Subscription relationships");
        var subscriptionIndex = 0;
        foreach (var sub in report.Subscriptions.OrderBy(s => s.EventName).ThenBy(s => s.Repository).ThenBy(s => s.SubscriptionType))
        {
            var subscriptionNodeName = $"subscription_{SanitizeNodeName(sub.Repository)}_{SanitizeNodeName(sub.Project)}_{SanitizeNodeName(sub.SubscriptionType)}_{subscriptionIndex++}";
            
            // Create subscription node
            cypher.AppendLine($"MERGE ({subscriptionNodeName}:Subscription {{");
            cypher.AppendLine($"    subscriptionType: '{EscapeCypherString(sub.SubscriptionType)}',");
            cypher.AppendLine($"    repository: '{EscapeCypherString(sub.Repository)}',");
            cypher.AppendLine($"    project: '{EscapeCypherString(sub.Project)}',");
            cypher.AppendLine($"    lineNumber: {sub.LineNumber},");
            cypher.AppendLine($"    isHangfireJob: {sub.IsInHangfireJob.ToString().ToLower()}");
            cypher.AppendLine("});");
            
            // Create relationships
            cypher.AppendLine($"MERGE ({subscriptionNodeName})-[:SUBSCRIBES]->(event_{SanitizeNodeName(sub.EventName)});");
            cypher.AppendLine($"MERGE (service_{SanitizeNodeName(sub.Repository)}_{SanitizeNodeName(sub.Project)})-[:HAS_SUBSCRIPTION]->({subscriptionNodeName});");
        }
        cypher.AppendLine();

        // Add useful queries as comments
        cypher.AppendLine("// ===== USEFUL QUERIES =====");
        cypher.AppendLine();
        cypher.AppendLine("// Show all events with their publishers and consumers");
        cypher.AppendLine("// MATCH (e:Event)");
        cypher.AppendLine("// OPTIONAL MATCH (p:Publisher)-[:PUBLISHES]->(e)");
        cypher.AppendLine("// OPTIONAL MATCH (c:Consumer)-[:CONSUMES]->(e)");
        cypher.AppendLine("// RETURN e.name as Event, collect(DISTINCT p.className + '.' + p.methodName) as Publishers, collect(DISTINCT c.handlerClass) as Consumers");
        cypher.AppendLine("// ORDER BY e.name;");
        cypher.AppendLine();
        cypher.AppendLine("// Find orphaned events (no publishers)");
        cypher.AppendLine("// MATCH (e:Event)");
        cypher.AppendLine("// WHERE NOT EXISTS { (p:Publisher)-[:PUBLISHES]->(e) }");
        cypher.AppendLine("// RETURN e.name as OrphanedEvent;");
        cypher.AppendLine();
        cypher.AppendLine("// Find dead letter events (no consumers)");
        cypher.AppendLine("// MATCH (e:Event)");
        cypher.AppendLine("// WHERE NOT EXISTS { (c:Consumer)-[:CONSUMES]->(e) }");
        cypher.AppendLine("// RETURN e.name as DeadLetterEvent;");
        cypher.AppendLine();
        cypher.AppendLine("// Show message flow between services");
        cypher.AppendLine("// MATCH (s1:Service)-[:HAS_PUBLISHER]->(p:Publisher)-[:PUBLISHES]->(e:Event)<-[:CONSUMES]-(c:Consumer)<-[:HAS_CONSUMER]-(s2:Service)");
        cypher.AppendLine("// RETURN s1.fullName as PublisherService, e.name as Event, s2.fullName as ConsumerService;");
        cypher.AppendLine();
        cypher.AppendLine("// Find Hangfire-related message flows");
        cypher.AppendLine("// MATCH (n)");
        cypher.AppendLine("// WHERE (n:Publisher OR n:Consumer) AND n.isHangfireJob = true");
        cypher.AppendLine("// OPTIONAL MATCH (n)-[:PUBLISHES|CONSUMES]->(e:Event)");
        cypher.AppendLine("// RETURN n, e;");
        cypher.AppendLine();
        cypher.AppendLine("// Show services with most message interactions");
        cypher.AppendLine("// MATCH (s:Service)");
        cypher.AppendLine("// OPTIONAL MATCH (s)-[:HAS_PUBLISHER]->(p:Publisher)");
        cypher.AppendLine("// OPTIONAL MATCH (s)-[:HAS_CONSUMER]->(c:Consumer)");
        cypher.AppendLine("// RETURN s.fullName as Service, count(DISTINCT p) as Publishers, count(DISTINCT c) as Consumers, (count(DISTINCT p) + count(DISTINCT c)) as TotalInteractions");
        cypher.AppendLine("// ORDER BY TotalInteractions DESC;");

        return cypher.ToString();
    }

    private string SanitizeNodeName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "unknown";
        
        // Replace invalid characters for Cypher node names
        return Regex.Replace(input, @"[^a-zA-Z0-9_]", "_").Trim('_');
    }

    private string EscapeCypherString(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        
        // Escape single quotes and backslashes for Cypher strings
        return input.Replace("\\", "\\\\").Replace("'", "\\'");
    }

    private async Task ExportToHtmlAsync(MessageFlowReport report, string outputPath)
    {
        var html = GenerateHtmlReport(report);
        await File.WriteAllTextAsync(outputPath, html);
        Console.WriteLine($"\nHTML analysis exported to: {outputPath}");
    }

    private string GenerateHtmlReport(MessageFlowReport report)
    {
        var html = new StringBuilder();
        
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html>");
        html.AppendLine("<head>");
        html.AppendLine("    <title>Message Flow Analysis Report</title>");
        html.AppendLine("    <style>");
        html.AppendLine("        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 20px; background-color: #f5f5f5; }");
        html.AppendLine("        .container { max-width: 1200px; margin: 0 auto; background-color: white; padding: 30px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }");
        html.AppendLine("        h1 { color: #2c3e50; border-bottom: 3px solid #3498db; padding-bottom: 10px; }");
        html.AppendLine("        h2 { color: #34495e; border-left: 4px solid #3498db; padding-left: 15px; margin-top: 30px; }");
        html.AppendLine("        h3 { color: #2980b9; margin-top: 25px; }");
        html.AppendLine("        .summary { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; border-radius: 8px; margin: 20px 0; }");
        html.AppendLine("        .summary-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 15px; }");
        html.AppendLine("        .summary-item { text-align: center; }");
        html.AppendLine("        .summary-number { font-size: 2em; font-weight: bold; display: block; }");
        html.AppendLine("        .event-card { border: 1px solid #ddd; border-radius: 8px; margin: 15px 0; padding: 20px; background: #fafafa; }");
        html.AppendLine("        .event-title { font-size: 1.2em; font-weight: bold; color: #2c3e50; margin-bottom: 10px; }");
        html.AppendLine("        .event-info { margin: 8px 0; color: #555; }");
        html.AppendLine("        .publishers, .consumers { margin: 15px 0; }");
        html.AppendLine("        .publishers h4, .consumers h4 { color: #27ae60; margin: 10px 0 5px 0; }");
        html.AppendLine("        .consumers h4 { color: #e74c3c; }");
        html.AppendLine("        .publisher-item, .consumer-item { background: white; border-left: 4px solid #27ae60; padding: 10px; margin: 5px 0; border-radius: 4px; }");
        html.AppendLine("        .consumer-item { border-left-color: #e74c3c; }");
        html.AppendLine("        .hangfire { background-color: #f39c12; color: white; padding: 2px 8px; border-radius: 4px; font-size: 0.8em; margin-left: 8px; }");
        html.AppendLine("        .warning { color: #e74c3c; font-weight: bold; padding: 10px; background: #fdf2f2; border-radius: 4px; margin: 10px 0; }");
        html.AppendLine("        .matrix-section { margin-top: 30px; }");
        html.AppendLine("        .flow-item { border: 1px solid #ddd; margin: 10px 0; padding: 15px; border-radius: 6px; }");
        html.AppendLine("        .toc { background: #ecf0f1; padding: 20px; border-radius: 8px; margin: 20px 0; }");
        html.AppendLine("        .toc ul { columns: 2; column-gap: 30px; }");
        html.AppendLine("        .toc a { text-decoration: none; color: #3498db; }");
        html.AppendLine("        .toc a:hover { text-decoration: underline; }");
        html.AppendLine("        .timestamp { color: #7f8c8d; font-size: 0.9em; text-align: right; margin-top: 20px; }");
        html.AppendLine("        .repo-section { margin-left: 20px; }");
        html.AppendLine("        .repo-title { color: #8e44ad; font-weight: bold; margin: 10px 0 5px 0; }");
        html.AppendLine("    </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("    <div class=\"container\">");
        
        // Header
        html.AppendLine("        <h1>Message Flow Analysis Report</h1>");
        html.AppendLine($"        <div class=\"timestamp\">Generated: {report.AnalyzedAt:yyyy-MM-dd HH:mm:ss}</div>");
        
        // Summary
        html.AppendLine("        <div class=\"summary\">");
        html.AppendLine("            <h2>Summary</h2>");
        html.AppendLine("            <div class=\"summary-grid\">");
        html.AppendLine($"                <div class=\"summary-item\"><span class=\"summary-number\">{report.RepositoriesScanned}</span>Repositories</div>");
        html.AppendLine($"                <div class=\"summary-item\"><span class=\"summary-number\">{report.ProjectsScanned}</span>Projects</div>");
        html.AppendLine($"                <div class=\"summary-item\"><span class=\"summary-number\">{report.Events.Count}</span>Events</div>");
        html.AppendLine($"                <div class=\"summary-item\"><span class=\"summary-number\">{report.Publishers.Count}</span>Publishers</div>");
        html.AppendLine($"                <div class=\"summary-item\"><span class=\"summary-number\">{report.Consumers.Count}</span>Consumers</div>");
        html.AppendLine("            </div>");
        html.AppendLine("        </div>");

        // Table of Contents
        html.AppendLine("        <div class=\"toc\">");
        html.AppendLine("            <h3>Table of Contents</h3>");
        html.AppendLine("            <ul>");
        html.AppendLine("                <li><a href=\"#events\">Integration Events</a></li>");
        html.AppendLine("                <li><a href=\"#matrix\">Publisher-Consumer Matrix</a></li>");
        html.AppendLine("                <li><a href=\"#hangfire\">Hangfire Analysis</a></li>");
        html.AppendLine("            </ul>");
        html.AppendLine("        </div>");

        // Events Section
        html.AppendLine("        <h2 id=\"events\">Integration Events</h2>");
        
        foreach (var evt in report.Events.OrderBy(e => e.Name))
        {
            var publishers = report.Publishers.Where(p => 
                p.EventName.Equals(evt.Name, StringComparison.OrdinalIgnoreCase) ||
                p.EventName.Contains(evt.Name)).ToList();
            
            var consumers = report.Consumers.Where(c => 
                c.EventName.Equals(evt.Name, StringComparison.OrdinalIgnoreCase) ||
                c.EventName.Contains(evt.Name)).ToList();

            html.AppendLine("        <div class=\"event-card\">");
            html.AppendLine($"            <div class=\"event-title\">{evt.Name}</div>");
            html.AppendLine($"            <div class=\"event-info\"><strong>Repository:</strong> {evt.Repository}</div>");
            html.AppendLine($"            <div class=\"event-info\"><strong>Project:</strong> {evt.Project}</div>");
            
            if (!string.IsNullOrEmpty(evt.MessageDataClass))
            {
                html.AppendLine($"            <div class=\"event-info\"><strong>Message Data Class:</strong> {evt.MessageDataClass}</div>");
            }

            // Publishers
            html.AppendLine("            <div class=\"publishers\">");
            if (publishers.Any())
            {
                html.AppendLine($"                <h4>Published by ({publishers.Count})</h4>");
                foreach (var pub in publishers)
                {
                    var hangfireTag = pub.IsInHangfireJob ? "<span class=\"hangfire\">HANGFIRE</span>" : "";
                    html.AppendLine($"                <div class=\"publisher-item\">{pub.Repository}/{pub.Project} - {pub.ClassName}.{pub.MethodName}(){hangfireTag}</div>");
                }
            }
            else
            {
                html.AppendLine("                <div class=\"warning\">No publishers found</div>");
            }
            html.AppendLine("            </div>");

            // Consumers
            html.AppendLine("            <div class=\"consumers\">");
            if (consumers.Any())
            {
                html.AppendLine($"                <h4>Consumed by ({consumers.Count})</h4>");
                foreach (var cons in consumers)
                {
                    var hangfireTag = cons.IsInHangfireJob ? "<span class=\"hangfire\">HANGFIRE</span>" : "";
                    var handlerName = string.IsNullOrEmpty(cons.HandlerClass) ? "Unknown Handler" : cons.HandlerClass;
                    html.AppendLine($"                <div class=\"consumer-item\">{cons.Repository}/{cons.Project} - {handlerName}{hangfireTag}</div>");
                }
            }
            else
            {
                html.AppendLine("                <div class=\"warning\">No consumers found</div>");
            }
            html.AppendLine("            </div>");
            
            html.AppendLine("        </div>");
        }

        // Matrix Section
        html.AppendLine("        <div class=\"matrix-section\">");
        html.AppendLine("            <h2 id=\"matrix\">Publisher-Consumer Matrix</h2>");
        
        var allEventNames = report.Events.Select(e => e.Name)
            .Union(report.Publishers.Select(p => p.EventName))
            .Union(report.Consumers.Select(c => c.EventName))
            .Distinct()
            .OrderBy(name => name);

        foreach (var eventName in allEventNames)
        {
            var publishers = report.Publishers.Where(p => 
                p.EventName.Contains(eventName, StringComparison.OrdinalIgnoreCase)).ToList();
            var consumers = report.Consumers.Where(c => 
                c.EventName.Contains(eventName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (publishers.Any() || consumers.Any())
            {
                html.AppendLine("            <div class=\"flow-item\">");
                html.AppendLine($"                <h3>{eventName}</h3>");
                
                if (publishers.Any())
                {
                    html.AppendLine($"                <h4>Publishers ({publishers.Count})</h4>");
                    var publishingRepos = publishers.GroupBy(p => p.Repository);
                    foreach (var repo in publishingRepos)
                    {
                        html.AppendLine($"                <div class=\"repo-title\">Repository: {repo.Key}</div>");
                        html.AppendLine("                <div class=\"repo-section\">");
                        foreach (var pub in repo)
                        {
                            var hangfire = pub.IsInHangfireJob ? "<span class=\"hangfire\">HF</span>" : "";
                            html.AppendLine($"                    <div>{pub.Project}/{pub.ClassName}.{pub.MethodName}(){hangfire}</div>");
                        }
                        html.AppendLine("                </div>");
                    }
                }
                else
                {
                    html.AppendLine("                <div class=\"warning\">No publishers found - orphaned event?</div>");
                }

                if (consumers.Any())
                {
                    html.AppendLine($"                <h4>Consumers ({consumers.Count})</h4>");
                    var consumingRepos = consumers.GroupBy(c => c.Repository);
                    foreach (var repo in consumingRepos)
                    {
                        html.AppendLine($"                <div class=\"repo-title\">Repository: {repo.Key}</div>");
                        html.AppendLine("                <div class=\"repo-section\">");
                        foreach (var cons in repo)
                        {
                            var hangfire = cons.IsInHangfireJob ? "<span class=\"hangfire\">HF</span>" : "";
                            var handlerName = string.IsNullOrEmpty(cons.HandlerClass) ? "Unknown Handler" : cons.HandlerClass;
                            html.AppendLine($"                    <div>{cons.Project}/{handlerName}{hangfire}</div>");
                        }
                        html.AppendLine("                </div>");
                    }
                }
                else
                {
                    html.AppendLine("                <div class=\"warning\">No consumers found - dead letter?</div>");
                }
                
                html.AppendLine("            </div>");
            }
        }
        html.AppendLine("        </div>");

        // Hangfire Section
        var hangfirePublishers = report.Publishers.Where(p => p.IsInHangfireJob).ToList();
        var hangfireConsumers = report.Consumers.Where(c => c.IsInHangfireJob).ToList();

        html.AppendLine("        <h2 id=\"hangfire\">Hangfire Analysis</h2>");
        html.AppendLine($"        <p><strong>Hangfire Publishers:</strong> {hangfirePublishers.Count}</p>");
        html.AppendLine($"        <p><strong>Hangfire Consumers:</strong> {hangfireConsumers.Count}</p>");

        if (hangfirePublishers.Any())
        {
            html.AppendLine("        <h3>Hangfire Publishers</h3>");
            foreach (var pub in hangfirePublishers.GroupBy(p => p.EventName))
            {
                html.AppendLine("        <div class=\"flow-item\">");
                html.AppendLine($"            <h4>Event: {pub.Key}</h4>");
                foreach (var publisher in pub)
                {
                    html.AppendLine($"            <div class=\"publisher-item\">{publisher.Repository}/{publisher.Project} - {publisher.HangfireJobClass}</div>");
                }
                html.AppendLine("        </div>");
            }
        }

        if (hangfireConsumers.Any())
        {
            html.AppendLine("        <h3>Hangfire Consumers</h3>");
            foreach (var cons in hangfireConsumers.GroupBy(c => c.EventName))
            {
                html.AppendLine("        <div class=\"flow-item\">");
                html.AppendLine($"            <h4>Event: {cons.Key}</h4>");
                foreach (var consumer in cons)
                {
                    var handlerName = string.IsNullOrEmpty(consumer.HandlerClass) ? "Unknown Handler" : consumer.HandlerClass;
                    html.AppendLine($"            <div class=\"consumer-item\">{consumer.Repository}/{consumer.Project} - {handlerName}</div>");
                }
                html.AppendLine("        </div>");
            }
        }

        if (!hangfirePublishers.Any() && !hangfireConsumers.Any())
        {
            html.AppendLine("        <p>No Hangfire-related message flows detected</p>");
        }

        html.AppendLine("    </div>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        
        return html.ToString();
    }

    private async Task ExportToJsonAsync(MessageFlowReport report, string outputPath)
    {
        var options = new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        var json = JsonSerializer.Serialize(report, options);
        await File.WriteAllTextAsync(outputPath, json);
        Console.WriteLine($"\nDetailed analysis exported to: {outputPath}");
    }
}