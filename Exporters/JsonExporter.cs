using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MessageFlowAnalyzer.Models;

namespace MessageFlowAnalyzer.Exporters
{
    public class JsonExporter : IExporter
    {
        public async Task ExportAsync(MessageFlowReport report, string outputPath)
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
}