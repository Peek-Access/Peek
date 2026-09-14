using System.Windows.Forms;

namespace Peek.Integration.Tests;

/// <summary>
/// A real, self-contained WinForms window positioned off-screen (far outside any
/// monitor) so it never becomes visible on the developer's actual desktop, but
/// still gets a real HWND that UI Automation/screenshot/OCR can inspect like any
/// other window - never reuses or touches a pre-existing window.
/// Must be created and disposed on an STA thread.
/// </summary>
public sealed class TestWindow : IDisposable
{
    // Each window gets its own horizontal band, off-screen. xUnit runs test collections in
    // parallel, so two windows alive at once must never sit on top of each other -
    // AutomationElement.FromPoint would return whichever window happens to be on top rather
    // than the one a given test created, making assertions intermittently target the wrong
    // test's window.
    private static int _instanceCount = -1;

    private const int OffScreenX = -32000;
    private const int OffScreenBaseY = -32000;
    private const int WindowHeight = 200;
    private const int InstanceSpacing = WindowHeight + 50;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly int _offScreenY;
    private Form? _form;
    private Button? _button;

    public nint Hwnd { get; private set; }

    /// <summary>
    /// The button's center in real screen coordinates. Deliberately not
    /// <c>Form.Location + Button.Location + half-size</c>: that ignores the
    /// window's non-client area (title bar/borders), so it lands on the form
    /// itself rather than the button - use <see cref="Control.PointToScreen"/>,
    /// the actual client-to-screen transform, instead.
    /// </summary>
    public System.Drawing.Point ButtonCenterScreenPoint =>
        (System.Drawing.Point)_form!.Invoke(() =>
        {
            var center = new System.Drawing.Point(_button!.Width / 2, _button.Height / 2);
            return _button.PointToScreen(center);
        });

    public TestWindow(string title, string buttonText)
    {
        _offScreenY = OffScreenBaseY + (Interlocked.Increment(ref _instanceCount) * InstanceSpacing);

        _thread = new Thread(() => RunMessageLoop(title, buttonText)) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Test window did not become ready in time.");
    }

    private void RunMessageLoop(string title, string buttonText)
    {
        _form = new Form
        {
            Text = title,
            Width = 420,
            Height = WindowHeight,
            StartPosition = FormStartPosition.Manual,
            // Far outside any real monitor - never visible on the actual desktop - and in a
            // band of its own so concurrently-running tests never share a coordinate.
            Location = new System.Drawing.Point(OffScreenX, _offScreenY),
        };
        _button = new Button
        {
            Text = buttonText,
            Width = 160,
            Height = 44,
            Location = new System.Drawing.Point(120, 70),
            Font = new System.Drawing.Font("Segoe UI", 12),
        };
        _form.Controls.Add(_button);
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
