using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BpmApiClient.Models;

namespace BpmApiHost.Services
{
    public class InMemoryBpmWorkflowService : IBpmWorkflowService
    {
        private readonly ConcurrentDictionary<string, ProcessDetailResponse> _processes = new ConcurrentDictionary<string, ProcessDetailResponse>();

        public StartProcessResponse StartProcess(StartProcessRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.ProcessDefinitionKey))
            {
                throw new ArgumentException("ProcessDefinitionKey is required.", nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Initiator))
            {
                throw new ArgumentException("Initiator is required.", nameof(request));
            }

            var processInstanceId = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow;
            var firstTask = new WorkflowTaskSummary
            {
                TaskId = Guid.NewGuid().ToString("N"),
                ProcessInstanceId = processInstanceId,
                TaskName = "Approval",
                Assignee = request.Initiator,
                Status = "Pending",
                CreatedAt = now
            };

            var process = new ProcessDetailResponse
            {
                ProcessInstanceId = processInstanceId,
                ProcessDefinitionKey = request.ProcessDefinitionKey,
                Initiator = request.Initiator,
                Status = "Running",
                CreatedAt = now,
                Tasks = new List<WorkflowTaskSummary> { firstTask }
            };

            _processes[processInstanceId] = process;

            return new StartProcessResponse
            {
                ProcessInstanceId = processInstanceId,
                ProcessDefinitionKey = request.ProcessDefinitionKey,
                Status = process.Status,
                CreatedAt = now
            };
        }

        public WorkflowTaskActionResponse ApproveTask(string taskId, WorkflowTaskActionRequest request)
        {
            return HandleTaskAction(taskId, request, "Approve", "Approved", "Completed");
        }

        public WorkflowTaskActionResponse RejectTask(string taskId, WorkflowTaskActionRequest request)
        {
            return HandleTaskAction(taskId, request, "Reject", "Rejected", "Rejected");
        }

        public ProcessDetailResponse GetProcess(string processInstanceId)
        {
            if (string.IsNullOrWhiteSpace(processInstanceId))
            {
                throw new ArgumentException("ProcessInstanceId is required.", nameof(processInstanceId));
            }

            if (_processes.TryGetValue(processInstanceId, out var process))
            {
                return process;
            }

            throw new KeyNotFoundException($"Process {processInstanceId} not found.");
        }

        public IReadOnlyCollection<WorkflowTaskSummary> GetPendingTasks(string assignee)
        {
            if (string.IsNullOrWhiteSpace(assignee))
            {
                throw new ArgumentException("Assignee is required.", nameof(assignee));
            }

            return _processes.Values
                .SelectMany(p => p.Tasks)
                .Where(t => string.Equals(t.Assignee, assignee, StringComparison.OrdinalIgnoreCase) && string.Equals(t.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private WorkflowTaskActionResponse HandleTaskAction(string taskId, WorkflowTaskActionRequest request, string action, string taskResult, string processResult)
        {
            if (string.IsNullOrWhiteSpace(taskId))
            {
                throw new ArgumentException("TaskId is required.", nameof(taskId));
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            foreach (var process in _processes.Values)
            {
                var task = process.Tasks.FirstOrDefault(t => string.Equals(t.TaskId, taskId, StringComparison.Ordinal));
                if (task == null)
                {
                    continue;
                }

                task.Status = taskResult;
                process.Status = processResult;

                return new WorkflowTaskActionResponse
                {
                    TaskId = taskId,
                    Action = action,
                    Result = taskResult,
                    OperatedAt = DateTime.UtcNow
                };
            }

            throw new KeyNotFoundException($"Task {taskId} not found.");
        }
    }
}
