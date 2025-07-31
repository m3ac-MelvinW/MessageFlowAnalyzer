using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MessageFlowAnalyzer.Models;

namespace MessageFlowAnalyzer.Exporters
{
    public class TinkerpopExporter : IExporter
    {
        public async Task ExportAsync(MessageFlowReport report, string outputPath)
        {
            var gremlin = GenerateTinkerpopScript(report);
            await File.WriteAllTextAsync(outputPath, gremlin);
            Console.WriteLine($"\nTinkerPop Gremlin script exported to: {outputPath}");
        }

        private string GenerateTinkerpopScript(MessageFlowReport report)
        {
            var gremlin = new StringBuilder();

            // Header comments
            gremlin.AppendLine("// Message Flow Analysis - TinkerPop Gremlin Script");
            gremlin.AppendLine($"// Generated: {report.AnalyzedAt:yyyy-MM-dd HH:mm:ss}");
            gremlin.AppendLine($"// Repositories: {report.RepositoriesScanned}, Events: {report.Events.Count}, Publishers: {report.Publishers.Count}, Consumers: {report.Consumers.Count}, Subscriptions: {report.Subscriptions.Count}");
            gremlin.AppendLine();
            gremlin.AppendLine("// Instructions:");
            gremlin.AppendLine("// 1. Connect to your TinkerPop-compatible graph database (Neptune, CosmosDB, JanusGraph, etc.)");
            gremlin.AppendLine("// 2. Execute this script in the Gremlin console or through your preferred client");
            gremlin.AppendLine("// 3. Use graph visualization tools to explore the message flow");
            gremlin.AppendLine("// 4. The 'displayName' property provides clean labels for visualization");
            gremlin.AppendLine();

            // Clear existing data (optional)
            gremlin.AppendLine("// ===== CLEAR EXISTING DATA (OPTIONAL) =====");
            gremlin.AppendLine("// Uncomment to clear all vertices and edges");
            gremlin.AppendLine("// g.V().drop().iterate()");
            gremlin.AppendLine();

            // Add Repository vertices
            gremlin.AppendLine("// ===== ADD REPOSITORY VERTICES =====");
            var repositories = report.Events.Select(e => e.Repository)
                .Union(report.Publishers.Select(p => p.Repository))
                .Union(report.Consumers.Select(c => c.Repository))
                .Union(report.Subscriptions.Select(s => s.Repository))
                .Distinct()
                .OrderBy(r => r);

            foreach (var repo in repositories)
            {
                var repoId = SanitizeId(repo);
                var displayName = CreateRepositoryLabel(repo);

                gremlin.AppendLine("g.addV('Repository')");
                gremlin.AppendLine($"  .property(id, '{repoId}')");
                gremlin.AppendLine($"  .property('name', '{EscapeString(repo)}')");
                gremlin.AppendLine($"  .property('displayName', '{EscapeString(displayName)}')");
                gremlin.AppendLine($"  .property('type', 'Repository')");
                gremlin.AppendLine("  .next()");
                gremlin.AppendLine();
            }

            // Add Service vertices
            gremlin.AppendLine("// ===== ADD SERVICE VERTICES =====");
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

                var serviceType = isHangfireService ? "BackgroundService" : "Service";
                var serviceId = $"{SanitizeId(service.Repository)}_{SanitizeId(service.Project)}";
                var displayName = CreateServiceLabel(service.Project, serviceType);

                gremlin.AppendLine("g.addV('Service')");
                gremlin.AppendLine($"  .property(id, '{serviceId}')");
                gremlin.AppendLine($"  .property('name', '{EscapeString(service.Project)}')");
                gremlin.AppendLine($"  .property('fullName', '{EscapeString(service.Repository)}/{EscapeString(service.Project)}')");
                gremlin.AppendLine($"  .property('displayName', '{EscapeString(displayName)}')");
                gremlin.AppendLine($"  .property('type', '{serviceType}')");
                gremlin.AppendLine($"  .property('repository', '{EscapeString(service.Repository)}')");
                gremlin.AppendLine("  .next()");
                gremlin.AppendLine();
            }

