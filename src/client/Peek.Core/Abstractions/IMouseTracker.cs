using ReactiveUI.Primitives;
using System.Drawing;

namespace Peek.Core.Abstractions;

public interface IMouseTracker
{
    public IObservable<Point> MousePositionStream { get; }
    public IObservable<RxVoid> SelectedStream { get; }
    public void Pause();
    public void Resume();
}
