namespace Fnmw.Lang
{
    /// <summary>Lang thread entry point: registers the real language engine in place of the fake.</summary>
    public static class LangModule
    {
        public static void Install() => Fnmw.Core.Services.Language = new LanguageEngine();
    }
}
