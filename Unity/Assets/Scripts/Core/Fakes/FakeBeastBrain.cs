namespace Fnmw.Core.Fakes
{
    /// <summary>Walks straight at the player, faster with aggression and stage. Replaced by Scripts/Monster.</summary>
    public class FakeBeastBrain : IBeastBrain
    {
        public BeastIntent Think(BeastPerception p, float dt)
        {
            int stage = p.Elapsed > 70f ? 3 : p.Elapsed > 35f ? 2 : 1;
            bool hunting = stage == 3;
            float speed = (0.35f + 0.4f * p.Aggression) * (hunting ? 3f : stage);
            return new BeastIntent { MoveTarget = p.PlayerPosition, Speed = speed, DesiredStage = stage, Hunting = hunting };
        }
    }
}
