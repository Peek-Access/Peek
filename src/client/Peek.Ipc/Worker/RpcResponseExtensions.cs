using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Peek.Worker.Contracts.Rpc;

namespace Peek.Ipc.Worker;

/// <summary>
/// Shared response handling for every *Client class in this namespace - previously
/// duplicated as identical private static methods in each one.
/// </summary>
internal static class RpcResponseExtensions
{
    public static void ThrowIfError(this RpcResponse response)
    {
        if (response.Error is not null)
            throw new WorkerRpcException(response.Error);
    }

    public static T? Deserialize<T>(this RpcResponse response, JsonTypeInfo<T> typeInfo)
    {
        if (!response.Result.HasValue) return default;
        return response.Result.Value.Deserialize(typeInfo);
    }
}
