using System.Globalization;
using Peek.Worker.Contracts.Automation;

namespace Peek.Core.Services.Speech;

/// <summary>Turns a raw UIA <see cref="SemanticElement"/> into structured, policy-driven speech.</summary>
public interface ISpeechPolicy
{
    /// <param name="culture">
    /// The language to phrase the announcement in - the speech language, which is configured
    /// separately from the UI language (see <see cref="SpeechStrings.ResolveCulture"/>).
    /// Only Peek's own scaffolding ("button", "checked") is translated; the element's name
    /// and value come from the inspected app and are spoken as-is.
    /// </param>
    SpeechContent Describe(SemanticElement element, SpeechVerbosity verbosity = SpeechVerbosity.Standard, CultureInfo? culture = null);
}
