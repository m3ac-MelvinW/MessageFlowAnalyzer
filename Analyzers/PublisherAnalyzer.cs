using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MessageFlowAnalyzer.Models;

namespace MessageFlowAnalyzer.Analyzers
{
    public class PublisherAnalyzer : BaseAnalyzer
    {
        public async Task<List<MessagePublisher>> AnalyzeAsync(string filePath, string repoName, bool includeDetails)
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
    }
}