            // Add Event vertices
            gremlin.AppendLine("// ===== ADD EVENT VERTICES =====");
            foreach (var evt in report.Events.OrderBy(e => e.Name))
            {
                var eventId = SanitizeId(evt.Name);
                var displayName = CreateEventLabel(evt.Name);

                gremlin.AppendLine("g.addV('IntegrationEvent')");
                gremlin.AppendLine($"  .property(id, '{eventId}')");
                gremlin.AppendLine($"  .property('name', '{EscapeString(evt.Name)}')");
                gremlin.AppendLine($"  .property('fullName', '{EscapeString(evt.FullName)}')");
                gremlin.AppendLine($"  .property('displayName', '{EscapeString(displayName)}')");
                gremlin.AppendLine($"  .property('repository', '{EscapeString(evt.Repository)}')");
                gremlin.AppendLine($"  .property('project', '{EscapeString(evt.Project)}')");

                if (!string.IsNullOrEmpty(evt.MessageDataClass))
                {
                    gremlin.AppendLine($"  .property('messageDataClass', '{EscapeString(evt.MessageDataClass)}')");
                }

                if (evt.Properties.Any())
                {
                    // Store properties as a comma-separated string since Gremlin doesn't have native array support in all implementations
                    var properties = string.Join(",", evt.Properties.Select(p => EscapeString(p)));
                    gremlin.AppendLine($"  .property('properties', '{properties}')");
                }

                gremlin.AppendLine("  .next()");
                gremlin.AppendLine();
            }

            // Add Publisher vertices
            gremlin.AppendLine("// ===== ADD PUBLISHER VERTICES =====");
            var publisherIndex = 0;
            foreach (var pub in report.Publishers.OrderBy(p => p.EventName).ThenBy(p => p.Repository).ThenBy(p => p.ClassName))
            {
                var publisherId = $"pub_{SanitizeId(pub.Repository)}_{SanitizeId(pub.Project)}_{SanitizeId(pub.ClassName)}_{SanitizeId(pub.MethodName)}_{publisherIndex++}";
                var displayName = CreatePublisherLabel(pub.ClassName, pub.MethodName, pub.IsInHangfireJob);

                gremlin.AppendLine("g.addV('Publisher')");
                gremlin.AppendLine($"  .property(id, '{publisherId}')");
                gremlin.AppendLine($"  .property('className', '{EscapeString(pub.ClassName)}')");
                gremlin.AppendLine($"  .property('methodName', '{EscapeString(pub.MethodName)}')");
                gremlin.AppendLine($"  .property('displayName', '{EscapeString(displayName)}')");
                gremlin.AppendLine($"  .property('repository', '{EscapeString(pub.Repository)}')");
                gremlin.AppendLine($"  .property('project', '{EscapeString(pub.Project)}')");
                gremlin.AppendLine($"  .property('lineNumber', {pub.LineNumber})");
                gremlin.AppendLine($"  .property('eventName', '{EscapeString(pub.EventName)}')");
                gremlin.AppendLine($"  .property('isHangfireJob', {pub.IsInHangfireJob.ToString().ToLower()})");

                if (!string.IsNullOrEmpty(pub.HangfireJobClass))
                {
                    gremlin.AppendLine($"  .property('hangfireJobClass', '{EscapeString(pub.HangfireJobClass)}')");
                }

                gremlin.AppendLine("  .next()");
                gremlin.AppendLine();
            }

