using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MessageFlowAnalyzer.Models;

namespace MessageFlowAnalyzer.Analyzers
{
    public class SubscriptionAnalyzer : BaseAnalyzer
    {
        public async Task<List<MessageEventSubscription>> AnalyzeAsync(string filePath, string repoName, bool includeDetails)
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
    }
}