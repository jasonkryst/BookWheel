namespace BookWheel;

// Deliberately in the root BookWheel namespace, not BookWheel.Resources, even
// though the file lives under Resources/. The SDK's resx build target strips a
// literal "Resources" path segment when computing the embedded manifest name
// (so SharedErrors.resx becomes "BookWheel.SharedErrors.resources", not
// "BookWheel.Resources.SharedErrors.resources"). Do not set ResourcesPath on
// AddLocalization for this type — that would re-add the segment MSBuild
// already dropped and the two would no longer match.
public sealed class SharedErrors
{
}