            // Add Consumer vertices
            gremlin.AppendLine("// ===== ADD CONSUMER VERTICES =====");
            var consumerIndex = 0;
            foreach (var cons in report.Consumers.OrderBy(c => c.EventName).ThenBy(c => c.Repository).ThenBy(c => c.HandlerClass))
            {
                var handlerClass = string.IsNullOrEmpty(cons.HandlerClass) ? "UnknownHandler" : cons.HandlerClass;
                var consumerId = $"cons_{SanitizeId(cons.Repository)}_{SanitizeId(cons.Project)}_{SanitizeId(handlerClass)}_{consumerIndex++}";
                var displayName = CreateConsumerLabel(handlerClass, cons.IsInHangfireJob);

                gremlin.AppendLine("g.addV('Consumer')");
                gremlin.AppendLine($"  .property(id, '{consumerId}')");
                gremlin.AppendLine($"  .property('handlerClass', '{EscapeString(handlerClass)}')");
                gremlin.AppendLine($"  .property('handlerMethod', '{EscapeString(cons.HandlerMethod)}')");
                gremlin.AppendLine($"  .property('displayName', '{EscapeString(displayName)}')");
                gremlin.AppendLine($"  .property('repository', '{EscapeString(cons.Repository)}')");
                gremlin.AppendLine($"  .property('project', '{EscapeString(cons.Project)}')");
                gremlin.AppendLine($"  .property('eventName', '{EscapeString(cons.EventName)}')");
                gremlin.AppendLine($"  .property('isHangfireJob', {cons.IsInHangfireJob.ToString().ToLower()})");
                gremlin.AppendLine("  .next()");
                gremlin.AppendLine();
            }

            // Add Subscription vertices
            gremlin.AppendLine("// ===== ADD SUBSCRIPTION VERTICES =====");
            var subscriptionIndex = 0;
            foreach (var sub in report.Subscriptions.OrderBy(s => s.EventName).ThenBy(s => s.Repository).ThenBy(s => s.SubscriptionType))
            {
                var subscriptionId = $"sub_{SanitizeId(sub.Repository)}_{SanitizeId(sub.Project)}_{SanitizeId(sub.SubscriptionType)}_{subscriptionIndex++}";
                var displayName = CreateSubscriptionLabel(sub.SubscriptionType, sub.Project, sub.IsInHangfireJob);

                gremlin.AppendLine("g.addV('Subscription')");
                gremlin.AppendLine($"  .property(id, '{subscriptionId}')");
                gremlin.AppendLine($"  .property('subscriptionType', '{EscapeString(sub.SubscriptionType)}')");
                gremlin.AppendLine($"  .property('displayName', '{EscapeString(displayName)}')");
                gremlin.AppendLine($"  .property('repository', '{EscapeString(sub.Repository)}')");
                gremlin.AppendLine($"  .property('project', '{EscapeString(sub.Project)}')");
                gremlin.AppendLine($"  .property('lineNumber', {sub.LineNumber})");
                gremlin.AppendLine($"  .property('eventName', '{EscapeString(sub.EventName)}')");
                gremlin.AppendLine($"  .property('isHangfireJob', {sub.IsInHangfireJob.ToString().ToLower()})");
                gremlin.AppendLine("  .next()");
                gremlin.AppendLine();
            }

            // Add edges (relationships)
            gremlin.AppendLine("// ===== ADD EDGES (RELATIONSHIPS) =====");
            gremlin.AppendLine();

            // Repository -> Service edges (contains)
            gremlin.AppendLine("// Repository -> Service relationships (contains)");
            foreach (var service in services)
            {
                var repoId = SanitizeId(service.Repository);
                var serviceId = $"{SanitizeId(service.Repository)}_{SanitizeId(service.Project)}";

                gremlin.AppendLine($"g.V('{repoId}').addE('contains').to(g.V('{serviceId}')).next()");
            }
            gremlin.AppendLine();

            // Service -> Event edges (defines)
            gremlin.AppendLine("// Service -> Event relationships (defines)");
            foreach (var evt in report.Events)
            {
                var serviceId = $"{SanitizeId(evt.Repository)}_{SanitizeId(evt.Project)}";
                var eventId = SanitizeId(evt.Name);

                gremlin.AppendLine($"g.V('{serviceId}').addE('defines').to(g.V('{eventId}')).next()");
            }
            gremlin.AppendLine();

