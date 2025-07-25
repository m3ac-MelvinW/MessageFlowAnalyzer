using System;
using System.Linq;
using MessageFlowAnalyzer.Models;

namespace MessageFlowAnalyzer.Reports
{
    public class ReportGenerator
    {
        public void GenerateMessageFlowReport(MessageFlowReport report)
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

        public void GeneratePublisherConsumerMatrix(MessageFlowReport report)
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

        public void GenerateHangfireReport(MessageFlowReport report)
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
    }
}