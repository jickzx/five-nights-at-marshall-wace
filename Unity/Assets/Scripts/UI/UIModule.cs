using Fnmw.Core;

namespace Fnmw.UI
{
    /// <summary>UI thread entry point: registers the real IGameUI. Everything is built from code.</summary>
    public static class UIModule
    {
        public static void Install()
        {
            GlyphGen.ClearCache();
            Services.UI = GameUI.Create();
        }
    }
}
