using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MessageFlowAnalyzer.Models;

namespace MessageFlowAnalyzer.Analyzers
{
    public class ConsumerAnalyzer : BaseAnalyzer
    {
        public async Task<List<MessageConsumer>> AnalyzeAsync(string filePath, string repoName, bool includeDetails)
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
                    for (int j = System.Math.Max(0, i - 5); j <= System.Math.Min(lines.Length - 1, i + 5); j++)
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
    }
}