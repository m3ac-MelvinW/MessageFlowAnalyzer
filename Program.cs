using System;
using System.Linq;
using System.Threading.Tasks;
using MessageFlowAnalyzer.Core;

namespace MessageFlowAnalyzer
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: MessageFlowAnalyzer.exe <repos-root-path>");
                Console.WriteLine("  Options:");
                Console.WriteLine("    --export-json          Export results to JSON");
                Console.WriteLine("    --export-html          Export results to HTML");
                Console.WriteLine("    --export-arango        Export ArangoDB AQL script");
                Console.WriteLine("    --include-details      Include detailed code context");
                Console.WriteLine("    --hangfire-only        Only show Hangfire-related messages");
                Console.WriteLine("    --exclude-tests        Exclude test projects from analysis");
                return;
            }

            string reposRootPath = args[0];
            bool exportJson = args.Contains("--export-json");
            bool exportHtml = args.Contains("--export-html");
            bool exportArango = args.Contains("--export-arango");
            bool includeDetails = args.Contains("--include-details");
            bool hangfireOnly = args.Contains("--hangfire-only");
            bool excludeTests = args.Contains("--exclude-tests");

            var analyzer = new AnalyzerService();
            await analyzer.AnalyzeAllRepositoriesAsync(
                reposRootPath, 
                exportJson, 
                exportHtml, 
                exportArango, 
                includeDetails, 
                hangfireOnly, 
                excludeTests);
        }
    }
}