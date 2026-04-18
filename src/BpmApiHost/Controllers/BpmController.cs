using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BpmApiClient;
using BpmApiClient.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BpmApiHost.Controllers
{
    /// <summary>
    /// BPM 流程 API 代理控制器。
    /// 将 HTTP 请求透传给 IBpmApiClient，实现与 BPM v95 系统的对接。
    /// 包含 20 个流程接口、9 个辅助接口、4 个运维接口，共 33 个端点。
    /// </summary>
    [ApiController]
    [Route("api/bpm")]
    public class BpmController : ControllerBase
    {
        /// <summary>BPM API 客户端（通过依赖注入注入）。</summary>
        private readonly IBpmApiClient _bpmClient;

        /// <summary>初始化控制器，注入 BPM 客户端。</summary>
        public BpmController(IBpmApiClient bpmClient)
        {
            _bpmClient = bpmClient ?? throw new ArgumentNullException(nameof(bpmClient));
        }

        // ============================================================
        // 1.3 获取令牌
        // ============================================================

        /// <summary>
        /// 获取 BPM 访问令牌（AccessToken）。
        /// GET /api/bpm/token
        /// 注意：此端点会将 BPM 后端令牌直接返回给调用方，仅供内部受信服务使用，
        /// 生产环境请确保已启用身份验证以防止令牌泄露。
        /// </summary>
        [Authorize]
        [HttpGet("token")]
        public async Task<IActionResult> GetToken()
        {
            var token = await _bpmClient.GetAccessTokenAsync();
            return Ok(new { access_token = token });
        }

        // ============================================================
        // 2.2 启动流程
        // ============================================================

        /// <summary>
        /// 启动一个新的 BPM 流程实例。
        /// POST /api/bpm/process/init
        /// </summary>
        [HttpPost("process/init")]
        public async Task<IActionResult> InitProcess([FromBody] InitProcessRequest request)
        {
            var result = await _bpmClient.InitProcessAsync(request);
            return Ok(result);
        }

        // ============================================================
        // 2.3 驱动流程（提交/退回）
        // ============================================================

        /// <summary>
        /// 驱动流程推进（提交）或退回。
        /// POST /api/bpm/process/forward
        /// </summary>
        [HttpPost("process/forward")]
        public async Task<IActionResult> ForwardProcess([FromBody] ForwardProcessRequest request)
        {
            var result = await _bpmClient.ForwardProcessAsync(request);
            return Ok(result);
        }

        // ============================================================
        // 2.4 附件上传
        // ============================================================

        /// <summary>
        /// 上传附件到指定流程/环节。
        /// POST /api/bpm/formdata/upload-file?wfId=&amp;taskId=&amp;user=&amp;increment=
        /// Content-Type: multipart/form-data
        /// </summary>
        [HttpPost("formdata/upload-file")]
        public async Task<IActionResult> UploadFile(
            [FromQuery] string wfId,
            [FromQuery] string taskId,
            [FromQuery] string user,
            [FromQuery] int increment = 0)
        {
            // 严格检查 Content-Type 必须为 multipart/form-data，避免：
            // 1. application/x-www-form-urlencoded 被 HasFormContentType 误放行
            // 2. multipart/mixed 等其他 multipart 子类型触发 Request.Form 解析异常
            if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var mediaType) ||
                !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                return StatusCode(415, "Content-Type 必须为 multipart/form-data。");

            // 先校验参数，再打开流，避免已打开的流因参数校验失败而泄漏
            if (string.IsNullOrWhiteSpace(wfId) && string.IsNullOrWhiteSpace(taskId))
                return BadRequest("wfId 和 taskId 至少填一个。");

            if (!Request.Form.Files.Any())
                return BadRequest("至少需要上传一个文件。");

            var attachments = new Dictionary<string, (Stream, string)>();
            try
            {
                foreach (var file in Request.Form.Files)
                {
                    if (attachments.ContainsKey(file.Name))
                        return BadRequest($"存在重复的文件字段名：{file.Name}，请确保每个文件使用唯一的字段名。");
                    attachments[file.Name] = (file.OpenReadStream(), file.FileName);
                }

                var result = await _bpmClient.UploadFileAsync(wfId, taskId, user, increment, attachments);
                return Ok(result);
            }
            finally
            {
                foreach (var kv in attachments.Values)
                    kv.Item1?.Dispose();
            }
        }

        // ============================================================
        // 2.5 获取环节信息
        // ============================================================

        /// <summary>
        /// 获取流程中指定环节的状态和办理人信息。
        /// GET /api/bpm/process/task-state?wfId=&amp;taskDef=
        /// </summary>
        [HttpGet("process/task-state")]
        public async Task<IActionResult> GetTaskState([FromQuery] string wfId, [FromQuery] string taskDef)
        {
            var result = await _bpmClient.GetTaskStateAsync(wfId, taskDef);
            return Ok(result);
        }

        // ============================================================
        // 2.6 环节办理申请
        // ============================================================

        /// <summary>
        /// 环节办理申请（打开表单前调用，验证办理权限）。
        /// GET /api/bpm/process/apply?taskId=&amp;asnUser=&amp;asnDate=
        /// </summary>
        [HttpGet("process/apply")]
        public async Task<IActionResult> ApplyTask(
            [FromQuery] string taskId,
            [FromQuery] string asnUser,
            [FromQuery] string asnDate = null)
        {
            await _bpmClient.ApplyTaskAsync(taskId, asnUser, asnDate);
            return Ok();
        }

        // ============================================================
        // 2.7 流程撤回
        // ============================================================

        /// <summary>
        /// 将流程实例撤回到指定环节。
        /// POST /api/bpm/maintain/workflow-backoff?wfId=&amp;opUser=&amp;taskId=
        /// </summary>
        [Authorize]
        [HttpPost("maintain/workflow-backoff")]
        public async Task<IActionResult> WorkflowBackoff([FromBody] WorkflowBackoffRequest request)
        {
            var result = await _bpmClient.WorkflowBackoffAsync(request);
            return Ok(result);
        }

        // ============================================================
        // 2.8 流程取消
        // ============================================================

        /// <summary>
        /// 取消一个进行中的流程实例。
        /// POST /api/bpm/maintain/cancelwf
        /// </summary>
        [Authorize]
        [HttpPost("maintain/cancelwf")]
        public async Task<IActionResult> CancelWf([FromBody] CancelWfRequest request)
        {
            await _bpmClient.CancelWfAsync(request);
            return Ok();
        }

        // ============================================================
        // 2.9 流程环节回滚
        // ============================================================

        /// <summary>
        /// 将流程环节回滚到之前的状态。
        /// POST /api/bpm/process/rollback
        /// </summary>
        [HttpPost("process/rollback")]
        public async Task<IActionResult> Rollback([FromBody] WorkflowBackoffRequest request)
        {
            var result = await _bpmClient.RollbackAsync(request);
            return Ok(result);
        }

        // ============================================================
        // 2.10 获取办理锁
        // ============================================================

        /// <summary>
        /// 获取指定环节的办理锁，防止并发操作。
        /// GET /api/bpm/process/try-lock?taskId=&amp;opUser=
        /// </summary>
        [HttpGet("process/try-lock")]
        public async Task<IActionResult> TryLock([FromQuery] string taskId, [FromQuery] string opUser)
        {
            await _bpmClient.TryLockAsync(new ProcessLockRequest { TaskId = taskId, OpUser = opUser });
            return Ok();
        }

        // ============================================================
        // 2.11 释放办理锁
        // ============================================================

        /// <summary>
        /// 释放已持有的环节办理锁。
        /// GET /api/bpm/process/release-lock?taskId=&amp;opUser=
        /// </summary>
        [HttpGet("process/release-lock")]
        public async Task<IActionResult> ReleaseLock([FromQuery] string taskId, [FromQuery] string opUser)
        {
            await _bpmClient.ReleaseLockAsync(new ProcessLockRequest { TaskId = taskId, OpUser = opUser });
            return Ok();
        }

        // ============================================================
        // 2.12 预跑测试
        // ============================================================

        /// <summary>
        /// 对指定环节发起预跑测试。
        /// POST /api/bpm/trivial/task-linetest
        /// </summary>
        [HttpPost("trivial/task-linetest")]
        public async Task<IActionResult> TaskLinetest([FromBody] TaskLinetestRequest request)
        {
            var result = await _bpmClient.TaskLinetestAsync(request);
            return Ok(result);
        }

        // ============================================================
        // 2.13 获取表单信息
        // ============================================================

        /// <summary>
        /// 获取流程实例的完整表单信息。
        /// GET /api/bpm/formdata/full-get?wfId=
        /// </summary>
        [HttpGet("formdata/full-get")]
        public async Task<IActionResult> GetFormData([FromQuery] string wfId)
        {
            var result = await _bpmClient.GetFormDataAsync(wfId);
            return Ok(result);
        }

        // ============================================================
        // 2.14 获取待办列表
        // ============================================================

        /// <summary>
        /// 查询指定办理人的待办任务列表（分页）。
        /// POST /api/bpm/searchlist/toassign
        /// </summary>
        [HttpPost("searchlist/toassign")]
        public async Task<IActionResult> GetPendingTasks([FromBody] PendingTaskSearchRequest request)
        {
            var result = await _bpmClient.GetPendingTasksAsync(request);
            return Ok(result);
        }

        // ============================================================
        // 2.15 表单赋值
        // ============================================================

        /// <summary>
        /// 更新进行中流程的表单字段值。
        /// POST /api/bpm/formdata/update
        /// </summary>
        [HttpPost("formdata/update")]
        public async Task<IActionResult> UpdateFormData([FromBody] FormDataUpdateRequest request)
        {
            await _bpmClient.UpdateFormDataAsync(request);
            return Ok();
        }

        // ============================================================
        // 2.16 获取流程实例列表（监控）
        // ============================================================

        /// <summary>
        /// 以监控视角分页查询流程实例列表。
        /// POST /api/bpm/searchlist/tomonitor
        /// </summary>
        [HttpPost("searchlist/tomonitor")]
        public async Task<IActionResult> GetMonitorList([FromBody] MonitorSearchRequest request)
        {
            var result = await _bpmClient.GetMonitorListAsync(request);
            return Ok(result);
        }

        // ============================================================
        // 2.17 我发起的列表
        // ============================================================

        /// <summary>
        /// 查询指定用户发起的流程列表（分页）。
        /// POST /api/bpm/searchlist/selfinit
        /// </summary>
        [HttpPost("searchlist/selfinit")]
        public async Task<IActionResult> GetSelfInitList([FromBody] SelfInitSearchRequest request)
        {
            var result = await _bpmClient.GetSelfInitListAsync(request);
            return Ok(result);
        }

        // ============================================================
        // 2.18 我经办的列表
        // ============================================================

        /// <summary>
        /// 查询指定用户已经办的流程列表（分页）。
        /// POST /api/bpm/searchlist/selfprocess
        /// </summary>
        [HttpPost("searchlist/selfprocess")]
        public async Task<IActionResult> GetSelfProcessList([FromBody] SelfProcessSearchRequest request)
        {
            var result = await _bpmClient.GetSelfProcessListAsync(request);
            return Ok(result);
        }

        // ============================================================
        // 2.19 抄送我的列表
        // ============================================================

        /// <summary>
        /// 查询抄送给指定用户的流程列表（分页）。
        /// POST /api/bpm/searchlist/tocc
        /// </summary>
        [HttpPost("searchlist/tocc")]
        public async Task<IActionResult> GetToCcList([FromBody] ToCcSearchRequest request)
        {
            var result = await _bpmClient.GetToCcListAsync(request);
            return Ok(result);
        }

        // ============================================================
        // 2.20 流程实例详情
        // ============================================================

        /// <summary>
        /// 获取流程实例的完整详情。
        /// GET /api/bpm/process/wfdetail?wfId=
        /// </summary>
        [HttpGet("process/wfdetail")]
        public async Task<IActionResult> GetWfDetail([FromQuery] string wfId)
        {
            var result = await _bpmClient.GetWfDetailAsync(wfId);
            return Ok(result);
        }

        // ============================================================
        // 3.1 检查已配置的角色
        // ============================================================

        /// <summary>
        /// 检查当前发布模板中是否配置了指定角色编号。
        /// GET /api/bpm/trivial/template-check-rolecode?roleCode=
        /// </summary>
        [HttpGet("trivial/template-check-rolecode")]
        public async Task<IActionResult> CheckRoleCode([FromQuery] string roleCode)
        {
            var result = await _bpmClient.CheckRoleCodeAsync(roleCode);
            return Ok(result);
        }

        // ============================================================
        // 3.2 获取流程历史列表
        // ============================================================

        /// <summary>
        /// 获取指定流程实例的审批历史记录列表。
        /// GET /api/bpm/trivial/workflow-history?wfId=
        /// </summary>
        [HttpGet("trivial/workflow-history")]
        public async Task<IActionResult> GetWorkflowHistory([FromQuery] string wfId)
        {
            var result = await _bpmClient.GetWorkflowHistoryAsync(wfId);
            return Ok(result);
        }

        // ============================================================
        // 3.3 获取流程历史环节（办理人）
        // ============================================================

        /// <summary>
        /// 获取流程所有历史环节的办理人信息。
        /// GET /api/bpm/process/full-task-trace?wfId=
        /// </summary>
        [HttpGet("process/full-task-trace")]
        public async Task<IActionResult> GetFullTaskTrace([FromQuery] string wfId)
        {
            var result = await _bpmClient.GetFullTaskTraceAsync(wfId);
            return Ok(result);
        }

        // ============================================================
        // 3.4 获取流程历史环节（层级）
        // ============================================================

        /// <summary>
        /// 按层级获取流程所有历史环节信息。
        /// GET /api/bpm/process/full-tasklevel-trace?wfId=
        /// </summary>
        [HttpGet("process/full-tasklevel-trace")]
        public async Task<IActionResult> GetFullTaskLevelTrace([FromQuery] string wfId)
        {
            var result = await _bpmClient.GetFullTaskLevelTraceAsync(wfId);
            return Ok(result);
        }

        // ============================================================
        // 3.5 流程表单打印（下载）
        // ============================================================

        /// <summary>
        /// 下载指定流程的表单打印文件。
        /// POST /api/bpm/trivial/print-wf?wfId=&amp;printUser=&amp;printDef=
        /// </summary>
        [HttpPost("trivial/print-wf")]
        public async Task<IActionResult> PrintWf(
            [FromQuery] string wfId,
            [FromQuery] string printUser,
            [FromQuery] string printDef)
        {
            var fileBytes = await _bpmClient.PrintWfAsync(wfId, printUser, printDef);
            return File(fileBytes, "application/octet-stream", $"print_{wfId}.pdf");
        }

        // ============================================================
        // 3.6 添加流程委托
        // ============================================================

        /// <summary>
        /// 为指定用户添加流程委托配置。
        /// POST /api/bpm/trivial/delegate-addasn
        /// </summary>
        [HttpPost("trivial/delegate-addasn")]
        public async Task<IActionResult> DelegateAdd([FromBody] DelegateAddRequest request)
        {
            await _bpmClient.DelegateAddAsync(request);
            return Ok();
        }

        // ============================================================
        // 3.7 取消流程委托
        // ============================================================

        /// <summary>
        /// 取消指定委托关系。
        /// POST /api/bpm/trivial/delegate-invalidasn
        /// </summary>
        [HttpPost("trivial/delegate-invalidasn")]
        public async Task<IActionResult> DelegateInvalid([FromBody] DelegateInvalidRequest request)
        {
            await _bpmClient.DelegateInvalidAsync(request);
            return Ok();
        }

        // ============================================================
        // 3.8 获取类别列表
        // ============================================================

        /// <summary>
        /// 获取所有流程类别列表。
        /// GET /api/bpm/trivial/groups
        /// </summary>
        [HttpGet("trivial/groups")]
        public async Task<IActionResult> GetGroups()
        {
            var result = await _bpmClient.GetGroupsAsync();
            return Ok(result);
        }

        // ============================================================
        // 3.9 获取模板列表
        // ============================================================

        /// <summary>
        /// 获取指定类别下的流程模板列表。
        /// POST /api/bpm/trivial/templates
        /// </summary>
        [HttpPost("trivial/templates")]
        public async Task<IActionResult> GetTemplates([FromBody] TemplateSearchRequest request)
        {
            var result = await _bpmClient.GetTemplatesAsync(request);
            return Ok(result);
        }

        // ============================================================
        // 4.1 变更流程状态
        // ============================================================

        /// <summary>
        /// 强制变更流程实例状态（运维用途）。
        /// POST /api/bpm/maintain/change-wfstatus
        /// </summary>
        [Authorize]
        [HttpPost("maintain/change-wfstatus")]
        public async Task<IActionResult> ChangeWfStatus([FromBody] ChangeWfStatusRequest request)
        {
            await _bpmClient.ChangeWfStatusAsync(request);
            return Ok();
        }

        // ============================================================
        // 4.2 变更环节（人员）办理状态
        // ============================================================

        /// <summary>
        /// 强制变更指定人员环节的办理状态（运维用途）。
        /// POST /api/bpm/maintain/change-taskstatus
        /// </summary>
        [Authorize]
        [HttpPost("maintain/change-taskstatus")]
        public async Task<IActionResult> ChangeTaskStatus([FromBody] ChangeTaskStatusRequest request)
        {
            await _bpmClient.ChangeTaskStatusAsync(request);
            return Ok();
        }

        // ============================================================
        // 4.3 变更环节（节点）状态
        // ============================================================

        /// <summary>
        /// 强制变更整个层级节点的状态（运维用途）。
        /// POST /api/bpm/maintain/change-levelstatus
        /// </summary>
        [Authorize]
        [HttpPost("maintain/change-levelstatus")]
        public async Task<IActionResult> ChangeLevelStatus([FromBody] ChangeLevelStatusRequest request)
        {
            await _bpmClient.ChangeLevelStatusAsync(request);
            return Ok();
        }

        // ============================================================
        // 4.4 变更环节（节点）办理人
        // ============================================================

        /// <summary>
        /// 变更指定环节的办理人（运维用途）。
        /// POST /api/bpm/maintain/task-change-assignee
        /// </summary>
        [Authorize]
        [HttpPost("maintain/task-change-assignee")]
        public async Task<IActionResult> TaskChangeAssignee([FromBody] TaskChangeAssigneeRequest request)
        {
            await _bpmClient.TaskChangeAssigneeAsync(request);
            return Ok();
        }
    }
}
