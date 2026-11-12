using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    internal sealed class ClassicMinimapView : IDisposable
    {
        private const int Resolution = 256, Padding = 16;
        private static readonly Color32 Floor = new(82, 88, 98, 255);
        private static readonly Color32 Edge = new(115, 122, 133, 255);
        private static readonly Color32 AttackColor = new(255, 202, 22, 255);
        private static readonly Color32 DefenseColor = new(112, 184, 255, 255);
        private readonly VisualElement m_Panel, m_Markers;
        private readonly Label m_SiteA, m_SiteB;
        private readonly Vector2[] m_Teammates = new Vector2[ClassicMatch.TeamSize];
        private World m_World;
        private EntityQuery m_Actors;
        private ClassicDust2Map m_Map;
        private Texture2D m_Surface;
        private Vector2 m_Origin, m_Player, m_Heading;
        private float m_Scale;
        private int m_TeammateCount;
        private bool m_ShowPlayer;
        private Color m_TeamColor;
        private double m_NextUpdate;

        public ClassicMinimapView(VisualElement root)
        {
            m_Panel = new VisualElement { name = "classic-minimap", pickingMode = PickingMode.Ignore };
            m_Panel.AddToClassList("classic-minimap");
            m_SiteA = CreateSite("A");
            m_SiteB = CreateSite("B");
            m_Markers = new VisualElement { pickingMode = PickingMode.Ignore };
            m_Markers.StretchToParentSize();
            m_Markers.generateVisualContent += DrawMarkers;
            m_Panel.Add(m_Markers);
            root.Add(m_Panel);
            Hide();
        }

        private Label CreateSite(string name)
        {
            var label = new Label(name) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("classic-minimap-site");
            m_Panel.Add(label);
            return label;
        }

        public void Hide()
        {
            if (m_Panel.style.display != DisplayStyle.None) m_Panel.style.display = DisplayStyle.None;
            m_NextUpdate = 0;
        }

        public void Present(ClassicDust2Map map, World world, int team, bool attacking, bool alive)
        {
            if (!map) { Hide(); return; }
            if (m_Map != map)
            {
                m_Map = map;
                if (m_Surface) UnityEngine.Object.Destroy(m_Surface);
                m_Surface = CreateSurface(NavMesh.CalculateTriangulation(), map.Spawn(attacking: true, slot: 0).Position);
                m_Panel.style.backgroundImage = new StyleBackground(m_Surface);
                PositionSite(m_SiteA, map.Site(0));
                PositionSite(m_SiteB, map.Site(1));
            }
            if (!m_Surface) { Hide(); return; }
            if (m_World != world)
            {
                if (m_World != null && m_World.IsCreated) m_Actors.Dispose();
                m_World = world;
                m_Actors = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<PredictedPlayerGhost>());
                m_NextUpdate = 0;
            }
            if (Time.unscaledTimeAsDouble < m_NextUpdate) return;
            m_NextUpdate = Time.unscaledTimeAsDouble + .1;
            m_TeamColor = attacking ? AttackColor : DefenseColor;
            m_TeammateCount = 0;
            m_ShowPlayer = false;
            var manager = world.EntityManager;
            using var actors = m_Actors.ToEntityArray(Allocator.Temp);
            foreach (var actor in actors)
            {
                var health = manager.GetComponentData<PredictedPlayerGhost>(actor);
                if (!health.ClashEnabled || health.ClashTeam != team || health.CurrentHealth <= 0 ||
                    health.ClashLife is not (ClashLife.Alive or ClashLife.Waiting)) continue;
                var point = Project(manager.GetComponentData<LocalTransform>(actor).Position);
                if (manager.HasComponent<GhostOwnerIsLocal>(actor) && manager.IsComponentEnabled<GhostOwnerIsLocal>(actor))
                {
                    if (!alive) continue;
                    float yaw = (health.ControllerState.YawDegrees + health.ControllerState.FreeLookOffset.x) * Mathf.Deg2Rad;
                    m_Heading = new Vector2(Mathf.Sin(yaw), -Mathf.Cos(yaw));
                    m_Player = point;
                    m_ShowPlayer = true;
                }
                else if (m_TeammateCount < m_Teammates.Length)
                    m_Teammates[m_TeammateCount++] = point;
            }
            if (m_Panel.style.display != DisplayStyle.Flex) m_Panel.style.display = DisplayStyle.Flex;
            m_Markers.MarkDirtyRepaint();
        }

        // The texture uses +Z up. UI coordinates start from the top left.
        private Vector2 ToPixel(Vector3 position) =>
            new Vector2(position.x - m_Origin.x, position.z - m_Origin.y) * m_Scale + Vector2.one * (Resolution * .5f);

        private Vector2 Project(Vector3 position)
        {
            var pixel = ToPixel(position) / Resolution;
            return new Vector2(pixel.x, 1 - pixel.y);
        }

        private void PositionSite(Label label, Vector3 position)
        {
            var point = Project(position);
            label.style.left = Length.Percent(point.x * 100);
            label.style.top = Length.Percent(point.y * 100);
        }

        private Texture2D CreateSurface(NavMeshTriangulation mesh, Vector3 spawn)
        {
            if (mesh.indices.Length == 0) return null;
            var parents = new int[mesh.vertices.Length];
            int Root(int index)
            {
                while (parents[index] != index)
                {
                    parents[index] = parents[parents[index]];
                    index = parents[index];
                }
                return index;
            }

            // NavMesh tiles repeat border vertices. Join them before finding the area connected to spawn.
            var shared = new Dictionary<Vector3, int>(mesh.vertices.Length);
            int nearest = 0;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < mesh.vertices.Length; i++)
            {
                Vector3 vertex = mesh.vertices[i];
                if (shared.TryGetValue(vertex, out int existing)) parents[i] = existing;
                else { shared.Add(vertex, i); parents[i] = i; }
                float distance = (vertex - spawn).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearest = i;
                nearestDistance = distance;
            }
            for (int i = 0; i < mesh.indices.Length; i += 3)
            {
                int root = Root(mesh.indices[i]);
                parents[Root(mesh.indices[i + 1])] = root;
                parents[Root(mesh.indices[i + 2])] = root;
            }
            int island = Root(nearest);
            var bounds = new Bounds(mesh.vertices[nearest], Vector3.zero);
            for (int i = 0; i < mesh.vertices.Length; i++)
                if (Root(i) == island) bounds.Encapsulate(mesh.vertices[i]);
            m_Origin = new Vector2(bounds.center.x, bounds.center.z);
            m_Scale = (Resolution - 2 * Padding) / Mathf.Max(1, bounds.size.x, bounds.size.z);
            var vertices = new Vector2[mesh.vertices.Length];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = ToPixel(mesh.vertices[i]);

            // Collapse the walkable triangles onto X/Z once per map, without a second camera.
            var walkable = new bool[Resolution * Resolution];
            for (int i = 0; i < mesh.indices.Length; i += 3)
                if (Root(mesh.indices[i]) == island)
                    Rasterize(walkable, vertices[mesh.indices[i]], vertices[mesh.indices[i + 1]], vertices[mesh.indices[i + 2]]);
            var pixels = new Color32[walkable.Length];
            for (int y = 1; y < Resolution - 1; y++)
                for (int x = 1; x < Resolution - 1; x++)
                {
                    int pixel = y * Resolution + x;
                    if (walkable[pixel]) pixels[pixel] = Floor;
                    else if (walkable[pixel - 1] || walkable[pixel + 1] || walkable[pixel - Resolution] || walkable[pixel + Resolution])
                        pixels[pixel] = Edge;
                }
            var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
            {
                name = "Classic Dust II minimap", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static float Side(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        private static void Rasterize(bool[] walkable, Vector2 a, Vector2 b, Vector2 c)
        {
            float area = Side(a, b, c);
            if (Mathf.Abs(area) < .0001f) return;
            if (area < 0) (b, c) = (c, b);
            var min = Vector2.Min(a, Vector2.Min(b, c));
            var max = Vector2.Max(a, Vector2.Max(b, c));
            for (int y = Mathf.FloorToInt(min.y); y < Mathf.CeilToInt(max.y); y++)
                for (int x = Mathf.FloorToInt(min.x); x < Mathf.CeilToInt(max.x); x++)
                {
                    var point = new Vector2(x + .5f, y + .5f);
                    if (Side(a, b, point) >= 0 && Side(b, c, point) >= 0 && Side(c, a, point) >= 0)
                        walkable[y * Resolution + x] = true;
                }
        }

        private void DrawMarkers(MeshGenerationContext context)
        {
            var size = m_Markers.contentRect.size;
            var painter = context.painter2D;
            painter.fillColor = m_TeamColor;
            painter.strokeColor = new Color32(17, 17, 19, 255);
            painter.lineWidth = 2;
            for (int i = 0; i < m_TeammateCount; i++)
            {
                painter.BeginPath();
                painter.Arc(Vector2.Scale(m_Teammates[i], size), 3.5f, Angle.Degrees(0), Angle.Degrees(360));
                painter.Fill();
                painter.Stroke();
            }
            if (m_ShowPlayer) RaidMinimapView.Triangle(painter, Vector2.Scale(m_Player, size), m_Heading, 7, Color.white);
        }

        public void Dispose()
        {
            m_Markers.generateVisualContent -= DrawMarkers;
            if (m_World != null && m_World.IsCreated) m_Actors.Dispose();
            if (m_Surface) UnityEngine.Object.Destroy(m_Surface);
        }
    }
}
