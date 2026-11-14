using System;
using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Game
{
    /// <summary>
    /// Demo mode for showing the game live: fixed seed, gentler puzzles and a beast that keeps its distance
    /// so the run can't end in death unless you ask for it (F12). Turn on with F1 (restarts the run) or the
    /// launch flag -demo. The choice survives restarts within a session.
    /// </summary>
    public static class DemoMode
    {
        public const int Seed = 4242;
        public static bool On;
        static bool _argsRead;

        public static void ReadLaunchFlags()
        {
            if (_argsRead) return;
            _argsRead = true;
            foreach (var a in Environment.GetCommandLineArgs())
                if (a == "-demo" || a == "--demo") On = true;
        }

        /// <summary>Wrap the real (or fake) beast and difficulty so the run is forgiving.</summary>
        public static void Install()
        {
            if (!On) return;
            if (!(Services.BeastBrain is ForgivingBeastBrain)) Services.BeastBrain = new ForgivingBeastBrain(Services.BeastBrain);
            if (!(Services.Difficulty is GentleDifficulty)) Services.Difficulty = new GentleDifficulty(Services.Difficulty);
            Debug.Log("[Demo] demo mode on: fixed seed, forgiving beast");
        }
    }

    /// <summary>Lets the real brain decide, then keeps the beast outside a comfortable radius and below running speed.</summary>
    public class ForgivingBeastBrain : IBeastBrain
    {
        public readonly IBeastBrain Inner;
        public float KeepAway = 9f, MaxSpeed = 2.8f;
        public ForgivingBeastBrain(IBeastBrain inner) { Inner = inner; }

        public BeastIntent Think(BeastPerception p, float dt)
        {
            BeastIntent i;
            try { i = Inner.Think(p, dt); }
            catch (Exception ex) { Debug.LogException(ex); i = new BeastIntent { MoveTarget = p.PlayerPosition, Speed = 1f, DesiredStage = p.Stage }; }
            var away = p.BeastPosition - p.PlayerPosition; away.y = 0;
            if (away.sqrMagnitude < 0.01f) away = Vector3.back;
            if (p.Distance < KeepAway)
            {
                i.MoveTarget = p.PlayerPosition + away.normalized * KeepAway * 1.4f;
                i.Speed = 3.5f;
            }
            else i.Speed = Mathf.Min(i.Speed, MaxSpeed);
            return i;
        }
    }

    public class GentleDifficulty : IDifficultyDirector
    {
        public readonly IDifficultyDirector Inner;
        public GentleDifficulty(IDifficultyDirector inner) { Inner = inner; }
        public void Reset(int seed) => Inner.Reset(seed);
        public void Observe(AnswerEvent answer) => Inner.Observe(answer);
        public float Difficulty => Mathf.Min(Inner.Difficulty, 0.4f);
        public float BeastAggression => Inner.BeastAggression * 0.35f;
    }
}
