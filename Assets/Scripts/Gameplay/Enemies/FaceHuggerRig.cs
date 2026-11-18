// 覆面虫的肢体骨架。根据身体姿势、移动状态和附着动作计算手指和掌部，再用结果构造网格和身体姿势。
using System;
using System.Collections.Generic;
using Unity.MP_FPS.SpiderLab;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS {
    /// <summary>掌形的寄生虫，带八根多节手指和一条尾巴。
    /// 服务器和客户端网格、碰撞体共用手指抓附的关节位置。</summary>
    public sealed class FaceHuggerRig : IDisposable {
        public const int Fingers = 8, FingerPoints = 4, TailPoints = 13;
        public readonly Vector3[] Joints = new Vector3[Fingers * FingerPoints];
        public readonly Vector3[] Tail = new Vector3[TailPoints];
        public Vector3 Centre { get; private set; }
        public float BodyRadius { get; private set; }
        public Quaternion CoreRotation => m_BodyRotation;
        public Vector3 CoreRadii { get; private set; }
        private readonly float m_Size;
        private readonly GameObject m_Visual;
        private readonly Transform m_Palm;
        private readonly Material m_Skin, m_Dark;
        private readonly Mesh m_Core, m_Limbs;
        private readonly List<Vector3> m_Vertices = new(2048);
        private readonly List<int> m_Indices = new(8192);
        private readonly Vector3[] m_Curve = new Vector3[13];
        private Quaternion m_BodyRotation;

        // 先创建身体关节和视觉需要的网格。背甲轮廓做得比普通平面更明显，俯视也能读出方向。
        public FaceHuggerRig(Transform owner, float scale, bool render) {
            m_Size = scale / .45f;
            if (!render) return;
            m_Visual = new GameObject("Face parasite / palm fingers tail");
            m_Visual.transform.SetParent(owner, false);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            m_Skin = new Material(shader) { name = "Parasite chitin", hideFlags = HideFlags.DontSave };
            m_Skin.SetColor("_BaseColor", new Color(.34f, .25f, .16f));
            m_Skin.SetFloat("_Smoothness", .4f);
            m_Dark = new Material(shader) { name = "Parasite dorsal scutes", hideFlags = HideFlags.DontSave };
            m_Dark.SetColor("_BaseColor", new Color(.15f, .10f, .065f));
            m_Dark.SetFloat("_Smoothness", .48f);
            m_Core = BuildPalm();
            m_Palm = MeshObject("Flattened abdominal palm", m_Core, m_Skin).transform;
            m_Limbs = new Mesh { name = "Gripping fingers and tail", hideFlags = HideFlags.DontSave };
            m_Limbs.MarkDynamic();
            MeshObject("Eight jointed fingers and tapered tail", m_Limbs, m_Skin);
            // 隆起的横向背甲让俯视的轮廓也清晰。
            for (int i = 0; i < 6; i++) {
                var plate = MeshObject("Abdominal scute " + i, m_Core, m_Dark).transform;
                plate.SetParent(m_Palm, false);
                plate.localPosition = new Vector3(0, .04f, -.13f + i * .05f);
                plate.localScale = new Vector3(.82f, .28f, .11f);
            }
        }

        // 把脚的支撑点和表面姿势变成各指节。附着时切到围住目标头侧的抓握姿势。
        public void Tick(SurfaceMotor.Pose surface, PredictiveFeet feet, AlienAction action, float age, float clock) {
            bool gripping = action == AlienAction.Attached;
            float size = gripping ? 1 : m_Size;
            var pose = feet != null && !gripping ? feet.BodyPose : surface;
            Centre = gripping ? surface.Position + surface.Forward * .12f : pose.Position;
            m_BodyRotation = gripping ? Quaternion.LookRotation(surface.Up, surface.Forward) : pose.Rotation;
            BodyRadius = .17f * size;
            CoreRadii = new Vector3(.145f, .047f, .21f) * size;
            float curl = action == AlienAction.Windup ? Mathf.Clamp01(age / .65f) :
                action == AlienAction.Dead ? Mathf.Clamp01(age / .5f) : 0;
            if (!gripping) Centre -= pose.Up * (.055f * curl * size);
            for (int finger = 0; finger < Fingers; finger++) {
                int row = finger % 4;
                float side = finger < 4 ? -1 : 1;
                float along = -.15f + row * .10f;
                int first = finger * FingerPoints;
                if (gripping) {
                    // 扁平腹部贴在脸前面，手指从正面勾向两侧太阳穴。
                    // 指尖伸向头骨后面，不穿过脑袋。
                    float breathe = Mathf.Sin(clock * 7 + row) * .008f;
                    var head = surface.Position - surface.Forward * .12f;
                    Joints[first] = Centre + surface.Right * (side * .105f) + surface.Up * along;
                    Joints[first + 1] = head + surface.Right * (side * (.29f + breathe)) + surface.Forward * .21f +
                                        surface.Up * (along * 1.1f);
                    Joints[first + 2] = head + surface.Right * (side * .27f) - surface.Forward * .045f +
                                        surface.Up * (along * 1.15f);
                    Joints[first + 3] = head + surface.Right * (side * .16f) - surface.Forward * .17f +
                                        surface.Up * (along * .95f);
                } else {
                    // 继续使用真实脚部触点。
                    // 每只脚用三节手指的关节形状，而非蜘蛛膝形。
                    int leg = finger < 4 ? finger : 11 - finger;
                    var foot = feet[leg].Position;
                    var hip = Centre + pose.Right * (side * .105f * size) + pose.Forward * (along * size);
                    var reach = foot - hip;
                    Joints[first] = hip;
                    Joints[first + 1] = hip + reach * .38f + pose.Up * (.12f + curl * .05f) * size;
                    Joints[first + 2] = hip + reach * .78f + pose.Up * .045f * size;
                    Joints[first + 3] = Vector3.Lerp(foot,
                        hip + pose.Right * side * .16f * size - pose.Up * .04f * size, curl * .55f);
                    if (action == AlienAction.Strike) {
                        float extension = Mathf.Sin(Mathf.Clamp01(age / .45f) * Mathf.PI);
                        Joints[first + 2] += pose.Forward * extension * .08f * size;
                        Joints[first + 3] += pose.Forward * extension * .12f * size;
                    }
                }
            }
            for (int i = 0; i < TailPoints; i++) {
                float t = i / (float)(TailPoints - 1);
                if (gripping) {
                    var head = surface.Position - surface.Forward * .12f;
                    if (t < .25f) {
                        Tail[i] = Vector3.Lerp(Centre - surface.Up * .20f,
                            head - surface.Up * .30f + surface.Forward * .16f, t * 4);
                    } else {
                        float a = (t - .25f) / .75f * Mathf.PI * 1.8f;
                        Tail[i] = head - surface.Up * (.30f + .05f * t) + surface.Right * Mathf.Sin(a) * .17f +
                                  surface.Forward * Mathf.Cos(a) * .16f;
                    }
                } else {
                    Tail[i] = Centre - pose.Forward * (.19f + .82f * t) * size +
                              pose.Right * Mathf.Sin(t * Mathf.PI * 2 - clock * 4) * .07f * t * size +
                              pose.Up * ((curl * .32f + (surface.Up.y < .5f ? .05f : -.12f)) * t * size);
                }
            }
            if (!m_Visual || !m_Visual.activeSelf) return;
            m_Palm.SetPositionAndRotation(Centre, m_BodyRotation);
            m_Palm.localScale = Vector3.one * size;
            DrawLimbs(size);
        }

        public void SetVisible(bool visible) {
            if (m_Visual) m_Visual.SetActive(visible);
        }

        private GameObject MeshObject(string name, Mesh mesh, Material material) {
            var go = new GameObject(name);
            go.transform.SetParent(m_Visual.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        // 沿实际关节点铺小段网格，更新一整条腿的外形。
        private void DrawLimbs(float size) {
            m_Vertices.Clear();
            m_Indices.Clear();
            var inverse = m_Visual.transform.worldToLocalMatrix;
            for (int finger = 0; finger < Fingers; finger++) {
                for (int i = 0; i < m_Curve.Length; i++)
                    m_Curve[i] = FingerPoint(finger, i / (float)(m_Curve.Length - 1));
                Tube(m_Curve, .029f * size, .006f * size, inverse, true);
            }
            Tube(Tail, .032f * size, .003f * size, inverse, false);
            m_Limbs.Clear();
            m_Limbs.SetVertices(m_Vertices);
            m_Limbs.SetTriangles(m_Indices, 0);
            m_Limbs.RecalculateNormals();
            m_Limbs.RecalculateBounds();
        }

        // 外层可以按指头编号和沿指距离取采样坐标，碰撞和液体表现可跟着同一关节位置。
        public Vector3 FingerPoint(int finger, float t) {
            int a = finger * FingerPoints;
            float u = 1 - t;
            return Joints[a] * (u * u * u) + Joints[a + 1] * (3 * u * u * t) + Joints[a + 2] * (3 * u * t * t) +
                   Joints[a + 3] * (t * t * t);
        }

        // 用半径逐渐变化的管段画指节，沿着骨架转向。
        private void Tube(Vector3[] points, float start, float end, Matrix4x4 inverse, bool knuckles) {
            const int sides = 8;
            int first = m_Vertices.Count;
            for (int i = 0; i < points.Length; i++) {
                var tangent = (points[Mathf.Min(points.Length - 1, i + 1)] - points[Mathf.Max(0, i - 1)]).normalized;
                var right = Vector3.Cross(tangent, Vector3.up);
                if (right.sqrMagnitude < .01f) right = Vector3.Cross(tangent, Vector3.right);
                right.Normalize();
                var up = Vector3.Cross(right, tangent).normalized;
                float t = i / (float)(points.Length - 1);
                float radius = Mathf.Lerp(start, end, t) *
                               (knuckles ? 1 + .23f * Mathf.Pow(Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3)), 8) : 1);
                for (int side = 0; side < sides; side++) {
                    float angle = side * Mathf.PI * 2 / sides;
                    m_Vertices.Add(inverse.MultiplyPoint3x4(points[i] +
                                                            (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) *
                                                            radius));
                    if (i == 0) continue;
                    int a = first + (i - 1) * sides + side, b = first + (i - 1) * sides + (side + 1) % sides;
                    int c = first + i * sides + side, d = first + i * sides + (side + 1) % sides;
                    m_Indices.Add(a);
                    m_Indices.Add(c);
                    m_Indices.Add(b);
                    m_Indices.Add(b);
                    m_Indices.Add(c);
                    m_Indices.Add(d);
                }
            }
        }

        private static Mesh BuildPalm() {
            const int rings = 10, sides = 24;
            var vertices = new Vector3[(rings + 1) * sides];
            var triangles = new List<int>();
            for (int ring = 0; ring <= rings; ring++) {
                float latitude = ring * Mathf.PI / rings;
                for (int side = 0; side < sides; side++) {
                    float a = side * Mathf.PI * 2 / sides;
                    float bulge = 1 + .12f * Mathf.Sin(a * 2) * Mathf.Sin(latitude);
                    vertices[ring * sides + side] = new Vector3(Mathf.Cos(a) * Mathf.Sin(latitude) * .145f * bulge,
                        Mathf.Cos(latitude) * .047f, Mathf.Sin(a) * Mathf.Sin(latitude) * .21f);
                    if (ring == 0) continue;
                    int p = (ring - 1) * sides + side, q = (ring - 1) * sides + (side + 1) % sides;
                    int r = ring * sides + side, s = ring * sides + (side + 1) % sides;
                    triangles.Add(p);
                    triangles.Add(q);
                    triangles.Add(r);
                    triangles.Add(q);
                    triangles.Add(s);
                    triangles.Add(r);
                }
            }
            var mesh = new Mesh { name = "Parasite abdominal shield", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Dispose() {
            Destroy(m_Visual);
            Destroy(m_Core);
            Destroy(m_Limbs);
            Destroy(m_Skin);
            Destroy(m_Dark);
        }

        private static void Destroy(Object value) {
            if (!value) return;
            if (Application.isPlaying)
                Object.Destroy(value);
            else
                Object.DestroyImmediate(value);
        }
    }
}