using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Monster.Beast
{
    public enum BeastMode { Lurk, Stalk, Approach, Frozen, Lunge, Loom, Retreat, Chase }

    /// <summary>
    /// What the beast does, tick by tick. Pure logic (no scene access) so it is testable.
    ///
    /// Lurk     circles at the edge of the fog, behind you
    /// Stalk    closes in, behind you, as aggression and stage rise
    /// Approach creeps up while you are busy at a console
    /// Frozen   stops dead while you look at it (stages 1-2), slinks off if you keep staring
    /// Lunge    bursts at you on a wrong answer, then Looms over you
    /// Retreat  backs off into the fog on a right answer, then stays calm for a while
    /// Chase    stage 3: hunts you down and no longer cares that you are looking
    ///
    /// It evolves 1 -> 2 -> 3 with time (faster when aggressive) and with every mistake.
    /// </summary>
    public class BeastBrain : IBeastBrain
    {
        public const float FogEdge = 24f;
        /// <summary>Seconds per stage at aggression ~0.5. Tunable by the game loop.</summary>
        public static float StageSeconds = 75f;
        public const float LungeSpeed = 10f;
        public const float CatchDistance = 1.6f;

        public BeastMode Mode { get; private set; } = BeastMode.Lurk;
        public float Evolution { get; private set; }  // 0..2: stage = 1 + floor(Evolution)
        public int Stage => 1 + Mathf.Min(2, Mathf.FloorToInt(Evolution));
        public float EvolutionToNext => Stage >= 3 ? 1f : Evolution - (Stage - 1);
        public string Why { get; private set; } = "";
        public float CalmLeft => _calm;

        float _modeTime, _calm, _watchedFor, _orbitClock, _lungeStop;
        bool _pendingLunge, _pendingRetreat;

        /// <summary>Feed the result of every answer (the module wires GameEvents.AnswerSubmitted).</summary>
        public void OnAnswer(bool correct, float aggression)
        {
            if (correct)
            {
                _pendingRetreat = true; _pendingLunge = false;
                Evolution = Mathf.Max(Stage - 1, Evolution - 0.15f); // never de-evolves a stage
            }
            else
            {
                _pendingLunge = true; _pendingRetreat = false;
                Evolution = Mathf.Min(2f, Evolution + 0.3f + 0.3f * aggression);
            }
        }

        /// <summary>Debug/demo hook: push evolution forward.</summary>
        public void Evolve(float amount) => Evolution = Mathf.Clamp(Evolution + amount, 0f, 2f);

        public BeastIntent Think(BeastPerception p, float dt)
        {
            bool active = p.Phase != GamePhase.Title && p.Phase != GamePhase.Escaped && p.Phase != GamePhase.Dead;
            if (active) Evolution = Mathf.Min(2f, Evolution + dt / StageSeconds * (0.4f + 1.2f * p.Aggression));
            _orbitClock += dt;
            _modeTime += dt;
            if (_calm > 0) _calm -= dt;
            _watchedFor = p.Watched ? _watchedFor + dt : 0f;

            int stage = Stage;
            bool atConsole = p.Phase == GamePhase.Puzzle || p.Phase == GamePhase.Reveal || p.Phase == GamePhase.Quiz;
            float aggr = Mathf.Clamp01(p.Aggression);
            Vector3 player = Flat(p.PlayerPosition), me = Flat(p.BeastPosition);
            Vector3 away = me - player; float dist = away.magnitude;
            if (dist < 0.01f) away = Vector3.back; else away /= dist;

            // Answers interrupt whatever it was doing.
            if (_pendingLunge)
            {
                _pendingLunge = false;
                _lungeStop = stage >= 3 ? (aggr > 0.6f ? 0f : 2f) : Mathf.Lerp(stage == 1 ? 7f : 5f, 2.5f, aggr);
                if (dist > _lungeStop + 0.5f) Enter(BeastMode.Lunge);
                else Enter(BeastMode.Loom);
            }
            else if (_pendingRetreat)
            {
                _pendingRetreat = false;
                _calm = stage >= 3 ? 2.5f : 4f + 8f * (1f - aggr);
                Enter(BeastMode.Retreat);
            }

            // Ongoing modes that run to completion.
            switch (Mode)
            {
                case BeastMode.Lunge:
                    if (dist > _lungeStop + 0.3f && _modeTime < 2.5f)
                    {
                        Why = stage >= 3 ? "Lunging for the kill: wrong answer" : "Lunging: you got it wrong";
                        return Go(player + away * _lungeStop, LungeSpeed, stage, stage >= 3 || _lungeStop < 0.5f);
                    }
                    Enter(BeastMode.Loom);
                    break;
                case BeastMode.Loom:
                    if (_modeTime < 2.2f)
                    {
                        Why = "Looming over you";
                        return Go(me, 0f, stage, stage >= 3);
                    }
                    Enter(stage >= 3 ? BeastMode.Chase : BeastMode.Stalk);
                    break;
                case BeastMode.Retreat:
                    if (dist < FogEdge && _modeTime < 4f)
                    {
                        Why = stage >= 3 ? "Knocked back: right answer" : "Backing into the fog: you got it right";
                        return Go(player + away * (FogEdge + 4f), 5f, stage, false);
                    }
                    Enter(BeastMode.Lurk);
                    break;
            }

            // Stage 3: the final chase.
            if (stage >= 3)
            {
                if (atConsole)
                {
                    Mode = BeastMode.Approach;
                    Why = "Hunting: coming for you while you work";
                    return Go(player, 0.6f + 1.0f * aggr, stage, true);
                }
                if (Mode != BeastMode.Chase) Enter(BeastMode.Chase);
                Why = "Hunting you down";
                return Go(player, 2.6f + 2.0f * aggr, stage, true);
            }

            // Stages 1-2: it never touches you, it only gets closer.
            if (p.Watched && dist < FogEdge + 6f)
            {
                if (_watchedFor > 2.5f && dist < FogEdge)
                {
                    _calm = Mathf.Max(_calm, 3f);
                    Enter(BeastMode.Retreat);
                    Why = "You stared too long: it melts into the fog";
                    return Go(player + away * (FogEdge + 4f), 5f, stage, false);
                }
                if (Mode != BeastMode.Frozen) Enter(BeastMode.Frozen);
                Why = "Frozen: you are looking at it";
                return Go(me, 0f, stage, false);
            }

            if (!atConsole && dist < 5f && p.Phase != GamePhase.Title)
            {
                _calm = Mathf.Max(_calm, 3f);
                Enter(BeastMode.Retreat);
                Why = "You came too close: it backs away";
                return Go(player + away * (FogEdge + 4f), 5f, stage, false);
            }

            float pressure = Mathf.Clamp01(0.7f * aggr + 0.15f * (stage - 1));
            Vector3 behind = -Flat(p.PlayerForward);
            if (behind.sqrMagnitude < 0.01f) behind = away; else behind.Normalize();

            if (atConsole)
            {
                SetMode(BeastMode.Approach);
                float r = stage == 1 ? Mathf.Lerp(12f, 6f, aggr) : Mathf.Lerp(9f, 3.5f, aggr);
                if (_calm > 0) r = Mathf.Max(r, FogEdge - 4f);
                Why = _calm > 0 ? "Keeping its distance while you work" : "Creeping up behind you while you work the console";
                return Orbit(player, me, behind, r, 0.8f + 1.4f * aggr, stage);
            }

            if (_calm > 0 || pressure < 0.25f)
            {
                SetMode(BeastMode.Lurk);
                float wobble = Mathf.Sin(_orbitClock * 0.15f) * 60f;
                Why = _calm > 0 ? $"Lurking in the fog (calm for {_calm:0}s)" : "Lurking in the fog, watching";
                return Orbit(player, me, Quaternion.Euler(0, wobble, 0) * behind, FogEdge, 1.6f, stage);
            }

            SetMode(BeastMode.Stalk);
            float radius = Mathf.Lerp(FogEdge - 2f, stage == 1 ? 7f : 4.5f, pressure);
            float sway = Mathf.Sin(_orbitClock * 0.3f) * 35f;
            Why = "Stalking: closing in behind you";
            return Orbit(player, me, Quaternion.Euler(0, sway, 0) * behind, radius, 1.2f + 1.8f * aggr, stage);
        }

        void Enter(BeastMode m) { Mode = m; _modeTime = 0f; }
        void SetMode(BeastMode m) { if (Mode != m) Enter(m); }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        BeastIntent Go(Vector3 target, float speed, int stage, bool hunting) =>
            new BeastIntent { MoveTarget = target, Speed = speed, DesiredStage = stage, Hunting = hunting };

        /// <summary>
        /// Move around the player on a circle rather than through them: step the bearing toward
        /// the wanted bearing and the radius toward the wanted radius.
        /// </summary>
        BeastIntent Orbit(Vector3 player, Vector3 me, Vector3 wantDir, float wantRadius, float speed, int stage)
        {
            Vector3 rel = me - player;
            float r = rel.magnitude;
            float bearing = Mathf.Atan2(rel.x, rel.z) * Mathf.Rad2Deg;
            float want = Mathf.Atan2(wantDir.x, wantDir.z) * Mathf.Rad2Deg;
            float maxStep = Mathf.Rad2Deg * 3f / Mathf.Max(3f, r);   // ~3 m of arc per step
            float nb = bearing + Mathf.Clamp(Mathf.DeltaAngle(bearing, want), -maxStep, maxStep);
            float nr = r + Mathf.Clamp(wantRadius - r, -3f, 3f);
            var target = player + Quaternion.Euler(0, nb, 0) * Vector3.forward * nr;
            if (Mathf.Abs(wantRadius - r) < 0.3f && Mathf.Abs(Mathf.DeltaAngle(bearing, want)) < 3f) speed = 0f;
            return Go(target, speed, stage, false);
        }
    }
}
