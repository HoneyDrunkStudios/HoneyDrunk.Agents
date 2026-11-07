using Microsoft.Extensions.Logging;

namespace HoneyDrunk.Agents;

/// <summary>Log events. Messages carry ids, codes and exception type names only.</summary>
internal static partial class AgentLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent {AgentId} tool call {ToolKey} ended {Status} {ErrorCode}")]
    public static partial void ToolCallEnded(ILogger logger, string agentId, string toolKey, string status, string? errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentId} tool call {ToolKey}: an Operator check failed with {ExceptionType}; the call is not dispatched")]
    public static partial void GateFailed(ILogger logger, string agentId, string toolKey, string exceptionType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent {AgentId} memory {Operation} returned {Count}")]
    public static partial void MemoryOperation(ILogger logger, string agentId, string operation, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} {Transition} {ExecutionId} {Outcome}")]
    public static partial void Transition(ILogger logger, string agentId, string transition, string? executionId, string? outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentId} execution {ExecutionId} failed with {ExceptionType}")]
    public static partial void AgentFailed(ILogger logger, string agentId, string executionId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentId} execution {ExecutionId} timed out")]
    public static partial void AgentTimedOut(ILogger logger, string agentId, string executionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentId} execution {ExecutionId} stopped with {ExceptionType}; rethrowing to the host")]
    public static partial void AgentStopped(ILogger logger, string agentId, string executionId, string exceptionType);
}
