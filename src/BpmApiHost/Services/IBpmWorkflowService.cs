using System.Collections.Generic;
using BpmApiClient.Models;

namespace BpmApiHost.Services
{
    public interface IBpmWorkflowService
    {
        StartProcessResponse StartProcess(StartProcessRequest request);

        WorkflowTaskActionResponse ApproveTask(string taskId, WorkflowTaskActionRequest request);

        WorkflowTaskActionResponse RejectTask(string taskId, WorkflowTaskActionRequest request);

        ProcessDetailResponse GetProcess(string processInstanceId);

        IReadOnlyCollection<WorkflowTaskSummary> GetPendingTasks(string assignee);
    }
}
