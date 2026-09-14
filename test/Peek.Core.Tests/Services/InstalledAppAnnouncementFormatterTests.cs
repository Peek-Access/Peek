using Peek.Core.Services;
using Xunit;

namespace Peek.Core.Tests.Services;

public sealed class InstalledAppAnnouncementFormatterTests
{
    [Fact]
    public void Formats_name_version_and_publisher_into_one_sentence()
    {
        var text = InstalledAppAnnouncementFormatter.FormatSelection("Notepad++", "8.6", "Don Ho", Invariant);

        Assert.Equal("Notepad++, version 8.6, by Don Ho.", text);
    }

    [Fact]
    public void Omits_missing_version_and_publisher()
    {
        var text = InstalledAppAnnouncementFormatter.FormatSelection("SomeApp", "", "", Invariant);

        Assert.Equal("SomeApp.", text);
    }

    [Fact]
    public void Falls_back_to_Unnamed_app_for_a_blank_name()
    {
        var text = InstalledAppAnnouncementFormatter.FormatSelection("", "1.0", "Acme", Invariant);

        Assert.StartsWith("Unnamed app, version 1.0", text);
    }

    [Fact]
    public void Formats_launch_succeeded()
    {
        Assert.Equal("Launched Notepad++.", InstalledAppAnnouncementFormatter.FormatLaunchSucceeded("Notepad++", Invariant));
    }

    [Fact]
    public void Formats_launch_failed_with_reason()
    {
        var text = InstalledAppAnnouncementFormatter.FormatLaunchFailed("Notepad++", "Access denied", Invariant);

        Assert.Equal("Failed to launch Notepad++: Access denied", text);
    }

    [Fact]
    public void Formats_launch_failed_without_reason()
    {
        var text = InstalledAppAnnouncementFormatter.FormatLaunchFailed("Notepad++", null, Invariant);

        Assert.Equal("Failed to launch Notepad++.", text);
    }
}
