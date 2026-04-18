using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BpmApiClient.Models
{
    // ============================================================
    // 公共包装类
    // ============================================================

    /// <summary>
    /// BPM 统一响应包装体。
    /// 所有接口均以此结构返回：code=0 表示成功，其余表示异常。
    /// </summary>
    /// <typeparam name="T">业务数据类型</typeparam>
    public class BpmApiResponse<T>
    {
        /// <summary>返回码。0=成功，其他=异常。</summary>
        [JsonProperty("code")]
        public int Code { get; set; }

        /// <summary>异常信息，成功时为 null。</summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>业务数据，失败时为 null。</summary>
        [JsonProperty("result")]
        public T Result { get; set; }
    }

    /// <summary>无业务数据的通用响应（部分接口 result 为空对象）。</summary>
    public class BpmApiResponse : BpmApiResponse<object> { }

    // ============================================================
    // 客户端配置
    // ============================================================

    /// <summary>
    /// BPM API 客户端配置项。
    /// 对应 appsettings.json 中 BpmApiClient 节点。
    /// </summary>
    public class BpmApiClientOptions
    {
        /// <summary>BPM 服务器目标地址（见规范 1.2.1）。</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>appId，由 BPM 管理员分配（见规范 1.2.2）。</summary>
        public string AppId { get; set; } = string.Empty;

        /// <summary>密钥，由 BPM 管理员分配（见规范 1.2.2）。</summary>
        public string Secret { get; set; } = string.Empty;

        /// <summary>HTTP 请求超时时间，默认 30 秒。</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    // ============================================================
    // 获取令牌（1.3 获取令牌）
    // ============================================================

    /// <summary>
    /// 获取访问令牌（AccessToken）的响应结果。
    /// 令牌有效期约 30 分钟，每次接口调用须携带此令牌。
    /// </summary>
    public class TokenResult
    {
        /// <summary>JWT 格式的访问令牌字符串。</summary>
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        /// <summary>令牌类型，通常为 bearer。</summary>
        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        /// <summary>过期秒数（单位：秒）。</summary>
        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
    }

    // ============================================================
    // 公共子对象
    // ============================================================

    /// <summary>表单组件赋值项（formData 元素）。</summary>
    public class FormDataItem
    {
        /// <summary>组件自定义标识（组件的 def 字段）。</summary>
        [JsonProperty("def")]
        public string Def { get; set; }

        /// <summary>
        /// 组件值。多选/级联组件多个值用 "####" 分隔；
        /// 选人组件 HR_LINK 数据加前缀 "LINK#"。
        /// </summary>
        [JsonProperty("val")]
        public string Val { get; set; }
    }

    /// <summary>数据方阵行赋值项（gridData.val 的元素）。</summary>
    public class GridRowItem
    {
        /// <summary>行序号（从 1 开始）。</summary>
        [JsonProperty("rowInd")]
        public int RowInd { get; set; }

        /// <summary>
        /// 列赋值键值对：列组件自定义标识 → 赋值。
        /// 例如 { "gd_str": "名称", "gd_num": "0.21" }。
        /// </summary>
        [JsonProperty("cells")]
        public IDictionary<string, string> Cells { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>数据方阵整体赋值项（gridData 元素）。</summary>
    public class GridDataItem
    {
        /// <summary>数据方阵组件自定义标识。</summary>
        [JsonProperty("def")]
        public string Def { get; set; }

        /// <summary>数据方阵行列表。</summary>
        [JsonProperty("val")]
        public IList<GridRowItem> Val { get; set; } = new List<GridRowItem>();
    }

    /// <summary>环节经办人员指定项（assigneeData 元素）。</summary>
    public class AssigneeDataItem
    {
        /// <summary>环节自定义标识（在环节备注栏查看）。</summary>
        [JsonProperty("def")]
        public string Def { get; set; }

        /// <summary>环节办理人员；多个用 "####" 分隔。</summary>
        [JsonProperty("val")]
        public string Val { get; set; }
    }

    /// <summary>下一环节信息（nextList 中的元素）。</summary>
    public class NextTaskInfo
    {
        /// <summary>环节名称。</summary>
        [JsonProperty("taskName")]
        public string TaskName { get; set; }

        /// <summary>环节层级自定义标识。</summary>
        [JsonProperty("taskDef")]
        public string TaskDef { get; set; }

        /// <summary>环节业务标识，来自基础数据 BPM_TASK_BIZ_TAG。</summary>
        [JsonProperty("bizTag")]
        public string BizTag { get; set; }

        /// <summary>
        /// 驱动类型：PARALLEL=会签，EXCLUSIVE=或签，
        /// SEQUENCE=顺序签，PREEMPT=抢占签。
        /// </summary>
        [JsonProperty("driveType")]
        public string DriveType { get; set; }

        /// <summary>环节 ID。</summary>
        [JsonProperty("taskId")]
        public string TaskId { get; set; }

        /// <summary>
        /// 环节类型：COUNTERSIGN=意见征询/内部会签，
        /// NORMAL=审批环节，START=开始环节，DELEGATE=委托办理，BACK=退回。
        /// </summary>
        [JsonProperty("taskType")]
        public string TaskType { get; set; }

        /// <summary>环节办理人姓名。</summary>
        [JsonProperty("assigeeName")]
        public string AssigneeName { get; set; }

        /// <summary>环节办理人 ID（Link 字段）。</summary>
        [JsonProperty("assigneeLink")]
        public string AssigneeLink { get; set; }

        /// <summary>环节办理人账号。</summary>
        [JsonProperty("assigneeAccount")]
        public string AssigneeAccount { get; set; }

        /// <summary>环节办理人直接上级部门编码。</summary>
        [JsonProperty("assigneeOrg")]
        public string AssigneeOrg { get; set; }

        /// <summary>退回到目标环节 ID（仅退回场景有值）。</summary>
        [JsonProperty("backTgTaskId")]
        public string BackTgTaskId { get; set; }

        /// <summary>退回到目标环节层级（仅退回场景有值）。</summary>
        [JsonProperty("backTgTaskLevel")]
        public int? BackTgTaskLevel { get; set; }

        /// <summary>流程实例 ID（仅退回场景有值）。</summary>
        [JsonProperty("wfId")]
        public string WfId { get; set; }

        /// <summary>流程状态（仅退回场景有值）。</summary>
        [JsonProperty("wfStatus")]
        public int? WfStatus { get; set; }

        /// <summary>环节层级（仅退回场景有值）。</summary>
        [JsonProperty("taskLevel")]
        public int? TaskLevel { get; set; }
    }

    /// <summary>子流程信息（subWfList 中的元素）。</summary>
    public class SubWfInfo
    {
        /// <summary>子流程实例 ID。</summary>
        [JsonProperty("wfId")]
        public string WfId { get; set; }

        /// <summary>子流程模板自定义标识。</summary>
        [JsonProperty("wfDef")]
        public string WfDef { get; set; }

        /// <summary>发起人账号。</summary>
        [JsonProperty("initUserAccount")]
        public string InitUserAccount { get; set; }

        /// <summary>发起人 ID。</summary>
        [JsonProperty("initUser")]
        public string InitUser { get; set; }

        /// <summary>分支编码；若按数据方阵拆分则默认为行数。</summary>
        [JsonProperty("forkKey")]
        public string ForkKey { get; set; }

        /// <summary>子流程下一环节列表。</summary>
        [JsonProperty("taskList")]
        public IList<NextTaskInfo> TaskList { get; set; } = new List<NextTaskInfo>();
    }

    // ============================================================
    // 2.2 启动流程（/process/init）
    // ============================================================

    /// <summary>启动流程请求体。</summary>
    public class InitProcessRequest
    {
        /// <summary>流程模板自定义标识（必填）。</summary>
        [JsonProperty("wfDef")]
        public string WfDef { get; set; }

        /// <summary>流程发起人账号（必填）。</summary>
        [JsonProperty("initUser")]
        public string InitUser { get; set; }

        /// <summary>开始环节是否自动流转到审批环节，默认 false。</summary>
        [JsonProperty("forward", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Forward { get; set; }

        /// <summary>流程名称（可选）。</summary>
        [JsonProperty("wfDesc", NullValueHandling = NullValueHandling.Ignore)]
        public string WfDesc { get; set; }

        /// <summary>流程编码（门户，可选）。</summary>
        [JsonProperty("instCode", NullValueHandling = NullValueHandling.Ignore)]
        public string InstCode { get; set; }

        /// <summary>
        /// 回避列表：办理过程中需回避的用户 ID，多个用 "####" 分隔。
        /// </summary>
        [JsonProperty("avoiders", NullValueHandling = NullValueHandling.Ignore)]
        public string Avoiders { get; set; }

        /// <summary>表单组件赋值（可选）。</summary>
        [JsonProperty("formData", NullValueHandling = NullValueHandling.Ignore)]
        public IList<FormDataItem> FormData { get; set; }

        /// <summary>数据方阵赋值（可选）。</summary>
        [JsonProperty("gridData", NullValueHandling = NullValueHandling.Ignore)]
        public IList<GridDataItem> GridData { get; set; }

        /// <summary>环节经办人员指定（可选）。</summary>
        [JsonProperty("assigneeData", NullValueHandling = NullValueHandling.Ignore)]
        public IList<AssigneeDataItem> AssigneeData { get; set; }
    }

    /// <summary>启动流程返回的 result 数据。</summary>
    public class InitProcessResult
    {
        /// <summary>流程实例 ID。</summary>
        [JsonProperty("wfId")]
        public string WfId { get; set; }

        /// <summary>
        /// 流程状态：11=未启动，1=进行中，3=结束，13=异常结束。
        /// </summary>
        [JsonProperty("wfStatus")]
        public int WfStatus { get; set; }

        /// <summary>开始环节 ID。</summary>
        [JsonProperty("startTaskId")]
        public string StartTaskId { get; set; }

        /// <summary>下一环节列表。</summary>
        [JsonProperty("nextList")]
        public IList<NextTaskInfo> NextList { get; set; } = new List<NextTaskInfo>();

        /// <summary>执行过程信息（Map 结构，扩展字段）。</summary>
        [JsonProperty("reObj")]
        public IDictionary<string, object> ReObj { get; set; }
    }

    // ============================================================
    // 2.3 驱动流程（提交）（/process/forward）
    // ============================================================

    /// <summary>驱动流程（提交/退回）请求体。</summary>
    public class ForwardProcessRequest
    {
        /// <summary>当前环节 ID（必填）。</summary>
        [JsonProperty("taskId")]
        public string TaskId { get; set; }

        /// <summary>流程办理人账号（必填）。</summary>
        [JsonProperty("asnUser")]
        public string AsnUser { get; set; }

        /// <summary>意见编码：0=不同意，1=同意（可选）。</summary>
        [JsonProperty("approveCode", NullValueHandling = NullValueHandling.Ignore)]
        public string ApproveCode { get; set; }

        /// <summary>意见描述（可选）。</summary>
        [JsonProperty("approveDesc", NullValueHandling = NullValueHandling.Ignore)]
        public string ApproveDesc { get; set; }

        /// <summary>签批模式：默认空，C=意见征询，D=委托办理。</summary>
        [JsonProperty("cosignFlag", NullValueHandling = NullValueHandling.Ignore)]
        public string CosignFlag { get; set; }

        /// <summary>征询/委托人；cosignFlag 不为空时必填，委托办理只取第一个用户。</summary>
        [JsonProperty("cosignUser", NullValueHandling = NullValueHandling.Ignore)]
        public string CosignUser { get; set; }

        /// <summary>
        /// 退回到：0=不退回（默认），1=退回到办理人，2=退回到环节（层级）。
        /// </summary>
        [JsonProperty("backType", NullValueHandling = NullValueHandling.Ignore)]
        public int? BackType { get; set; }

        /// <summary>
        /// 退回到的环节列表：格式为 "流程ID@环节ID"，多个用 "####" 分隔。
        /// </summary>
        [JsonProperty("backTasks", NullValueHandling = NullValueHandling.Ignore)]
        public string BackTasks { get; set; }

        /// <summary>退回到办理人范围：ALL=全部，HIS=历史（默认 HIS）。</summary>
        [JsonProperty("backDispatcher", NullValueHandling = NullValueHandling.Ignore)]
        public string BackDispatcher { get; set; }

        /// <summary>表单组件赋值（可选）。</summary>
        [JsonProperty("formData", NullValueHandling = NullValueHandling.Ignore)]
        public IList<FormDataItem> FormData { get; set; }

        /// <summary>数据方阵赋值（可选）。</summary>
        [JsonProperty("gridData", NullValueHandling = NullValueHandling.Ignore)]
        public IList<GridDataItem> GridData { get; set; }

        /// <summary>环节经办人员指定（可选）。</summary>
        [JsonProperty("assigneeData", NullValueHandling = NullValueHandling.Ignore)]
        public IList<AssigneeDataItem> AssigneeData { get; set; }
    }

    /// <summary>驱动流程（提交）返回的 result 数据。</summary>
    public class ForwardProcessResult
    {
        /// <summary>流程实例 ID。</summary>
        [JsonProperty("wfId")]
        public string WfId { get; set; }

        /// <summary>
        /// 环节状态：6=完成，1=待办，3=办理中。
        /// </summary>
        [JsonProperty("taskStatus")]
        public int TaskStatus { get; set; }

        /// <summary>
        /// 流程状态：11=未启动，1=进行中，3=结束，13=异常结束。
        /// </summary>
        [JsonProperty("wfStatus")]
        public int WfStatus { get; set; }

        /// <summary>下一环节列表。</summary>
        [JsonProperty("nextList")]
        public IList<NextTaskInfo> NextList { get; set; } = new List<NextTaskInfo>();

        /// <summary>子流程信息列表。</summary>
        [JsonProperty("subWfList")]
        public IList<SubWfInfo> SubWfList { get; set; } = new List<SubWfInfo>();

        /// <summary>执行过程信息（Map 结构，扩展字段）。</summary>
        [JsonProperty("reObj")]
        public IDictionary<string, object> ReObj { get; set; }
    }

    // ============================================================
    // 2.5 获取环节信息（/process/task-state）
    // ============================================================

    /// <summary>环节办理详情（taskList 中的元素）。</summary>
    public class TaskStateItem
    {
        /// <summary>环节名称。</summary>
        [JsonProperty("taskName")]
        public string TaskName { get; set; }

        /// <summary>环节层级自定义标识。</summary>
        [JsonProperty("taskDef")]
        public string TaskDef { get; set; }

        /// <summary>环节业务标识。</summary>
        [JsonProperty("bizTag")]
        public string BizTag { get; set; }

        /// <summary>环节 ID。</summary>
        [JsonProperty("taskId")]
        public string TaskId { get; set; }

        /// <summary>环节类型。</summary>
        [JsonProperty("taskType")]
        public string TaskType { get; set; }

        /// <summary>
        /// 环节状态：1=待办，3=办理中，6=已完成，-1=未到达，11=已取消。
        /// </summary>
        [JsonProperty("status")]
        public int Status { get; set; }

        /// <summary>办理人姓名。</summary>
        [JsonProperty("assigeeName")]
        public string AssigneeName { get; set; }

        /// <summary>办理人 ID（Link 字段）。</summary>
        [JsonProperty("assigneeLink")]
        public string AssigneeLink { get; set; }

        /// <summary>办理人账号。</summary>
        [JsonProperty("assigneeAccount")]
        public string AssigneeAccount { get; set; }

        /// <summary>办理人上级部门编码。</summary>
        [JsonProperty("assigneeOrg")]
        public string AssigneeOrg { get; set; }

        /// <summary>环节办理截止日期（格式 yyyy-MM-dd HH:mm:ss）。</summary>
        [JsonProperty("deadlineTime")]
        public string DeadlineTime { get; set; }
    }

    /// <summary>获取环节信息接口返回的 result 数据。</summary>
    public class TaskStateResult
    {
        /// <summary>流程实例 ID。</summary>
        [JsonProperty("wfId")]
        public string WfId { get; set; }

        /// <summary>环节层级自定义标识。</summary>
        [JsonProperty("taskDef")]
        public string TaskDef { get; set; }

        /// <summary>环节业务标识。</summary>
        [JsonProperty("bizTag")]
        public string BizTag { get; set; }

        /// <summary>环节类型。</summary>
        [JsonProperty("taskType")]
        public string TaskType { get; set; }

        /// <summary>办理环节列表。</summary>
        [JsonProperty("taskList")]
        public IList<TaskStateItem> TaskList { get; set; } = new List<TaskStateItem>();

        /// <summary>执行过程信息（Map 结构，扩展字段）。</summary>
        [JsonProperty("reObj")]
        public IDictionary<string, object> ReObj { get; set; }
    }

    // ============================================================
    // 2.7 流程撤回（/maintain/workflow-backoff）
    // 2.9 流程环节回滚（/process/rollback）
    // ============================================================

    /// <summary>
    /// 流程撤回请求体。
    /// 在 BPM 客户端中各字段以 query string 形式发送至 BPM 后端；
    /// 在 BpmApiHost 中作为 JSON 请求体（<c>[FromBody]</c>）接收。
    /// </summary>
    public class WorkflowBackoffRequest
    {
        /// <summary>流程实例 ID（必填）。</summary>
        public string WfId { get; set; }

        /// <summary>操作人账号（必填）。</summary>
        public string OpUser { get; set; }

        /// <summary>撤回到的环节 ID（必填）。</summary>
        public string TaskId { get; set; }
    }

    /// <summary>流程撤回/回滚的返回 result 数据。</summary>
    public class BackoffResult
    {
        /// <summary>下一环节列表。</summary>
        [JsonProperty("nextList")]
        public IList<NextTaskInfo> NextList { get; set; } = new List<NextTaskInfo>();
    }

    // ============================================================
    // 2.8 流程取消（/maintain/cancelwf）
    // ============================================================

    /// <summary>
    /// 流程取消请求。
    /// 在 BPM 客户端中各字段以 query string 形式发送至 BPM 后端；
    /// 在 BpmApiHost 中作为 JSON 请求体（<c>[FromBody]</c>）接收。
    /// </summary>
    public class CancelWfRequest
    {
        /// <summary>流程实例 ID（必填）。</summary>
        public string WfId { get; set; }

        /// <summary>操作人账号（必填）。</summary>
        public string OpUser { get; set; }
    }

    // ============================================================
    // 2.10 获取办理锁 / 2.11 释放办理锁（/process/try-lock、release-lock）
    // ============================================================

    /// <summary>
    /// 获取/释放办理锁请求。
    /// 在 BPM 客户端中各字段以 query string 形式发送至 BPM 后端；
    /// 在 BpmApiHost 中作为 JSON 请求体（<c>[FromBody]</c>）接收。
    /// </summary>
    public class ProcessLockRequest
    {
        /// <summary>环节 ID（必填）。</summary>
        public string TaskId { get; set; }

        /// <summary>办理人账号（必填）。</summary>
        public string OpUser { get; set; }
    }

    // ============================================================
    // 2.12 预跑测试（/trivial/task-linetest）
    // ============================================================

    /// <summary>预跑测试请求体。</summary>
    public class TaskLinetestRequest
    {
        /// <summary>当前环节 ID（必填）。</summary>
        [JsonProperty("taskId")]
        public string TaskId { get; set; }

        /// <summary>
        /// 预跑层级：指定 taskId 为第 0 层，往后依次递增（可选）。
        /// </summary>
        [JsonProperty("lineLevel", NullValueHandling = NullValueHandling.Ignore)]
        public int? LineLevel { get; set; }
    }

    // ============================================================
    // 2.13 获取表单信息（/formdata/full-get）
    // ============================================================

    /// <summary>表单信息接口返回的 result 数据（Map 结构，字段因模板而异）。</summary>
    public class FormDataResult
    {
        /// <summary>表单字段数据，键=组件标识，值=组件值。</summary>
        [JsonExtensionData]
        public IDictionary<string, object> Fields { get; set; } = new Dictionary<string, object>();
    }

    // ============================================================
    // 2.14 获取待办列表（/searchlist/toassign）
    // ============================================================

    /// <summary>获取待办列表请求体。</summary>
    public class PendingTaskSearchRequest
    {
        /// <summary>办理人账号（必填）。</summary>
        [JsonProperty("asnUser")]
        public string AsnUser { get; set; }

        /// <summary>页码，默认 1。</summary>
        [JsonProperty("page", NullValueHandling = NullValueHandling.Ignore)]
        public int? Page { get; set; }

        /// <summary>每页记录数，默认 5。</summary>
        [JsonProperty("rows", NullValueHandling = NullValueHandling.Ignore)]
        public int? Rows { get; set; }

        /// <summary>流程类别 ID（可选）。</summary>
        [JsonProperty("group_id", NullValueHandling = NullValueHandling.Ignore)]
        public string GroupId { get; set; }

        /// <summary>流程模板自定义标识（可选）。</summary>
        [JsonProperty("template_id", NullValueHandling = NullValueHandling.Ignore)]
        public string TemplateId { get; set; }

        /// <summary>发起人 ID（可选）。</summary>
        [JsonProperty("init_user", NullValueHandling = NullValueHandling.Ignore)]
        public string InitUser { get; set; }
    }

    // ============================================================
    // 2.15 表单赋值（/formdata/update）
    // ============================================================

    /// <summary>表单赋值请求体。</summary>
    public class FormDataUpdateRequest
    {
        /// <summary>流程实例 ID（必填）。</summary>
        [JsonProperty("wfId")]
        public string WfId { get; set; }

        /// <summary>操作人账号（可选）。</summary>
        [JsonProperty("user", NullValueHandling = NullValueHandling.Ignore)]
        public string User { get; set; }

        /// <summary>表单组件赋值（可选）。</summary>
        [JsonProperty("formData", NullValueHandling = NullValueHandling.Ignore)]
        public IList<FormDataItem> FormData { get; set; }

        /// <summary>数据方阵赋值（可选）。</summary>
        [JsonProperty("gridData", NullValueHandling = NullValueHandling.Ignore)]
        public IList<GridDataItem> GridData { get; set; }

        /// <summary>环节经办人员指定（可选）。</summary>
        [JsonProperty("assigneeData", NullValueHandling = NullValueHandling.Ignore)]
        public IList<AssigneeDataItem> AssigneeData { get; set; }
    }

    // ============================================================
    // 公共列表查询基类（2.16~2.19 系列查询接口共用）
    // ============================================================

    /// <summary>流程列表查询通用过滤参数基类。</summary>
    public class ProcessListSearchRequest
    {
        /// <summary>流程模板自定义标识（可选）。</summary>
        [JsonProperty("wfDef", NullValueHandling = NullValueHandling.Ignore)]
        public string WfDef { get; set; }

        /// <summary>页码，默认 1。</summary>
        [JsonProperty("page", NullValueHandling = NullValueHandling.Ignore)]
        public int? Page { get; set; }

        /// <summary>每页记录数，默认 10。</summary>
        [JsonProperty("rows", NullValueHandling = NullValueHandling.Ignore)]
        public int? Rows { get; set; }

        /// <summary>
        /// 流程实例状态：0=进行中，1=已完成，2=取消，-1=全部（默认）。
        /// </summary>
        [JsonProperty("folder", NullValueHandling = NullValueHandling.Ignore)]
        public int? Folder { get; set; }

        /// <summary>流程类别编码（可选）。</summary>
        [JsonProperty("groupCode", NullValueHandling = NullValueHandling.Ignore)]
        public string GroupCode { get; set; }

        /// <summary>流程子类别编码（可选）。</summary>
        [JsonProperty("subGroupCode", NullValueHandling = NullValueHandling.Ignore)]
        public string SubGroupCode { get; set; }

        /// <summary>流程名称（可选）。</summary>
        [JsonProperty("requestDesc", NullValueHandling = NullValueHandling.Ignore)]
        public string RequestDesc { get; set; }

        /// <summary>发起时间范围起始（格式 yyyy-MM-dd HH:mm:ss，可选）。</summary>
        [JsonProperty("initStartTime", NullValueHandling = NullValueHandling.Ignore)]
        public string InitStartTime { get; set; }

        /// <summary>发起时间范围结束（格式 yyyy-MM-dd HH:mm:ss，可选）。</summary>
        [JsonProperty("initEndTime", NullValueHandling = NullValueHandling.Ignore)]
        public string InitEndTime { get; set; }
    }

    // ============================================================
    // 2.16 获取流程实例列表（/searchlist/tomonitor）
    // ============================================================

    /// <summary>获取流程实例列表请求体（监控视角）。</summary>
    public class MonitorSearchRequest : ProcessListSearchRequest
    {
        /// <summary>发起人工号或手机号（可选）。</summary>
        [JsonProperty("initValue", NullValueHandling = NullValueHandling.Ignore)]
        public string InitValue { get; set; }
    }

    // ============================================================
    // 2.17 我发起的列表（/searchlist/selfinit）
    // ============================================================

    /// <summary>我发起的流程列表请求体。</summary>
    public class SelfInitSearchRequest : ProcessListSearchRequest
    {
        /// <summary>发起人工号或手机号（必填）。</summary>
        [JsonProperty("initUser")]
        public string InitUser { get; set; }
    }

    // ============================================================
    // 2.18 我经办的列表（/searchlist/selfprocess）
    // ============================================================

    /// <summary>我经办的流程列表请求体。</summary>
    public class SelfProcessSearchRequest : ProcessListSearchRequest
    {
        /// <summary>办理人工号或手机号（必填）。</summary>
        [JsonProperty("asnUser")]
        public string AsnUser { get; set; }

        /// <summary>发起人工号（可选）。</summary>
        [JsonProperty("initUser", NullValueHandling = NullValueHandling.Ignore)]
        public string InitUser { get; set; }

        /// <summary>办理时间范围起始（格式 yyyy-MM-dd HH:mm:ss，可选）。</summary>
        [JsonProperty("opStartTime", NullValueHandling = NullValueHandling.Ignore)]
        public string OpStartTime { get; set; }

        /// <summary>办理时间范围结束（格式 yyyy-MM-dd HH:mm:ss，可选）。</summary>
        [JsonProperty("opEndTime", NullValueHandling = NullValueHandling.Ignore)]
        public string OpEndTime { get; set; }
    }

    // ============================================================
    // 2.19 抄送我的列表（/searchlist/tocc）
    // ============================================================

    /// <summary>抄送我的流程列表请求体。</summary>
    public class ToCcSearchRequest : ProcessListSearchRequest
    {
        /// <summary>接收人工号或手机号（必填）。</summary>
        [JsonProperty("asnUser")]
        public string AsnUser { get; set; }

        /// <summary>发起人工号，多个用 "####" 分割（可选）。</summary>
        [JsonProperty("initUser", NullValueHandling = NullValueHandling.Ignore)]
        public string InitUser { get; set; }

        /// <summary>抄送来自（工号取值，多个用 "####" 分割，可选）。</summary>
        [JsonProperty("fromUser", NullValueHandling = NullValueHandling.Ignore)]
        public string FromUser { get; set; }

        /// <summary>接收时间范围起始（格式 yyyy-MM-dd HH:mm:ss，可选）。</summary>
        [JsonProperty("assignStartTime", NullValueHandling = NullValueHandling.Ignore)]
        public string AssignStartTime { get; set; }

        /// <summary>接收时间范围结束（格式 yyyy-MM-dd HH:mm:ss，可选）。</summary>
        [JsonProperty("assignEndTime", NullValueHandling = NullValueHandling.Ignore)]
        public string AssignEndTime { get; set; }
    }

    // ============================================================
    // 3.6 添加流程委托（/trivial/delegate-addasn）
    // 3.7 取消流程委托（/trivial/delegate-invalidasn）
    // ============================================================

    /// <summary>添加流程委托请求体。</summary>
    public class DelegateAddRequest
    {
        /// <summary>委托人账号（必填）。</summary>
        [JsonProperty("fromUser")]
        public string FromUser { get; set; }

        /// <summary>委托生效日期（格式 yyyy-MM-dd，可选）。</summary>
        [JsonProperty("startDate", NullValueHandling = NullValueHandling.Ignore)]
        public string StartDate { get; set; }

        /// <summary>委托失效日期（格式 yyyy-MM-dd，可选）。</summary>
        [JsonProperty("endDate", NullValueHandling = NullValueHandling.Ignore)]
        public string EndDate { get; set; }
    }

    /// <summary>取消流程委托请求体。</summary>
    public class DelegateInvalidRequest
    {
        /// <summary>委托人账号（必填）。</summary>
        [JsonProperty("fromUser")]
        public string FromUser { get; set; }

        /// <summary>受委托人账号（必填）。</summary>
        [JsonProperty("toUser")]
        public string ToUser { get; set; }
    }

    // ============================================================
    // 3.9 获取模板列表（/trivial/templates）
    // ============================================================

    /// <summary>获取模板列表请求体。</summary>
    public class TemplateSearchRequest
    {
        /// <summary>类别 ID（可选，不填返回所有）。</summary>
        [JsonProperty("groupId", NullValueHandling = NullValueHandling.Ignore)]
        public string GroupId { get; set; }
    }

    // ============================================================
    // 4 运维接口 请求体
    // ============================================================

    /// <summary>
    /// 变更流程状态请求。
    /// 在 BPM 客户端中各字段以 query string 形式发送至 BPM 后端；
    /// 在 BpmApiHost 中作为 JSON 请求体（<c>[FromBody]</c>）接收。
    /// </summary>
    public class ChangeWfStatusRequest
    {
        /// <summary>流程实例 ID（必填）。</summary>
        public string WfId { get; set; }

        /// <summary>
        /// 目标状态（必填）：1=进行中，3=已完成，13=异常结束，11=取消。
        /// </summary>
        public int TargetStatus { get; set; }

        /// <summary>操作人账号（必填）。</summary>
        public string OpUser { get; set; }
    }

    /// <summary>
    /// 变更环节（人员）办理状态请求。
    /// 在 BPM 客户端中各字段以 query string 形式发送至 BPM 后端；
    /// 在 BpmApiHost 中作为 JSON 请求体（<c>[FromBody]</c>）接收。
    /// </summary>
    public class ChangeTaskStatusRequest
    {
        /// <summary>环节 ID（必填）。</summary>
        public string TaskId { get; set; }

        /// <summary>
        /// 目标状态（必填）：1=待办，3=办理中，6=已完成，-1=未到达，11=已取消。
        /// </summary>
        public int TargetStatus { get; set; }

        /// <summary>操作人账号（必填）。</summary>
        public string OpUser { get; set; }
    }

    /// <summary>
    /// 变更环节（节点）状态请求。
    /// 在 BPM 客户端中各字段以 query string 形式发送至 BPM 后端；
    /// 在 BpmApiHost 中作为 JSON 请求体（<c>[FromBody]</c>）接收。
    /// </summary>
    public class ChangeLevelStatusRequest
    {
        /// <summary>流程实例 ID（必填）。</summary>
        public string WfId { get; set; }

        /// <summary>
        /// 目标状态（必填）：1=待办，3=办理中，6=已完成，-1=未到达，11=已取消。
        /// </summary>
        public int TargetStatus { get; set; }

        /// <summary>环节层级（与 TaskDef 二选一）。</summary>
        public int? TaskLevel { get; set; }

        /// <summary>环节标识（与 TaskLevel 二选一）。</summary>
        public string TaskDef { get; set; }

        /// <summary>操作人账号（必填）。</summary>
        public string OpUser { get; set; }
    }

    /// <summary>
    /// 变更环节（节点）办理人请求。
    /// 在 BPM 客户端中各字段以 query string 形式发送至 BPM 后端；
    /// 在 BpmApiHost 中作为 JSON 请求体（<c>[FromBody]</c>）接收。
    /// </summary>
    public class TaskChangeAssigneeRequest
    {
        /// <summary>环节 ID（必填）。</summary>
        public string TaskId { get; set; }

        /// <summary>环节办理人账号（必填）。</summary>
        public string Assignee { get; set; }

        /// <summary>操作人账号（必填）。</summary>
        public string OpUser { get; set; }
    }
}
