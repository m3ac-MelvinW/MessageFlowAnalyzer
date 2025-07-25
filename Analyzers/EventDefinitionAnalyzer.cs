using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MessageFlowAnalyzer.Models;

namespace MessageFlowAnalyzer.Analyzers
{
    public class EventDefinitionAnalyzer : BaseAnalyzer
    {
        public async Task<List<MessageEventDefinition>> AnalyzeAsync(string filePath, string repoName)
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
    }
}