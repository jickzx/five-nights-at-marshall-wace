using UnityEngine;

namespace Fnmw.Core.Telemetry
{
    /// <summary>
    /// Installs the QS sender when it is configured and enabled. Off by default: with no qs.json and no
    /// FNMW_QS_* environment variables nothing is created and nothing is sent.
    /// </summary>
    public static class QsModule
    {
        public static void Install()
        {
            var config = QsConfig.Load();
            string problem = config.Problem;
            if (problem != null)
            {
                if (problem != "disabled") Debug.LogWarning("[QS] not sending: " + problem + " (settings from " + config.Source + ")");
                if (QsTelemetry.Instance != null) Object.Destroy(QsTelemetry.Instance.gameObject);
                return;
            }
            IQsPoseSource source = new CameraPoseSource();
#if FNMW_QS_XR
            if (XrPoseSource.Available()) source = new XrPoseSource();
#endif
            var sender = QsTelemetry.Create(config, source);
            if (Application.isPlaying) Object.DontDestroyOnLoad(sender.gameObject);
            sender.BeginConsent();
            Debug.Log("[QS] ready, waiting for the candidate's consent (source " + source.Name + ", settings from " + config.Source + ")");
        }
    }
}
