using System.Windows.Forms;

namespace Peek.Integration.Tests;

/// <summary>
/// A real WinForms window with no child controls at all, its text drawn directly via GDI in
/// <see cref="Form.OnPaint"/> - reproducing the exact shape of the bug report this exists to
/// pin down: hovering/focusing anywhere over it resolves to the same root Window automation
/// element (titled, but with no drillable children), the same way every hover over WeChat's
/// message list resolved to just "Weixin, Window" with none of the actual chat text ever
/// reaching UI Automation. Positioned off-screen like <see cref="TestWindow"/>, in a
/// non-overlapping coordinate band so the two window types never collide when tests run in
/// parallel. Must be created and disposed on an STA thread.
/// </summary>
public sealed class OpaqueTestWindow : IDisposable
{
    private static int _instanceCount = -1;

    // A separate off-screen band from TestWindow's (-32000, -32000..) so the two window types
    // can never land on top of each other under parallel test execution.
    private const int OffScreenX = -32000;
    private const int OffScreenBaseY = -20000;
    private const int WindowHeight = 300;
    private const int InstanceSpacing = WindowHeight + 50;

    // Spacing generous enough that PP-OCRv6-tiny reliably reports each string as its own line
    // with non-overlapping boxes, rather than merging close lines into one recognized block.
    private const int FirstLineY = 40;
    private const int LineSpacing = 60;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly int _offScreenY;
    private readonly IReadOnlyList<string> _lines;
    private Form? _form;

    public nint Hwnd { get; private set; }

    /// <summary>The window's client-area center in real screen coordinates.</summary>
    public System.Drawing.Point ClientCenterScreenPoint => ToScreen(_form!.ClientSize.Width / 2, _form.ClientSize.Height / 2);

    /// <summary>The screen point over the middle of the given line's drawn text (0-indexed, in constructor order).</summary>
    public System.Drawing.Point ScreenPointForLine(int index) =>
        ToScreen(_form!.ClientSize.Width / 2, FirstLineY + index * LineSpacing + 15);

    private System.Drawing.Point ToScreen(int x, int y) =>
        (System.Drawing.Point)_form!.Invoke(() => _form.PointToScreen(new System.Drawing.Point(x, y)));

    /// <param name="title">The window's title - this is the only text UI Automation can ever see (see SemanticElement.Name on the root Window element).</param>
    /// <param name="lines">
    /// One or more lines of text, each drawn on its own row directly onto the client area via
    /// GDI - visible on screen and to OCR, invisible to UI Automation. Use <see cref="ScreenPointForLine"/>
    /// to get a hoverable screen point for a specific one.
    /// </param>
    public OpaqueTestWindow(string title, params string[] lines)
    {
        _lines = lines;
        _offScreenY = OffScreenBaseY + (Interlocked.Increment(ref _instanceCount) * InstanceSpacing);

        _thread = new Thread(() => RunMessageLoop(title)) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Opaque test window did not become ready in time.");
    }

    private void RunMessageLoop(string title)
    {
        _form = new Form
        {
            Text = title,
            Width = 420,
            Height = WindowHeight,
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(OffScreenX, _offScreenY),
            // No child controls added anywhere - the only thing on this form is what OnPaint
            // draws, which carries no accessibility information of any kind.
        };

        _form.Paint += (_, e) =>
        {
            using var font = new System.Drawing.Font("Segoe UI", 20, System.Drawing.FontStyle.Bold);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Black);
            e.Graphics.Clear(System.Drawing.Color.White);
            for (var i = 0; i < _lines.Count; i++)
                e.Graphics.DrawString(_lines[i], font, brush, new System.Drawing.PointF(10, FirstLineY + i * LineSpacing));
        };

        _form.Shown += (_, _) =>
        {
            Hwnd = _form.Handle;
            _ready.Set();
        };

        Application.Run(_form);
    }

    public void Dispose()
    {
        _form?.Invoke(() => _form.Close());
        _thread.Join(TimeSpan.FromSeconds(5));
        _ready.Dispose();
    }
}
