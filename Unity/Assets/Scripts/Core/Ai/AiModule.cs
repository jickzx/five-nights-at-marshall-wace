using UnityEngine;

namespace Fnmw.Core.Ai
{
    /// <summary>Installs Services.Ai. Without a key the fake stays in place and every feature uses its stock lines.</summary>
    public static class AiModule
    {
        public static void Install()
        {
            AiRunner.StopAll(); // requests from the previous run must not answer into this one
            var config = AiConfig.Load();
            if (!config.Usable)
            {
                Debug.Log(config.Disabled
                    ? "[Ai] switched off; using stock lines"
                    : "[Ai] no usable free model, key or proxy configured" + " (see Docs/ai/README.md); using stock lines");
                return;
            }
            Services.Ai = new OpenRouterClient(config);
            Debug.Log("[Ai] OpenRouter ready: model " + config.Model + (config.UsesProxy ? " via proxy" : ", direct") + ", settings from " + config.Source);
        }
    }
}