            // Publisher edges
            gremlin.AppendLine("// Publisher relationships");
            publisherIndex = 0;
            foreach (var pub in report.Publishers.OrderBy(p => p.EventName).ThenBy(p => p.Repository).ThenBy(p => p.ClassName))
            {
                var publisherId = $"pub_{SanitizeId(pub.Repository)}_{SanitizeId(pub.Project)}_{SanitizeId(pub.ClassName)}_{SanitizeId(pub.MethodName)}_{publisherIndex++}";
                var serviceId = $"{SanitizeId(pub.Repository)}_{SanitizeId(pub.Project)}";
                var eventId = SanitizeId(pub.EventName);

                // Service -> Publisher
                gremlin.AppendLine($"g.V('{serviceId}').addE('hasPublisher').to(g.V('{publisherId}')).next()");
                // Publisher -> Event
                gremlin.AppendLine($"g.V('{publisherId}').addE('publishes').to(g.V('{eventId}')).next()");
            }
            gremlin.AppendLine();

            // Consumer edges
            gremlin.AppendLine("// Consumer relationships");
            consumerIndex = 0;
            foreach (var cons in report.Consumers.OrderBy(c => c.EventName).ThenBy(c => c.Repository).ThenBy(c => c.HandlerClass))
            {
                var handlerClass = string.IsNullOrEmpty(cons.HandlerClass) ? "UnknownHandler" : cons.HandlerClass;
                var consumerId = $"cons_{SanitizeId(cons.Repository)}_{SanitizeId(cons.Project)}_{SanitizeId(handlerClass)}_{consumerIndex++}";
                var serviceId = $"{SanitizeId(cons.Repository)}_{SanitizeId(cons.Project)}";
                var eventId = SanitizeId(cons.EventName);

                // Service -> Consumer
                gremlin.AppendLine($"g.V('{serviceId}').addE('hasConsumer').to(g.V('{consumerId}')).next()");
                // Consumer -> Event
                gremlin.AppendLine($"g.V('{consumerId}').addE('consumes').to(g.V('{eventId}')).next()");
            }
            gremlin.AppendLine();

            // Subscription edges
            gremlin.AppendLine("// Subscription relationships");
            subscriptionIndex = 0;
            foreach (var sub in report.Subscriptions.OrderBy(s => s.EventName).ThenBy(s => s.Repository).ThenBy(s => s.SubscriptionType))
            {
                var subscriptionId = $"sub_{SanitizeId(sub.Repository)}_{SanitizeId(sub.Project)}_{SanitizeId(sub.SubscriptionType)}_{subscriptionIndex++}";
                var serviceId = $"{SanitizeId(sub.Repository)}_{SanitizeId(sub.Project)}";
                var eventId = SanitizeId(sub.EventName);

                // Service -> Subscription
                gremlin.AppendLine($"g.V('{serviceId}').addE('hasSubscription').to(g.V('{subscriptionId}')).next()");
                // Subscription -> Event
                gremlin.AppendLine($"g.V('{subscriptionId}').addE('subscribes').to(g.V('{eventId}')).next()");
            }
            gremlin.AppendLine();

            // Add useful queries
            AddUsefulQueries(gremlin);

            return gremlin.ToString();
        }

