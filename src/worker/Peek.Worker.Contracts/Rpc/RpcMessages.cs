using System.Text.Json;
using System.Text.Json.Serialization;

namespace Peek.Worker.Contracts.Rpc;

public sealed class RpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Params { get; init; }
}

public sealed class RpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; init; }

    [JsonPropertyName("error")]
    public RpcError? Error { get; init; }

    /// <summary>
    /// Null for an ordinary single-shot response (every existing RPC method - fully
    /// backward compatible). A streaming method (see WorkerPipeServer/
    /// RpcRequestDispatcher.DispatchStreamingAsync) writes several responses sharing the
    /// same request Id: false on every intermediate chunk, true on the last one -
    /// WorkerRpcChannel.CallStreamingAsync treats "true or absent" as "this ends the
    /// stream" so a plain method's single response still terminates correctly without
    /// having to set this field at all.
    /// </summary>
    [JsonPropertyName("done")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Done { get; init; }

    public static RpcResponse Success(int id, JsonElement result) =>
        new() { Id = id, Result = result };

    public static RpcResponse Failure(int id, int code, string message) =>
        new() { Id = id, Error = new RpcError { Code = code, Message = message } };

    public static RpcResponse StreamChunk(int id, JsonElement result, bool done) =>
        new() { Id = id, Result = result, Done = done };
}

public sealed class RpcError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public static class RpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    public const int ElementNotFound = -32001;
    public const int AutomationError = -32002;
    public const int Timeout = -32003;
}

public sealed class RpcMethodException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
