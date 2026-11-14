using Fnmw.Core;

namespace Fnmw.Audio
{
    /// <summary>Audio thread entry point: registers the generative AudioDirector in place of the fake.</summary>
    public static class AudioModule
    {
        public static void Install()
        {
            Services.Audio = AudioDirector.Create();
        }
    }
}
