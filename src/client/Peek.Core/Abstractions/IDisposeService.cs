namespace Peek.Core.Abstractions;

public interface IDisposeService
{
    void Register(IDisposable disposable, string? group = null);
    void Register(Action cleanupAction, string? group = null);
    void DisposeGroup(string group);
    void DisposeAll();
}
