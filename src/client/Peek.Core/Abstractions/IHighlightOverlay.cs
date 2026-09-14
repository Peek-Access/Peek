using System.Drawing;

namespace Peek.Core.Abstractions;

public interface IHighlightOverlay : IDisposable
{
    void Show(Rectangle rect);
    void Hide();
    void Close();

    void Update(Rectangle rect);
    void Reset(Rectangle rect);

    void SetBorderColor(string color);
    void SetFillColor(string color);

    nint GetNativeHandle();
    Task StartSpeakingAsync(CancellationToken cancellationToken);
    Task StopSpeakingAsync();

}
