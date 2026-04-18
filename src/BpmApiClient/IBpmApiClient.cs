using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BpmApiClient.Models;

namespace BpmApiClient
{
    public interface IBpmApiClient
    {
        Task<StartProcessResponse> StartProcessAsync(StartProcessRequest request, CancellationToken cancellationToken = default);

        Task<WorkflowTaskActionResponse> ApproveTaskAsync(string taskId, WorkflowTaskActionRequest request, CancellationToken cancellationToken = default);

        Task<WorkflowTaskActionResponse> RejectTaskAsync(string taskId, WorkflowTaskActionRequest request, CancellationToken cancellationToken = default);

        Task<ProcessDetailResponse> GetProcessAsync(string processInstanceId, CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<WorkflowTaskSummary>> GetPendingTasksAsync(string assignee, CancellationToken cancellationToken = default);
    }
}
