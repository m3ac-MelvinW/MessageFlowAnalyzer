using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MessageFlowAnalyzer.Models;

namespace MessageFlowAnalyzer.Exporters
{
    public class ArangoExporter : IExporter
    {
        public async Task ExportAsync(MessageFlowReport report, string outputPath)
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
            aql.AppendLine("// 4. The graphLabel property provides clean display names for visualization");
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
                aql.AppendLine($"    graphLabel: \"{EscapeArangoString(CreateRepositoryLabel(repo))}\",");
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
                aql.AppendLine($"    graphLabel: \"{EscapeArangoString(CreateServiceLabel(service.Project, serviceType))}\",");
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
                aql.AppendLine($"    graphLabel: \"{EscapeArangoString(CreateEventLabel(evt.Name))}\",");
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
                aql.AppendLine($"    graphLabel: \"{EscapeArangoString(CreatePublisherLabel(pub.ClassName, pub.MethodName, pub.IsInHangfireJob))}\",");
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
                aql.AppendLine($"    graphLabel: \"{EscapeArangoString(CreateConsumerLabel(handlerClass, cons.IsInHangfireJob))}\",");
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
                aql.AppendLine($"    graphLabel: \"{EscapeArangoString(CreateSubscriptionLabel(sub.SubscriptionType, sub.Project, sub.IsInHangfireJob))}\",");
                aql.AppendLine($"    repository: \"{EscapeArangoString(sub.Repository)}\",");
                aql.AppendLine($"    project: \"{EscapeArangoString(sub.Project)}\",");
                aql.AppendLine($"    lineNumber: {sub.LineNumber},");
                aql.AppendLine($"    eventName: \"{EscapeArangoString(sub.EventName)}\",");
                aql.AppendLine($"    isHangfireJob: {sub.IsInHangfireJob.ToString().ToLower()}");
                aql.AppendLine("} INTO subscriptions OPTIONS { overwrite: true };");
                aql.AppendLine();
            }

            // Create relationships - THIS IS THE CRITICAL FIXED SECTION
            aql.AppendLine("// ===== CREATE RELATIONSHIPS (EDGES) =====");
            aql.AppendLine("// These edges create the connections between nodes");
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
                aql.AppendLine("} INTO contains OPTIONS { overwrite: true };");
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
                aql.AppendLine("} INTO defines OPTIONS { overwrite: true };");
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
                aql.AppendLine("} INTO hasPublisher OPTIONS { overwrite: true };");
                
                // Publisher -> Event
                aql.AppendLine("INSERT {");
                aql.AppendLine($"    _from: \"publishers/{publisherKey}\",");
                aql.AppendLine($"    _to: \"events/{eventKey}\"");
                aql.AppendLine("} INTO publishes OPTIONS { overwrite: true };");
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
                aql.AppendLine("} INTO hasConsumer OPTIONS { overwrite: true };");
                
                // Consumer -> Event
                aql.AppendLine("INSERT {");
                aql.AppendLine($"    _from: \"consumers/{consumerKey}\",");
                aql.AppendLine($"    _to: \"events/{eventKey}\"");
                aql.AppendLine("} INTO consumes OPTIONS { overwrite: true };");
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
                aql.AppendLine("} INTO hasSubscription OPTIONS { overwrite: true };");
                