        private void AddUsefulQueries(StringBuilder gremlin)
        {
            gremlin.AppendLine("// ===== USEFUL QUERIES =====");
            gremlin.AppendLine();

            gremlin.AppendLine("// Count all vertices by type");
            gremlin.AppendLine("// g.V().groupCount().by(label)");
            gremlin.AppendLine();

            gremlin.AppendLine("// Count all edges by type");
            gremlin.AppendLine("// g.E().groupCount().by(label)");
            gremlin.AppendLine();

            gremlin.AppendLine("// Show all events with their publishers and consumers");
            gremlin.AppendLine("// g.V().hasLabel('IntegrationEvent').as('event')");
            gremlin.AppendLine("//   .project('event', 'publishers', 'consumers')");
            gremlin.AppendLine("//   .by('displayName')");
            gremlin.AppendLine("//   .by(__.in('publishes').values('displayName').fold())");
            gremlin.AppendLine("//   .by(__.in('consumes').values('displayName').fold())");
            gremlin.AppendLine();

            gremlin.AppendLine("// Find message flow paths - Service A publishes Event X, Service B consumes Event X");
            gremlin.AppendLine("// g.V().hasLabel('Service').as('publisherService')");
            gremlin.AppendLine("//   .out('hasPublisher').out('publishes').as('event')");
            gremlin.AppendLine("//   .in('consumes').in('hasConsumer').as('consumerService')");
            gremlin.AppendLine("//   .where('publisherService', neq('consumerService'))");
            gremlin.AppendLine("//   .select('publisherService', 'event', 'consumerService')");
            gremlin.AppendLine("//   .by('displayName')");
            gremlin.AppendLine();

            gremlin.AppendLine("// Find orphaned events (no publishers)");
            gremlin.AppendLine("// g.V().hasLabel('IntegrationEvent')");
            gremlin.AppendLine("//   .where(__.not(__.in('publishes')))");
            gremlin.AppendLine("//   .values('displayName')");
            gremlin.AppendLine();

            gremlin.AppendLine("// Find dead letter events (no consumers)");
            gremlin.AppendLine("// g.V().hasLabel('IntegrationEvent')");
            gremlin.AppendLine("//   .where(__.not(__.in('consumes')))");
            gremlin.AppendLine("//   .values('displayName')");
            gremlin.AppendLine();

            gremlin.AppendLine("// Find events with subscriptions but no consumers");
            gremlin.AppendLine("// g.V().hasLabel('IntegrationEvent')");
            gremlin.AppendLine("//   .where(__.in('subscribes').hasLabel('Subscription'))");
            gremlin.AppendLine("//   .where(__.not(__.in('consumes')))");
            gremlin.AppendLine("//   .values('displayName')");
            gremlin.AppendLine();

            gremlin.AppendLine("// Show message flow between services (path traversal)");
            gremlin.AppendLine("// g.V().hasLabel('Service').as('start')");
            gremlin.AppendLine("//   .repeat(__.out('hasPublisher', 'publishes', 'consumes', 'hasConsumer'))");
            gremlin.AppendLine("//   .until(__.hasLabel('Service').where(neq('start')))");
            gremlin.AppendLine("//   .path()");
            gremlin.AppendLine("//   .by('displayName')");
            gremlin.AppendLine("//   .limit(10)");
            gremlin.AppendLine();

            gremlin.AppendLine("// Find Hangfire-related components");
            gremlin.AppendLine("// g.V().has('isHangfireJob', true)");
            gremlin.AppendLine("//   .project('type', 'name', 'event')");
            gremlin.AppendLine("//   .by(label)");
            gremlin.AppendLine("//   .by('displayName')");
            gremlin.AppendLine("//   .by('eventName')");
            gremlin.AppendLine();

            gremlin.AppendLine("// Services with most message interactions");
            gremlin.AppendLine("// g.V().hasLabel('Service')");
            gremlin.AppendLine("//   .project('service', 'publishers', 'consumers', 'subscriptions', 'total')");
            gremlin.AppendLine("//   .by('displayName')");
            gremlin.AppendLine("//   .by(__.out('hasPublisher').count())");
            gremlin.AppendLine("//   .by(__.out('hasConsumer').count())");
            gremlin.AppendLine("//   .by(__.out('hasSubscription').count())");
            gremlin.AppendLine("//   .by(__.out('hasPublisher', 'hasConsumer', 'hasSubscription').count())");
            gremlin.AppendLine("//   .order().by(select('total'), desc)");
            gremlin.AppendLine();

            gremlin.AppendLine("// Get all vertex types and their counts");
            gremlin.AppendLine("// g.V().label().groupCount()");
            gremlin.AppendLine();

            gremlin.AppendLine("// Get all edge types and their counts");
            gremlin.AppendLine("// g.E().label().groupCount()");
        }

