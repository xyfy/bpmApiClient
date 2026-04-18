using System;
using System.Collections.Generic;

namespace BpmApiClient.Models
{
    public class StartProcessRequest
    {
        public string ProcessDefinitionKey { get; set; } = string.Empty;

        public string Initiator { get; set; } = string.Empty;

        public IDictionary<string, object> Variables { get; set; } = new Dictionary<string, object>();
    }

    public class StartProcessResponse
    {
        public string ProcessInstanceId { get; set; } = string.Empty;

        public string ProcessDefinitionKey { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }

    public class WorkflowTaskActionRequest
    {
        public string Operator { get; set; } = string.Empty;

        public string Comment { get; set; } = string.Empty;

        public IDictionary<string, object> Variables { get; set; } = new Dictionary<string, object>();
    }

    public class WorkflowTaskActionResponse
    {
        public string TaskId { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string Result { get; set; } = string.Empty;

        public DateTime OperatedAt { get; set; }
    }

    public class WorkflowTaskSummary
    {
        public string TaskId { get; set; } = string.Empty;

        public string ProcessInstanceId { get; set; } = string.Empty;

        public string TaskName { get; set; } = string.Empty;

        public string Assignee { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }

    public class ProcessDetailResponse
    {
        public string ProcessInstanceId { get; set; } = string.Empty;

        public string ProcessDefinitionKey { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string Initiator { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public IList<WorkflowTaskSummary> Tasks { get; set; } = new List<WorkflowTaskSummary>();
    }

    public class BpmApiClientOptions
    {
        public string BaseUrl { get; set; } = string.Empty;

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    }
}
