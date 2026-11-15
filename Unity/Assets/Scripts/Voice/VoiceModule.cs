using Fnmw.Core;

namespace Fnmw.Voice
{
    /// <summary>Voice thread entry point: optional monster dialogue, ship hints and pronunciation.
    /// With no backend configured it makes no network request at all and shows authored hints only.</summary>
    public static class VoiceModule
    {
        public static void Install()
        {
            Services.Voice = VoiceDirector.Create();
        }
    }
}
