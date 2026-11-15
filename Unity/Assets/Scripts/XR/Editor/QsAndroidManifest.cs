#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace Fnmw.XR.EditorTools
{
    /// <summary>
    /// Makes sure the Quest build declares the permissions QS telemetry needs: INTERNET (posting to the API) and
    /// RECORD_AUDIO (breathing features, computed on the headset; the mic is opened only after the candidate agrees,
    /// and Android asks the player for permission at that moment). Unity normally adds both by itself because the
    /// game uses UnityWebRequest and Microphone; this keeps them explicit. Compiled only when the active target is
    /// Android, so desktop editors without the Android module are unaffected.
    /// </summary>
    public sealed class QsAndroidManifest : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;

        static readonly string[] Permissions = { "android.permission.INTERNET", "android.permission.RECORD_AUDIO" };

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) return;
            const string ns = "http://schemas.android.com/apk/res/android";
            var doc = new XmlDocument();
            doc.Load(manifest);
            var root = doc.DocumentElement;
            foreach (var perm in Permissions)
            {
                bool present = false;
                foreach (XmlNode n in root.SelectNodes("uses-permission"))
                    if (n.Attributes?["android:name"]?.Value == perm) { present = true; break; }
                if (present) continue;
                var el = doc.CreateElement("uses-permission");
                el.SetAttribute("name", ns, perm);
                root.PrependChild(el);
            }
            doc.Save(manifest);
        }
    }
}
#endif