                // Subscription -> Event
                aql.AppendLine("INSERT {");
                aql.AppendLine($"    _from: \"subscriptions/{subscriptionKey}\",");
                aql.AppendLine($"    _to: \"events/{eventKey}\"");
                aql.AppendLine("} INTO subscribes OPTIONS { overwrite: true };");
                aql.AppendLine();
            }

            // Add useful queries
            AddUsefulQueries(aql);

            return aql.ToString();
        }

        // Graph Label Creation Methods
        private string CreateRepositoryLabel(string repositoryName)
        {
            // Remove common prefixes/suffixes and make it concise
            var label = repositoryName;
            
            // Remove common repository prefixes
            var prefixesToRemove = new[] { "Company.", "Project.", "Repo.", "Repository." };
            foreach (var prefix in prefixesToRemove)
            {
                if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    label = label.Substring(prefix.Length);
                    break;
                }
            }
            
            // If still too long, take the last part after the last dot
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
            
            // Remove common suffixes to make labels cleaner
            var suffixesToRemove = new[] { ".Service", ".API", ".Web", ".Worker", ".Job", ".Background" };
            foreach (var suffix in suffixesToRemove)
            {
                if (label.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    label = label.Substring(0, label.Length - suffix.Length);
                    break;
                }
            }
            
            // Add icon based on service type
            var icon = serviceType == "Background" ? "⚙️" : "🔧";
            return $"{icon} {label}";
        }

        private string CreateEventLabel(string eventName)
        {
            var label = eventName;
            
            // Remove "IntegrationEvent" suffix if present
            if (label.EndsWith("IntegrationEvent", StringComparison.OrdinalIgnoreCase))
            {
                label = label.Substring(0, label.Length - "IntegrationEvent".Length);
            }
            else if (label.EndsWith("Event", StringComparison.OrdinalIgnoreCase))
            {
                label = label.Substring(0, label.Length - "Event".Length);
            }
            
            // Add camel case spacing for readability
            label = AddSpacesToCamelCase(label);
            
            return $"📨 {label}";
        }

        private string CreatePublisherLabel(string className, string methodName, bool isHangfireJob)
        {
            var classLabel = className;
            
            // Simplify common class name patterns
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
            
            // Remove "Handler" suffix if present
            if (label.EndsWith("Handler", StringComparison.OrdinalIgnoreCase))
            {
                label = label.Substring(0, label.Length - "Handler".Length);
            }
            
            // Remove "IntegrationEvent" if present
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
            
            // Simplify project name
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

        private void AddUsefulQueries(StringBuilder aql)
        {
            aql.AppendLine("// ===== USEFUL QUERIES =====");
            aql.AppendLine();
            aql.AppendLine("// Show all events with their publishers, consumers, and subscriptions");
            aql.AppendLine("// FOR event IN events");
            aql.AppendLine("//     LET publishers = (");
            aql.AppendLine("//         FOR pub IN publishers");
            aql.AppendLine("//             FILTER pub.eventName == event.name");
            aql.AppendLine("//             RETURN pub.graphLabel");
            aql.AppendLine("//     )");
            aql.AppendLine("//     LET consumers = (");
            aql.AppendLine("//         FOR cons IN consumers");
            aql.AppendLine("//             FILTER cons.eventName == event.name");
            aql.AppendLine("//             RETURN cons.graphLabel");
            aql.AppendLine("//     )");
            aql.AppendLine("//     LET subscriptions = (");
            aql.AppendLine("//         FOR sub IN subscriptions");
            aql.AppendLine("//             FILTER sub.eventName == event.name");
            aql.AppendLine("//             RETURN sub.graphLabel");
            aql.AppendLine("//     )");
            aql.AppendLine("//     RETURN {");
            aql.AppendLine("//         event: event.graphLabel,");
            aql.AppendLine("//         publishers: publishers,");
            aql.AppendLine("//         consumers: consumers,");
            aql.AppendLine("//         subscriptions: subscriptions");
            aql.AppendLine("//     }");
            aql.AppendLine();
            
            aql.AppendLine("// Find message flow paths - Service A publishes Event X, Service B consumes Event X");
            aql.AppendLine("// FOR publisherService IN services");
            aql.AppendLine("//     FOR publisher IN 1..1 OUTBOUND publisherService hasPublisher");
            aql.AppendLine("//         FOR event IN 1..1 OUTBOUND publisher publishes");
            aql.AppendLine("//             FOR consumer IN 1..1 INBOUND event consumes");
            aql.AppendLine("//                 FOR consumerService IN 1..1 INBOUND consumer hasConsumer");
            aql.AppendLine("//                     FILTER publisherService._id != consumerService._id");
            aql.AppendLine("//                     RETURN {");
            aql.AppendLine("//                         from: publisherService.graphLabel,");
            aql.AppendLine("//                         event: event.graphLabel,");
            aql.AppendLine("//                         to: consumerService.graphLabel,");
            aql.AppendLine("//                         publisher: publisher.graphLabel,");
            aql.AppendLine("//                         consumer: consumer.graphLabel");
            aql.AppendLine("//                     }");
            aql.AppendLine();
            
            aql.AppendLine("// Find orphaned events (no publishers)");
            aql.AppendLine("// FOR event IN events");
            aql.AppendLine("//     LET hasPublisher = LENGTH(FOR pub IN publishers FILTER pub.eventName == event.name RETURN 1) > 0");
            aql.AppendLine("//     FILTER !hasPublisher");
            aql.AppendLine("//     RETURN { orphanedEvent: event.graphLabel }");
            aql.AppendLine();
            
            aql.AppendLine("// Find dead letter events (no consumers)");
            aql.AppendLine("// FOR event IN events");
            aql.AppendLine("//     LET hasConsumer = LENGTH(FOR cons IN consumers FILTER cons.eventName == event.name RETURN 1) > 0");
            aql.AppendLine("//     FILTER !hasConsumer");
            aql.AppendLine("//     RETURN { deadLetterEvent: event.graphLabel }");
            aql.AppendLine();

            aql.AppendLine("// Find events with subscriptions but no consumers (potential configuration issues)");
            aql.AppendLine("// FOR event IN events");
            aql.AppendLine("//     LET hasConsumer = LENGTH(FOR cons IN consumers FILTER cons.eventName == event.name RETURN 1) > 0");
            aql.AppendLine("//     LET hasSubscription = LENGTH(FOR sub IN subscriptions FILTER sub.eventName == event.name RETURN 1) > 0");
            aql.AppendLine("//     FILTER hasSubscription && !hasConsumer");
            aql.AppendLine("//     RETURN { event: event.graphLabel, issue: 'Subscription without handler' }");
            aql.AppendLine();
            
            aql.AppendLine("// Show message flow between services (graph traversal)");
            aql.AppendLine("// FOR service IN services");
            aql.AppendLine("//     FOR vertex, edge, path IN 1..5 OUTBOUND service hasPublisher, publishes, consumes, hasConsumer");
            aql.AppendLine("//         FILTER IS_SAME_COLLECTION('services', vertex)");
            aql.AppendLine("//         RETURN {");
            aql.AppendLine("//             from: service.graphLabel,");
            aql.AppendLine("//             to: vertex.graphLabel,");
            aql.AppendLine("//             pathLength: LENGTH(path.edges)");
            aql.AppendLine("//         }");
            aql.AppendLine();
            
            aql.AppendLine("// Find Hangfire-related flows");
            aql.AppendLine("// FOR doc IN UNION(");
            aql.AppendLine("//     (FOR pub IN publishers FILTER pub.isHangfireJob == true RETURN { type: 'Publisher', label: pub.graphLabel, event: pub.eventName }),");
            aql.AppendLine("//     (FOR cons IN consumers FILTER cons.isHangfireJob == true RETURN { type: 'Consumer', label: cons.graphLabel, event: cons.eventName }),");
            aql.AppendLine("//     (FOR sub IN subscriptions FILTER sub.isHangfireJob == true RETURN { type: 'Subscription', label: sub.graphLabel, event: sub.eventName })");
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
            aql.AppendLine("//         service: service.graphLabel,");
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
            aql.AppendLine();
            
            aql.AppendLine("// Query to show all nodes with their graph labels (useful for testing)");
            aql.AppendLine("// FOR doc IN UNION(");
            aql.AppendLine("//     (FOR repo IN repositories RETURN { collection: 'repositories', key: repo._key, label: repo.graphLabel }),");
            aql.AppendLine("//     (FOR service IN services RETURN { collection: 'services', key: service._key, label: service.graphLabel }),");
            aql.AppendLine("//     (FOR event IN events RETURN { collection: 'events', key: event._key, label: event.graphLabel }),");
            aql.AppendLine("//     (FOR pub IN publishers RETURN { collection: 'publishers', key: pub._key, label: pub.graphLabel }),");
            aql.AppendLine("//     (FOR cons IN consumers RETURN { collection: 'consumers', key: cons._key, label: cons.graphLabel }),");
            aql.AppendLine("//     (FOR sub IN subscriptions RETURN { collection: 'subscriptions', key: sub._key, label: sub.graphLabel })");
            aql.AppendLine("// )");
            aql.AppendLine("// SORT doc.collection, doc.label");
            aql.AppendLine("// RETURN doc");
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
    }
}