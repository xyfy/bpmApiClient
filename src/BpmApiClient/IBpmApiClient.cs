using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BpmApiClient.Models;

namespace BpmApiClient
{
    /// <summary>
    /// BPM API 客户端接口，封装 BPM v95 系统所有 REST 接口。
    /// 调用方通过依赖注入获取此接口实例，无需关心 HTTP 细节。
    /// </summary>
    public interface IBpmApiClient
    {
        // ============================================================
        // 1 认证：获取令牌
        // ============================================================

        /// <summary>
        /// 获取访问令牌（AccessToken）。
        /// 对应规范 1.3：GET /oauth2/access-token?appid=&amp;secret=
        /// 令牌有效期约 30 分钟，客户端内部自动缓存，到期前自动刷新。
        /// </summary>
        Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

        // ============================================================
        // 2.2 启动流程
        // ============================================================

        /// <summary>
        /// 2.2 启动流程。
        /// POST /web-service/bpm/v95/process/init
        /// </summary>
        Task<InitProcessResult> InitProcessAsync(InitProcessRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.3 驱动流程（提交/退回）
        // ============================================================

        /// <summary>
        /// 2.3 驱动流程（提交 / 退回）。
        /// POST /web-service/bpm/v95/process/forward
        /// </summary>
        Task<ForwardProcessResult> ForwardProcessAsync(ForwardProcessRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.4 附件上传
        // ============================================================

        /// <summary>
        /// 2.4 附件上传（multipart/form-data）。
        /// POST /web-service/bpm/v95/formdata/upload-file
        /// </summary>
        /// <param name="wfId">流程实例 ID（与 taskId 至少填一个）</param>
        /// <param name="taskId">环节 ID（与 wfId 至少填一个）</param>
        /// <param name="user">当前操作人账号</param>
        /// <param name="increment">增量更新模式：0=替换（默认），1=增量</param>
        /// <param name="attachments">附件字典：key=组件标识（或 "def#rowInd#fileName"），value=(文件流, 文件名)</param>
        Task<IList<object>> UploadFileAsync(
            string wfId,
            string taskId,
            string user,
            int increment,
            IDictionary<string, (Stream Stream, string FileName)> attachments,
            CancellationToken cancellationToken = default);

        // ============================================================
        // 2.5 获取环节信息
        // ============================================================

        /// <summary>
        /// 2.5 获取环节信息。
        /// GET /web-service/bpm/v95/process/task-state?wfId=&amp;taskDef=
        /// </summary>
        Task<TaskStateResult> GetTaskStateAsync(string wfId, string taskDef, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.6 环节办理申请
        // ============================================================

        /// <summary>
        /// 2.6 环节办理申请。
        /// 在打开表单前调用，验证当前用户及环节是否允许办理。
        /// GET /web-service/bpm/v95/process/apply?taskId=&amp;asnUser=&amp;asnDate=
        /// </summary>
        Task ApplyTaskAsync(string taskId, string asnUser, string asnDate = null, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.7 流程撤回
        // ============================================================

        /// <summary>
        /// 2.7 流程撤回。
        /// POST /web-service/bpm/v95/maintain/workflow-backoff
        /// </summary>
        Task<BackoffResult> WorkflowBackoffAsync(WorkflowBackoffRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.8 流程取消
        // ============================================================

        /// <summary>
        /// 2.8 流程取消。
        /// POST /web-service/bpm/v95/maintain/cancelwf
        /// </summary>
        Task CancelWfAsync(CancelWfRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.9 流程环节回滚
        // ============================================================

        /// <summary>
        /// 2.9 流程环节回滚。
        /// POST /web-service/bpm/v95/process/rollback
        /// </summary>
        Task<BackoffResult> RollbackAsync(WorkflowBackoffRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.10 获取办理锁
        // ============================================================

        /// <summary>
        /// 2.10 获取办理锁。
        /// GET /web-service/bpm/v95/process/try-lock?taskId=&amp;opUser=
        /// </summary>
        Task TryLockAsync(ProcessLockRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.11 释放办理锁
        // ============================================================

        /// <summary>
        /// 2.11 释放办理锁。
        /// GET /web-service/bpm/v95/process/release-lock?taskId=&amp;opUser=
        /// </summary>
        Task ReleaseLockAsync(ProcessLockRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.12 预跑测试
        // ============================================================

        /// <summary>
        /// 2.12 预跑测试。
        /// POST /web-service/bpm/v95/trivial/task-linetest
        /// </summary>
        Task<object> TaskLinetestAsync(TaskLinetestRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.13 获取表单信息
        // ============================================================

        /// <summary>
        /// 2.13 获取表单信息（返回 Map 结构，字段因模板而异）。
        /// GET /web-service/bpm/v95/formdata/full-get?wfId=
        /// </summary>
        Task<object> GetFormDataAsync(string wfId, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.14 获取待办列表
        // ============================================================

        /// <summary>
        /// 2.14 获取待办列表。
        /// POST /web-service/bpm/v95/searchlist/toassign
        /// </summary>
        Task<object> GetPendingTasksAsync(PendingTaskSearchRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.15 表单赋值
        // ============================================================

        /// <summary>
        /// 2.15 表单赋值。
        /// POST /web-service/bpm/v95/formdata/update
        /// </summary>
        Task UpdateFormDataAsync(FormDataUpdateRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.16 获取流程实例列表（监控）
        // ============================================================

        /// <summary>
        /// 2.16 获取流程实例列表（监控视角）。
        /// POST /web-service/bpm/v95/searchlist/tomonitor
        /// </summary>
        Task<object> GetMonitorListAsync(MonitorSearchRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.17 我发起的列表
        // ============================================================

        /// <summary>
        /// 2.17 我发起的列表。
        /// POST /web-service/bpm/v95/searchlist/selfinit
        /// </summary>
        Task<object> GetSelfInitListAsync(SelfInitSearchRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.18 我经办的列表
        // ============================================================

        /// <summary>
        /// 2.18 我经办的列表。
        /// POST /web-service/bpm/v95/searchlist/selfprocess
        /// </summary>
        Task<object> GetSelfProcessListAsync(SelfProcessSearchRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.19 抄送我的列表
        // ============================================================

        /// <summary>
        /// 2.19 抄送我的列表。
        /// POST /web-service/bpm/v95/searchlist/tocc
        /// </summary>
        Task<object> GetToCcListAsync(ToCcSearchRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 2.20 流程实例详情
        // ============================================================

        /// <summary>
        /// 2.20 流程实例详情。
        /// GET /web-service/bpm/v95/process/wfdetail?wfId=
        /// </summary>
        Task<object> GetWfDetailAsync(string wfId, CancellationToken cancellationToken = default);

        // ============================================================
        // 3 辅助接口
        // ============================================================

        /// <summary>
        /// 3.1 检查已配置的角色（当前发布的模板）。
        /// GET /web-service/bpm/v95/trivial/template-check-rolecode?roleCode=
        /// </summary>
        Task<object> CheckRoleCodeAsync(string roleCode, CancellationToken cancellationToken = default);

        /// <summary>
        /// 3.2 获取流程历史列表。
        /// GET /web-service/bpm/v95/trivial/workflow-history?wfId=
        /// </summary>
        Task<object> GetWorkflowHistoryAsync(string wfId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 3.3 获取流程历史环节（办理人）。
        /// GET /web-service/bpm/v95/process/full-task-trace?wfId=
        /// </summary>
        Task<object> GetFullTaskTraceAsync(string wfId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 3.4 获取流程历史环节（层级）。
        /// GET /web-service/bpm/v95/process/full-tasklevel-trace?wfId=
        /// </summary>
        Task<object> GetFullTaskLevelTraceAsync(string wfId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 3.5 流程表单打印（下载）。
        /// POST /web-service/bpm/v95/trivial/print-wf?wfId=&amp;printUser=&amp;printDef=
        /// </summary>
        Task<byte[]> PrintWfAsync(string wfId, string printUser, string printDef, CancellationToken cancellationToken = default);

        /// <summary>
        /// 3.6 添加流程委托。
        /// POST /web-service/bpm/v95/trivial/delegate-addasn
        /// </summary>
        Task DelegateAddAsync(DelegateAddRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 3.7 取消流程委托。
        /// POST /web-service/bpm/v95/trivial/delegate-invalidasn
        /// </summary>
        Task DelegateInvalidAsync(DelegateInvalidRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 3.8 获取类别列表。
        /// POST /web-service/bpm/v95/trivial/groups
        /// </summary>
        Task<object> GetGroupsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 3.9 获取模板列表。
        /// POST /web-service/bpm/v95/trivial/templates
        /// </summary>
        Task<object> GetTemplatesAsync(TemplateSearchRequest request, CancellationToken cancellationToken = default);

        // ============================================================
        // 4 运维接口
        // ============================================================

        /// <summary>
        /// 4.1 变更流程状态。
        /// POST /web-service/bpm/v95/maintain/change-wfstatus
        /// </summary>
        Task ChangeWfStatusAsync(ChangeWfStatusRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 4.2 变更环节（人员）办理状态。
        /// POST /web-service/bpm/v95/maintain/change-taskstatus
        /// </summary>
        Task ChangeTaskStatusAsync(ChangeTaskStatusRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 4.3 变更环节（节点）状态。
        /// POST /web-service/bpm/v95/maintain/change-levelstatus
        /// </summary>
        Task ChangeLevelStatusAsync(ChangeLevelStatusRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 4.4 变更环节（节点）办理人。
        /// POST /web-service/bpm/v95/maintain/task-change-assignee
        /// </summary>
        Task TaskChangeAssigneeAsync(TaskChangeAssigneeRequest request, CancellationToken cancellationToken = default);
    }
}
