using System;
using System.Collections.Generic;

namespace MessageFlowAnalyzer.Models
{
    public class MessageEventDefinition
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public string FilePath { get; set; }
        public string Repository { get; set; }
        public string Project { get; set; }
        public List<string> Properties { get; set; } = new();
        public string MessageDataClass { get; set; }
        public List<string> BaseProperties { get; set; } = new();
    }

    public class MessagePublisher
    {
        public string EventName { get; set; }
        public string Repository { get; set; }
        public string Project { get; set; }
        public string FilePath { get; set; }
        public string ClassName { get; set; }
        public string MethodName { get; set; }
        public int LineNumber { get; set; }
        public string CodeContext { get; set; }
        public bool IsInHangfireJob { get; set; }
        public string HangfireJobClass { get; set; }
    }

    public class MessageEventSubscription
    {
        public string EventName { get; set; }
        public string Repository { get; set; }
        public string Project { get; set; }
        public string FilePath { get; set; }
        public string SubscriptionType { get; set; } // "ServiceCollection", "EventBus", etc.
        public int LineNumber { get; set; }
        public string CodeContext { get; set; }
        public bool IsInHangfireJob { get; set; }
    }

    public class MessageConsumer
    {
        public string EventName { get; set; }
        public string Repository { get; set; }
        public string Project { get; set; }
        public string FilePath { get; set; }
        public string HandlerClass { get; set; }
        public string HandlerMethod { get; set; }
        public bool IsInHangfireJob { get; set; }
        public List<string> HandlerLogic { get; set; } = new();
    }

    public class MessageFlowReport
    {
        public List<MessageEventDefinition> Events { get; set; } = new();
        public List<MessagePublisher> Publishers { get; set; } = new();
        public List<MessageConsumer> Consumers { get; set; } = new();
        public List<MessageEventSubscription> Subscriptions { get; set; } = new();
        public DateTime AnalyzedAt { get; set; }
        public int RepositoriesScanned { get; set; }
        public int ProjectsScanned { get; set; }
    }
}