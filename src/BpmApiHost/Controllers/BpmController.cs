using System;
using System.Collections.Generic;
using BpmApiClient.Models;
using BpmApiHost.Services;
using Microsoft.AspNetCore.Mvc;

namespace BpmApiHost.Controllers
{
    [ApiController]
    [Route("api/bpm")]
    public class BpmController : ControllerBase
    {
        private readonly IBpmWorkflowService _workflowService;

        public BpmController(IBpmWorkflowService workflowService)
        {
            _workflowService = workflowService;
        }

        [HttpPost("process/start")]
        public ActionResult<StartProcessResponse> StartProcess([FromBody] StartProcessRequest request)
        {
            try
            {
                return Ok(_workflowService.StartProcess(request));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("tasks/{taskId}/approve")]
        public ActionResult<WorkflowTaskActionResponse> ApproveTask(string taskId, [FromBody] WorkflowTaskActionRequest request)
        {
            return HandleTaskAction(() => _workflowService.ApproveTask(taskId, request));
        }

        [HttpPost("tasks/{taskId}/reject")]
        public ActionResult<WorkflowTaskActionResponse> RejectTask(string taskId, [FromBody] WorkflowTaskActionRequest request)
        {
            return HandleTaskAction(() => _workflowService.RejectTask(taskId, request));
        }

        [HttpGet("process/{processInstanceId}")]
        public ActionResult<ProcessDetailResponse> GetProcess(string processInstanceId)
        {
            try
            {
                return Ok(_workflowService.GetProcess(processInstanceId));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpGet("tasks/pending")]
        public ActionResult<IReadOnlyCollection<WorkflowTaskSummary>> GetPendingTasks([FromQuery] string assignee)
        {
            try
            {
                return Ok(_workflowService.GetPendingTasks(assignee));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private ActionResult<WorkflowTaskActionResponse> HandleTaskAction(Func<WorkflowTaskActionResponse> action)
        {
            try
            {
                return Ok(action());
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }
    }
}
