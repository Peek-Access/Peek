using Peek.Core.Services;
using Xunit;

namespace Peek.Core.Tests.Services;

public sealed class ProcessAnnouncementFormatterTests
{
    [Fact]
    public void Brief_selection_omits_memory()
    {
        var text = ProcessAnnouncementFormatter.FormatSelection("notepad", 1234, detailed: false, memoryBytes: 50 * 1024 * 1024, Invariant);

        Assert.Equal("notepad, process 1234.", text);
    }

    [Fact]
    public void Detailed_selection_includes_memory_in_megabytes()
    {
        var text = ProcessAnnouncementFormatter.FormatSelection("notepad", 1234, detailed: true, memoryBytes: 50 * 1024 * 1024, Invariant);

        Assert.Equal("notepad, process 1234, using 50 megabytes.", text);
    }

    [Fact]
    public void Detailed_selection_switches_to_gigabytes_above_1024_megabytes()
    {
        var text = ProcessAnnouncementFormatter.FormatSelection("chrome", 42, detailed: true, memoryBytes: 2L * 1024 * 1024 * 1024, Invariant);

        Assert.Equal("chrome, process 42, using 2.0 gigabytes.", text);
    }

    [Fact]
    public void Falls_back_to_Unnamed_process_for_a_blank_name()
    {
        var text = ProcessAnnouncementFormatter.FormatSelection("", 1, detailed: false, memoryBytes: 0, Invariant);

        Assert.StartsWith("Unnamed process, process 1", text);
    }

    [Fact]
    public void Formats_kill_succeeded()
    {
        Assert.Equal("Ended notepad.", ProcessAnnouncementFormatter.FormatKillSucceeded("notepad", Invariant));
    }

    [Fact]
    public void Formats_kill_failed_with_reason()
    {
        var text = ProcessAnnouncementFormatter.FormatKillFailed("notepad", "Access is denied", Invariant);

        Assert.Equal("Failed to end notepad: Access is denied", text);
    }

    [Fact]
    public void Formats_open_folder_failed_with_reason()
    {
        var text = ProcessAnnouncementFormatter.FormatOpenFolderFailed("notepad", "not found", Invariant);

        Assert.Equal("Couldn't open the folder for notepad: not found", text);
    }
}
