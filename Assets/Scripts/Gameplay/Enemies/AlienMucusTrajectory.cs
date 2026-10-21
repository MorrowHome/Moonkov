using UnityEngine;

namespace Unity.MP_FPS
{
    public static class AlienMucusTrajectory
    {
        public static float FlightTime(Vector3 origin, Vector3 target, float speed)
            => Mathf.Clamp(Vector3.Distance(origin,target)/Mathf.Max(3,speed),.2f,4);
        public static Vector3 Point(Vector3 origin, Vector3 target, float speed, float age, bool lowArc = false)
        {
            float duration=FlightTime(origin,target,speed);
            float gravity = lowArc ? .35f : 5;
            Vector3 velocity=(target-origin)/duration+Vector3.up*(gravity*duration);
            return origin+velocity*age-Vector3.up*(gravity*age*age);
        }
    }
}
