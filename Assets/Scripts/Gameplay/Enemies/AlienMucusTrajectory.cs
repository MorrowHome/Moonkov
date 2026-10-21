using UnityEngine;

namespace Unity.MP_FPS
{
    public static class AlienMucusTrajectory
    {
        public static float FlightTime(Vector3 origin, Vector3 target, float speed)
            => Mathf.Clamp(Vector3.Distance(origin,target)/Mathf.Max(3,speed),.2f,4);
        public static Vector3 Point(Vector3 origin, Vector3 target, float speed, float age)
        {
            float duration=FlightTime(origin,target,speed);
            Vector3 velocity=(target-origin)/duration+Vector3.up*(5*duration);
            return origin+velocity*age-Vector3.up*(5*age*age);
        }
    }
}
