// 按喷射起点、目标和飞行时间计算黏液在哪。服务端沿这条曲线扫过碰撞，客户端可用同一个函数画飞行过程。
using UnityEngine;

namespace Unity.MP_FPS {
    public static class AlienMucusTrajectory {
        // 由喷射距离和设置速度算一段有界的飞行时间。过远或过近也有最长和最短时间。
        public static float FlightTime(Vector3 origin, Vector3 target, float speed) {
            return Mathf.Clamp(Vector3.Distance(origin, target) / Mathf.Max(3, speed), .2f, 4);
        }

        public static Vector3 Point(Vector3 origin, Vector3 target, float speed, float age, bool lowArc = false) {
            float duration = FlightTime(origin, target, speed);
            // 低弹道可绕开较低的天花板。由终点反推初速度，两种弧线在同一个飞行时长后都到同一处。
            float gravity = lowArc ? .35f : 5;
            var velocity = (target - origin) / duration + Vector3.up * (gravity * duration);
            return origin + velocity * age - Vector3.up * (gravity * age * age);
        }
    }
}