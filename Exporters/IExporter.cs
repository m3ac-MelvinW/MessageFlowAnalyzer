using System.Threading.Tasks;
using MessageFlowAnalyzer.Models;

namespace MessageFlowAnalyzer.Exporters
{
    public interface IExporter
    {
        Task ExportAsync(MessageFlowReport report, string outputPath);
    }
}