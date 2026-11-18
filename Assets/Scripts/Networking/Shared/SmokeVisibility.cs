// 烟雾从展开到消散的尺寸与密度供画面和服务端感知共用。视线穿过一团或多团烟时累加遮挡，达到阈值会阻止 AI 等继续看见目标。
using UnityEngine;

namespace Unity.MP_FPS {
    // 客户端绘制和服务端感知用同样的椭球展开与淡出。
    // 烟雾只影响视线，不参与碰撞、伤害、听觉或导航查询。
    public static class SmokeVisibility {
        public const float Duration = 20, BloomSeconds = 1.2f, FadeSeconds = 3;

        // 烟雾开始快速展开，临近结束时密度逐步下降。客户端表现和 AI 视线使用同样的体积参数。
        public static float Growth(float remaining) {
            return Mathf.Lerp(.12f, 1, Mathf.SmoothStep(0, 1, (Duration - remaining) / BloomSeconds));
        }

        public static float Density(float remaining) { return Mathf.Clamp01(remaining / FadeSeconds); }

        public static Vector3 Radii(float radius, float remaining) {
            return new Vector3(radius, radius * .6f, radius) * Growth(remaining);
        }

        public static Vector3 Center(Vector3 origin) { return origin + Vector3.up * 1.4f; }

        public static bool Blocked(TacticalServerWorld world, PhysicsScene scene, Vector3 from, Vector3 to) {
            if (world == null) return false;
            float depth = 0;
            foreach (var cloud in world.Objects) {
                // 只算同一地图里仍有效的烟。视线经过几团稀烟的遮挡会叠加，达到阈值可提前返回。
                if (cloud.Phase != TacticalWorldPhase.Smoke || cloud.Scene != scene || cloud.Until <= world.Now)
                    continue;
                float remaining = (float)(cloud.Until - world.Now);
                depth += OpticalDepth(from, to, Center(cloud.Position), Radii(cloud.Definition.Radius, remaining),
                    Density(remaining));
                if (depth >= 1.6f) return true;
            }
            return false;
        }

        public static float OpticalDepth(Vector3 from, Vector3 to, Vector3 center, Vector3 radii, float density) {
            if (density <= 0 || radii.x <= 0 || radii.y <= 0 || radii.z <= 0) return 0;
            var segment = to - from;
            float length = segment.magnitude;
            if (length < .0001f) return 0;
            // 把各轴按烟团半径缩放，椭球问题就能当单位球来解。随后算视线在哪儿进入、在哪儿离开烟团。
            Vector3 origin = Divide(from - center, radii), direction = Divide(segment / length, radii);
            float a = Vector3.Dot(direction, direction), b = Vector3.Dot(origin, direction);
            float discriminant = b * b - a * (Vector3.Dot(origin, origin) - 1);
            if (discriminant <= 0) return 0;
            float root = Mathf.Sqrt(discriminant);
            // 把交段夹在视线的实际起始和终点之间。如果烟团在身后或目标远处，并不会影响眼前这段。
            float enter = Mathf.Max(0, (-b - root) / a), exit = Mathf.Min(length, (-b + root) / a);
            if (exit <= enter) return 0;
            float step = (exit - enter) / 12, depth = 0;
            // 在穿烟区段分为有限步数，中心更浓、边缘更稀。按路径长度、密度与步幅累积遮挡。
            for (int i = 0; i < 12; i++) {
                float radius = (origin + direction * (enter + (i + .5f) * step)).magnitude;
                depth += Mathf.Clamp01((1 - radius) * 5) * step * density * 2;
            }
            return depth;
        }

        private static Vector3 Divide(Vector3 a, Vector3 b) { return new Vector3(a.x / b.x, a.y / b.y, a.z / b.z); }
    }
}