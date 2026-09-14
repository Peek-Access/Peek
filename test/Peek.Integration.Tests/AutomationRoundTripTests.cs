using Xunit;

namespace Peek.Integration.Tests;

/// <summary>Exercises the automation.*/system.* RPC methods against a real worker and a real window - the core "UI &lt;-&gt; worker" contract.</summary>
public sealed class AutomationRoundTripTests : IClassFixture<TestWorkerFixture>
{
    private readonly TestWorkerFixture _fixture;

    public AutomationRoundTripTests(TestWorkerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Ping_succeeds()
    {
        var ok = await _fixture.Connection.Client.Automation.PingAsync();
        Assert.True(ok);
    }

    [Fact]
    public async Task GetStatus_reports_a_version_and_ready_state()
    {
        var status = await _fixture.Connection.Client.Automation.GetStatusAsync();

        Assert.False(string.IsNullOrWhiteSpace(status.Version));
        Assert.Equal("Ready", status.State);
    }

    [Fact]
    public async Task GetElementFromHandle_returns_the_real_window_and_button_child()
    {
        using var window = new TestWindow("Automation RoundTrip Test", "OK");

        var root = await _fixture.Connection.Client.Automation.GetElementFromHandleAsync(window.Hwnd);
        Assert.NotNull(root);
        Assert.Equal("Automation RoundTrip Test", root!.Name);

        var children = await _fixture.Connection.Client.Automation.GetChildrenAsync(window.Hwnd);
        Assert.Contains(children, c => c.Name == "OK" && c.ControlType == "Button");
    }

    [Fact]
    public async Task GetElementFromPoint_does_not_resolve_to_a_suite_test_window()
    {
        // Windows UI Automation always resolves *some* element at any point in the
        // (unbounded) virtual screen space - worst case the desktop root - so this can
        // never assert null. What actually matters: a point nowhere near the (off-screen,
        // but real) test windows this suite creates must not resolve to one of them by
        // accident.
        var element = await _fixture.Connection.Client.Automation.GetElementFromPointAsync(-999_000, -999_000);

        if (element is not null)
            Assert.DoesNotContain("WindowsForms", element.ClassName, StringComparison.Ordinal);
    }
}
