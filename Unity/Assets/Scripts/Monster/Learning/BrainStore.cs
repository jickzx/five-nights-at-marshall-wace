using System;
using System.IO;
using UnityEngine;

namespace Fnmw.Monster.Learning
{
    /// <summary>Saves what the beast learned to a JSON file so it remembers the player across runs.</summary>
    public class BrainStore
    {
        public const string FileName = "beast_memory.json";
        public string Path { get; }

        public BrainStore(string path) { Path = path; }

        public static BrainStore Default() => new BrainStore(System.IO.Path.Combine(Application.persistentDataPath, FileName));

        public static string ToJson(LearnerState s) => JsonUtility.ToJson(s, true);

        public static LearnerState FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var s = JsonUtility.FromJson<LearnerState>(json);
            if (s == null) return null;
            s.Concepts ??= new System.Collections.Generic.List<ConceptBelief>();
            s.History ??= new System.Collections.Generic.List<AnswerRecord>();
            return s;
        }

        public LearnerState Load()
        {
            try { return File.Exists(Path) ? FromJson(File.ReadAllText(Path)) : null; }
            catch (Exception ex) { Debug.LogWarning($"[Beast] could not read memory at {Path}: {ex.Message}"); return null; }
        }

        public void Save(LearnerState s)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(Path, ToJson(s));
            }
            catch (Exception ex) { Debug.LogWarning($"[Beast] could not save memory to {Path}: {ex.Message}"); }
        }

        public void Delete()
        {
            try { if (File.Exists(Path)) File.Delete(Path); }
            catch (Exception ex) { Debug.LogWarning($"[Beast] could not delete memory at {Path}: {ex.Message}"); }
        }
    }
}
