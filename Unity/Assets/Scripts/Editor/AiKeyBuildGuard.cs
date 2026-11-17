using Fnmw.Core.Ai;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fnmw.EditorTools
{
    /// <summary>
    /// Stops a build that would bundle a OpenRouter key. Builds never use a direct key (AiConfig ignores it outside the
    /// Editor), so a key in Assets/Resources/Ai/secrets.json could only leak. Local keys go in Unity/secrets.json,
    /// which is outside Assets and never bundled; a build reaches OpenRouter only through aiProxyUrl.
    /// </summary>
    public class AiKeyBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var baked = Resources.Load<TextAsset>(AiConfig.ResourcePath);
            if (baked == null) return;
            if (AiConfig.HoldsKey(baked.text))
                throw new BuildFailedException("[Ai] Assets/Resources/" + AiConfig.ResourcePath + ".json holds a OpenRouter key, which this build "
                    + "would bundle. Move the key to Unity/secrets.json (Editor only) and keep only aiProxyUrl in the Resources file.");
            Debug.Log("[Ai] build carries AI settings from Assets/Resources/" + AiConfig.ResourcePath + ".json (no key)");
        }
    }
}
