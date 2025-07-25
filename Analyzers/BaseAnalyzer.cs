using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MessageFlowAnalyzer.Analyzers
{
    public abstract class BaseAnalyzer
    {
        protected readonly List<string> _hangfireIndicators = new()
        {
            "BackgroundJob",
            "RecurringJob",
            "[AutomaticRetry]",
            "[Queue(",
            "IJob",
            "[JobDisplayName"
        };

        protected bool IsHangfireRelated(string content)
        {
            return _hangfireIndicators.Any(indicator => 
                content.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        protected string GetProjectNameFromPath(string filePath)
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

        protected string GetCodeContext(string[] lines, int centerIndex, int contextLines)
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

        protected bool IsServiceRegistrationContext(string[] lines, int currentIndex)
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

        protected string ExtractEventNameFromVariable(string[] lines, int publishLineIndex, string variableName)
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
    }
}