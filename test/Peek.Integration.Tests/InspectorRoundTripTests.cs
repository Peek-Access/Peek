using Peek.Worker.Contracts.Automation;
using Xunit;

namespace Peek.Integration.Tests;

/// <summary>
/// Exercises the automation.inspector.* RPC methods (Element Inspector support) against
/// a real worker and a real window - the lazy tree-expansion contract
/// ElementInspectorViewModel relies on: get a window's own automation element as a root,
/// then fetch a specific element's direct children by its opaque ElementId token.
/// </summary>
public sealed class InspectorRoundTripTests : IClassFixture<TestWorkerFixture>
{
    private readonly TestWorkerFixture _fixture;

    public InspectorRoundTripTests(TestWorkerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetInspectorRoot_returns_the_window_with_a_usable_ElementId()
    {
        using var window = new TestWindow("Inspector Root Test", "OK");

        var root = await _fixture.Connection.Client.Automation.GetInspectorRootAsync(window.Hwnd);

        Assert.NotNull(root);
        Assert.Equal("Inspector Root Test", root!.Name);
        Assert.False(string.IsNullOrWhiteSpace(root.ElementId));
    }

    [Fact]
    public async Task GetInspectorChildren_of_the_root_includes_the_button()
    {
        using var window = new TestWindow("Inspector Children Test", "Save");

        var root = await _fixture.Connection.Client.Automation.GetInspectorRootAsync(window.Hwnd);
        Assert.NotNull(root);

        var children = await _fixture.Connection.Client.Automation.GetInspectorChildrenAsync(root!.ElementId!);

        Assert.Contains(children, c => c.Name == "Save" && c.ControlType == "Button");
        Assert.All(children, c => Assert.False(string.IsNullOrWhiteSpace(c.ElementId)));
    }

    [Fact]
    public async Task GetInspectorChildren_of_a_leaf_button_returns_empty()
    {
        using var window = new TestWindow("Inspector Leaf Test", "Cancel");

        var root = await _fixture.Connection.Client.Automation.GetInspectorRootAsync(window.Hwnd);
        var children = await _fixture.Connection.Client.Automation.GetInspectorChildrenAsync(root!.ElementId!);
        var button = Assert.Single(children, c => c.Name == "Cancel");

        var buttonChildren = await _fixture.Connection.Client.Automation.GetInspectorChildrenAsync(button.ElementId!);

        Assert.Empty(buttonChildren);
    }

    [Fact]
    public async Task GetInspectorChildren_of_an_unknown_ElementId_returns_empty_rather_than_throwing()
    {
        var children = await _fixture.Connection.Client.Automation.GetInspectorChildrenAsync("not-a-real-token");

        Assert.Empty(children);
    }

    [Fact]
    public async Task ResetInspector_invalidates_previously_returned_ElementIds()
    {
        using var window = new TestWindow("Inspector Reset Test", "Apply");
        var root = await _fixture.Connection.Client.Automation.GetInspectorRootAsync(window.Hwnd);

        await _fixture.Connection.Client.Automation.ResetInspectorAsync();

        var children = await _fixture.Connection.Client.Automation.GetInspectorChildrenAsync(root!.ElementId!);
        Assert.Empty(children);
    }

    /// <summary>
    /// The exact race a user hits browsing the Inspector: they've expanded a window (its
    /// root/children are cached), then close that window, then try to expand further -
    /// TreeWalker calls against the now-dead AutomationElement throw
    /// ElementNotAvailableException/COMException. WindowsAutomationService must catch that
    /// (SafeWalk/SafeCreateSemantic) and return an empty list, not fail the RPC call.
    /// </summary>
    [Fact]
    public async Task GetInspectorChildren_after_the_window_closes_returns_empty_rather_than_throwing()
    {
        SemanticElement? root;
        using (var window = new TestWindow("Inspector Closed Window Test", "Delete"))
        {
            root = await _fixture.Connection.Client.Automation.GetInspectorRootAsync(window.Hwnd);
            Assert.NotNull(root);
        }
        // TestWindow.Dispose() has now closed the real window this element belonged to.

        var children = await _fixture.Connection.Client.Automation.GetInspectorChildrenAsync(root!.ElementId!);

        Assert.Empty(children);
    }

    [Fact]
    public async Task GetInspectorRoot_for_an_already_closed_window_returns_null_rather_than_throwing()
    {
        nint hwnd;
        using (var window = new TestWindow("Inspector Root Closed Window Test", "OK"))
            hwnd = window.Hwnd;

        var root = await _fixture.Connection.Client.Automation.GetInspectorRootAsync(hwnd);

        Assert.Null(root);
    }

    /// <summary>
    /// Backs the Inspector's selection-driven announce/highlight flow (see
    /// ElementInspectorViewModel.RefreshElementAsync): re-reading a previously-returned
    /// element by its ElementId must reflect its current state, not a stale snapshot.
    /// </summary>
    [Fact]
    public async Task RefreshInspectorElement_returns_the_same_elements_current_data()
    {
        using var window = new TestWindow("Inspector Refresh Test", "Submit");
        var root = await _fixture.Connection.Client.Automation.GetInspectorRootAsync(window.Hwnd);

        var refreshed = await _fixture.Connection.Client.Automation.RefreshInspectorElementAsync(root!.ElementId!);

        Assert.NotNull(refreshed);
        Assert.Equal("Inspector Refresh Test", refreshed!.Name);
        Assert.Equal(root.ElementId, refreshed.ElementId);
    }

    [Fact]
    public async Task RefreshInspectorElement_after_the_window_closes_returns_null_rather_than_throwing()
    {
        SemanticElement? root;
        using (var window = new TestWindow("Inspector Refresh Closed Test", "OK"))
        {
            root = await _fixture.Connection.Client.Automation.GetInspectorRootAsync(window.Hwnd);
            Assert.NotNull(root);
        }

        var refreshed = await _fixture.Connection.Client.Automation.RefreshInspectorElementAsync(root!.ElementId!);

        Assert.Null(refreshed);
    }

    [Fact]
    public async Task RefreshInspectorElement_of_an_unknown_ElementId_returns_null_rather_than_throwing()
    {
        var refreshed = await _fixture.Connection.Client.Automation.RefreshInspectorElementAsync("not-a-real-token");

        Assert.Null(refreshed);
    }
}
