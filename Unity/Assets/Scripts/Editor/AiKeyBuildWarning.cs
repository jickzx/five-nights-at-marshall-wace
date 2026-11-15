using Fnmw.Core.Ai;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fnmw.EditorTools
{
    /// <summary>Says so in the build log when a build is about to carry the Gemini key, so it is never shared by accident.</summary>
    public class AiKeyBuildWarning : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (Resources.Load<TextAsset>(AiConfig.ResourcePath) == null) return;
            Debug.LogWarning("[Ai] This build carries the Gemini key from Assets/Resources/" + AiConfig.ResourcePath
                             + ".json. Fine for a demo build; do not publish it (the key can be extracted).");
        }
    }
}
