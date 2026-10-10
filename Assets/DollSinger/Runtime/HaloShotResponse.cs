using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Cosmetic, bounded release shared by single-shot halos. Tick zero is a local demo shot.</summary>
    public struct HaloShotResponse
    {
        private float slow, fast;
        private uint lastTick;
        public float Release => Mathf.Clamp01((slow - fast) * 1.8f);

        public bool Play(uint tick)
        {
            // The snapshot and predicted/RPC effect may arrive in either order.
            if (tick != 0 && lastTick != 0 && (int)(tick - lastTick) <= 0) return false;
            if (tick != 0) lastTick = tick;
            slow = fast = 1f;
            return true;
        }

        public void Step(float deltaTime, float recovery, float attack)
        {
            float dt = Mathf.Max(0, deltaTime);
            slow *= Mathf.Exp(-dt * recovery);
            fast *= Mathf.Exp(-dt * attack);
        }

        public void Reset() { slow = fast = 0; lastTick = 0; }
    }
}
