# Message Flow Analyzer

A powerful C# tool that analyzes .NET repositories to discover and visualize message flow patterns between microservices. The analyzer scans your codebase to identify integration events, publishers, consumers, and subscriptions, then exports the findings to multiple formats for visualization and analysis.

## 🚀 Features

- **Multi-Repository Analysis**: Scan multiple .NET repositories simultaneously
- **Event Discovery**: Automatically detects integration events that inherit from `IntegrationEvent`
- **Publisher Detection**: Finds event publishers in your services and background jobs
- **Consumer Analysis**: Identifies event handlers and consumers
- **Subscription Mapping**: Discovers event subscriptions and registrations
- **Hangfire Support**: Special detection for Hangfire background job integrations
- **Multiple Export Formats**: Export to ArangoDB (AQL), Neo4j (Cypher), TinkerPop (Gremlin), Markdown, and HTML

## 📊 Supported Export Formats

| Format | Purpose | Output File |
|--------|---------|-------------|
| **ArangoDB** | Graph database queries | `*.aql` |
| **Neo4j** | Graph database queries | `*.cypher` |
| **TinkerPop** | Graph database scripts | `*.groovy` |
| **Markdown** | Documentation | `*.md` |
| **HTML** | Web-based reports | `*.html` |

## 🛠️ Installation

### Prerequisites

- .NET 6.0 or later
- JetBrains Rider 2025.1.2 (recommended) or Visual Studio

### Building from Source

```bash
git clone <repository-url>
cd MessageFlowAnalyzer
dotnet build
dotnet run
```

📖 Usage
Basic Usage
```bash
dotnet run -- --repositories "C:\Projects\Repo1,C:\Projects\Repo2" --output "output"
```
### Command-Line Options
| Option | Description |
|--------|-------------|
| `--repositories` | Comma-separated list of repository paths to analyze |
| `--output` | Output directory for generated files |
| `--format` | Output format (aql, cypher, groovy, md
| html) |
| `--include-hangfire` | Include Hangfire background job analysis |
| `--verbose` | Enable verbose logging |
| `--help` | Show help message |
### Example Command
Analyze single repository:

```bash
dotnet run -- --repositories "C:\Projects\Repo1,C:\Projects\Repo2" --output "output" --format "aql,cypher,groovy,md,html" --include-hangfire --verbose
```

Analyze multiple repositories:
```bash 
dotnet run -- -r "C:\Projects\ServiceA,C:\Projects\ServiceB,C:\Projects\ServiceC" -f "neo4j"
```
Export to all formats:
```bash
dotnet run -- -r "C:\Projects\MyProject" -f "all"
```

### Project Structure
```
MessageFlowAnalyzer/
├── Analyzers/
│   ├── BaseAnalyzer.cs
│   ├── EventDefinitionAnalyzer.cs
│   ├── PublisherAnalyzer.cs
│   ├── ConsumerAnalyzer.cs
│   └── SubscriptionAnalyzer.cs
├── Exporters/
│   ├── IExporter.cs
│   ├── ArangoExporter.cs
│   ├── Neo4jExporter.cs
│   ├── TinkerpopExporter.cs
│   ├── MarkdownExporter.cs
│   └── HtmlExporter.cs
├── Models/
│   ├── MessageEventDefinition.cs
│   ├── EventPublisher.cs
│   ├── EventConsumer.cs
│   ├── EventSubscription.cs
│   └── MessageFlowReport.cs
└── Program.cs
```

###  What Gets Analyzed
- **Integration Events**: Classes inheriting from `IntegrationEvent`
- **Publishers**: Classes that publish events, including background jobs
- **Consumers**: Classes that handle events, including message handlers
- **Subscriptions**: Event subscriptions and registrations in the codebase
- **Hangfire Jobs**: Special handling for Hangfire background jobs
- **Message Flow**: Overall message flow between services
- **Code Structure**: Analyzes the structure of the codebase to identify relationships
- **Dependency Graph**: Builds a dependency graph of services and their interactions
- **Documentation Generation**: Generates documentation for message flows and interactions
- **Visualization Support**: Exports data in formats suitable for visualization tools

 
### Future Enhancements
- **Performance Metrics**: Analyzes performance-related aspects of message flows
- **Error Handling**: Identifies potential error handling patterns in message flows
- **Configuration Analysis**: Analyzes configuration files for message flow settings
- **Best Practices**: Suggests best practices for message flow design
- **Code Quality**: Evaluates code quality related to message flows
- **Security Analysis**: Identifies security-related issues in message flows
- **Scalability Considerations**: Analyzes scalability aspects of message flows
- **Integration Patterns**: Identifies common integration patterns used in the codebase
- **Event Versioning**: Analyzes event versioning strategies
- **Testing Coverage**: Evaluates testing coverage for message flows
- **Documentation Quality**: Assesses the quality of documentation related to message flows
- **Code Smells**: Identifies potential code smells in message flow implementations
- **Refactoring Opportunities**: Suggests areas for refactoring to improve message flows
- **Dependency Injection**: Analyzes dependency injection patterns in message flows
- **Configuration Management**: Evaluates configuration management practices for message flows
- **Logging Practices**: Analyzes logging practices related to message flows
- **Monitoring and Metrics**: Identifies monitoring and metrics collection practices for message flows
- **Event Sourcing**: Analyzes event sourcing patterns in the codebase


