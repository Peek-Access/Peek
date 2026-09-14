using Peek.Worker.Contracts.Rpc;

namespace Peek.Ipc.Worker;

public sealed class WorkerRpcException : Exception
{
    public int Code { get; }
    public string RpcMessage { get; }

    public WorkerRpcException(RpcError error)
        : base($"Worker RPC error {error.Code}: {error.Message}")
    {
        Code = error.Code;
        RpcMessage = error.Message;
    }

    public bool IsElementNotFound => Code == RpcErrorCodes.ElementNotFound;

    public bool IsMethodNotFound => Code == RpcErrorCodes.MethodNotFound;
}