        // Reuse the label creation methods from ArangoExporter
        private string CreateRepositoryLabel(string repositoryName)
        {
            var label = repositoryName;
            var prefixesToRemove = new[] { "Company.", "Project.", "Repo.", "Repository." };
            foreach (var prefix in prefixesToRemove)
            {
                if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    label = label.Substring(prefix.Length);
                    break;
                }
            }

            if (label.Length > 20 && label.Contains("."))
            {
                var parts = label.Split('.');
                label = parts.Last();
            }

            return $"📦 {label}";
        }

        private string CreateServiceLabel(string projectName, string serviceType)
        {
            var label = projectName;
            var suffixesToRemove = new[] { ".Service", ".API", ".Web", ".Worker", ".Job", ".Background" };
            foreach (var suffix in suffixesToRemove)
            {
                if (label.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    label = label.Substring(0, label.Length - suffix.Length);
                    break;
                }
            }

            var icon = serviceType == "BackgroundService" ? "⚙️" : "🔧";
            return $"{icon} {label}";
        }

        private string CreateEventLabel(string eventName)
        {
            var label = eventName;
            if (label.EndsWith("IntegrationEvent", StringComparison.OrdinalIgnoreCase))
            {
                label = label.Substring(0, label.Length - "IntegrationEvent".Length);
            }
            else if (label.EndsWith("Event", StringComparison.OrdinalIgnoreCase))
            {
                label = label.Substring(0, label.Length - "Event".Length);
            }

            label = AddSpacesToCamelCase(label);
            return $"📨 {label}";
        }

        private string CreatePublisherLabel(string className, string methodName, bool isHangfireJob)
        {
            var classLabel = className;
            var suffixesToRemove = new[] { "Service", "Controller", "Handler", "Job", "Worker" };
            foreach (var suffix in suffixesToRemove)
            {
                if (classLabel.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    classLabel = classLabel.Substring(0, classLabel.Length - suffix.Length);
                    break;
                }
            }

            var icon = isHangfireJob ? "🔄" : "📤";
            return $"{icon} {classLabel}.{methodName}()";
        }

        private string CreateConsumerLabel(string handlerClass, bool isHangfireJob)
        {
            var label = handlerClass;
            if (label.EndsWith("Handler", StringComparison.OrdinalIgnoreCase))
            {
                label = label.Substring(0, label.Length - "Handler".Length);
            }

            if (label.Contains("IntegrationEvent"))
            {
                label = label.Replace("IntegrationEvent", "");
            }

            var icon = isHangfireJob ? "🔄" : "📥";
            return $"{icon} {label}";
        }

        private string CreateSubscriptionLabel(string subscriptionType, string projectName, bool isHangfireJob)
        {
            var projectLabel = projectName;
            var suffixesToRemove = new[] { ".Service", ".API", ".Web", ".Worker" };
            foreach (var suffix in suffixesToRemove)
            {
                if (projectLabel.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    projectLabel = projectLabel.Substring(0, projectLabel.Length - suffix.Length);
                    break;
                }
            }

            var icon = isHangfireJob ? "🔄" : "🔗";
            return $"{icon} {projectLabel} ({subscriptionType})";
        }

        private string AddSpacesToCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = new StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                if (i > 0 && char.IsUpper(input[i]) && !char.IsUpper(input[i - 1]))
                {
                    result.Append(' ');
                }
                result.Append(input[i]);
            }
            return result.ToString();
        }

        private string SanitizeId(string input)
        {
            if (string.IsNullOrEmpty(input)) return "unknown";

            // Remove special characters and replace with underscores
            var sanitized = Regex.Replace(input, @"[^a-zA-Z0-9_]", "_").Trim('_');

            // Ensure it's not empty
            if (string.IsNullOrEmpty(sanitized))
            {
                sanitized = "unknown";
            }

            // Ensure reasonable length
            if (sanitized.Length > 250)
            {
                sanitized = sanitized.Substring(0, 250);
            }

            return sanitized;
        }

        private string EscapeString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            // Escape single quotes for Gremlin strings
            return input.Replace("'", "\\'").Replace("\\", "\\\\");
        }
    }
}