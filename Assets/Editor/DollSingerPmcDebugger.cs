using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

public sealed class DollSingerPmcDebugger : EditorWindow
{
    private readonly List<(DollSingerEnemyBrain brain, Vector3 position, float health)> m_Rows = new();
    private double m_NextRead;
    private Vector2 m_Scroll;
    private bool m_Draw;

    [MenuItem("Moonkov/AI/PMC Debugger")]
    private static void Open() => GetWindow<DollSingerPmcDebugger>("PMC decisions");
    private void OnEnable() => SceneView.duringSceneGui += DrawScene;
    private void OnDisable() => SceneView.duringSceneGui -= DrawScene;
    private void OnInspectorUpdate() => Repaint();

    private void OnGUI()
    {
        if (EditorApplication.timeSinceStartup >= m_NextRead)
        {
            m_NextRead = EditorApplication.timeSinceStartup + .5;
            m_Rows.Clear();
            foreach (var world in World.All)
            {
                if (!world.IsCreated || !world.IsServer()) continue;
                var em = world.EntityManager;
                using var query = em.CreateEntityQuery(typeof(DollSingerEnemy), typeof(DollSingerEnemyBrain), typeof(LocalTransform), typeof(PredictedPlayerGhost));
                using var enemies = query.ToEntityArray(Allocator.Temp);
                foreach (var entity in enemies) m_Rows.Add((em.GetComponentObject<DollSingerEnemyBrain>(entity),
                    em.GetComponentData<LocalTransform>(entity).Position, em.GetComponentData<PredictedPlayerGhost>(entity).CurrentHealth));
            }
        }
        EditorGUILayout.HelpBox("Read-only server observations. Open during Host Play Mode. Cyan: route goal; orange: uncertain contact; green: cover.", MessageType.Info);
        m_Draw = EditorGUILayout.Toggle("Draw in Scene view", m_Draw);
        m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
        foreach (var row in m_Rows)
        {
            var b = row.brain;
            EditorGUILayout.LabelField($"PMC {b.Seed} — {b.Action} ({b.ActionScore:0})", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(b.Reason);
            EditorGUILayout.LabelField($"HP {row.health:0} | confidence {b.Confidence:0.00} | pressure {b.Suppression:0.00} | visible {b.Visible}");
            EditorGUILayout.LabelField($"Caches {b.LootedCaches} | dust {b.Inventory.Count("dust")} | alloy {b.Inventory.Count("alloy")} | cells {b.Inventory.Count("cells")}");
            EditorGUILayout.LabelField($"Goal {b.TacticalGoal} | path failed {b.PathFailed} | extraction {b.ExtractionProgress:0.0}s");
            EditorGUILayout.Space(8);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawScene(SceneView view)
    {
        if (!m_Draw || !EditorApplication.isPlaying) return;
        foreach (var row in m_Rows)
        {
            var b = row.brain;
            Handles.Label(row.position + Vector3.up * 2, $"PMC {b.Seed}: {b.Action}");
            Handles.color = Color.cyan;
            Handles.DrawLine(row.position + Vector3.up, b.TacticalGoal + Vector3.up);
            if (b.Target != Entity.Null)
            {
                Handles.color = new Color(1, .6f, .1f);
                Handles.DrawWireDisc(b.LastSeen + Vector3.up * .1f, Vector3.up, 1 + (1 - b.Confidence) * 12);
            }
            if (!b.HasCover) continue;
            Handles.color = Color.green;
            Handles.DrawLine(b.CoverPosition + Vector3.up, b.PeekPosition + Vector3.up);
        }
    }
}
