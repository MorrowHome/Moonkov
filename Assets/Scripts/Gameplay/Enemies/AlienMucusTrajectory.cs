// 按喷射起点、目标和飞行时间计算黏液在哪。服务端沿这条曲线扫过碰撞，客户端可用同一个函数画飞行过程。
using UnityEngine;

namespace Unity.MP_FPS {
    public static class AlienMucusTrajectory {
        public static float FlightTime(Vector3 origin, Vector3 target, float speed) {
            return Mathf.Clamp(Vector3.Distance(origin, target) / Mathf.Max(3, speed), .2f, 4);
        }

        public static Vector3 Point(Vector3 origin, Vector3 target, float speed, float age, bool lowArc = false) {
            float duration = FlightTime(origin, target, speed);
            float gravity = lowArc ? .35f : 5;
            var velocity = (target - origin) / duration + Vector3.up * (gravity * duration);
            return origin + velocity * age - Vector3.up * (gravity * age * age);
        }
    }
}