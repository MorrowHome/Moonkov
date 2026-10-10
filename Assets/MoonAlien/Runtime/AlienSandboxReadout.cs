using UnityEngine;

namespace Unity.MP_FPS.MoonAlien
{
    public sealed class AlienSandboxReadout : MonoBehaviour
    {
        [SerializeField] private AlienSandboxDriver m_Driver;
        [SerializeField] private ProceduralAlienLegs m_Legs;
        public void Configure(AlienSandboxDriver driver, ProceduralAlienLegs legs) { m_Driver = driver; m_Legs = legs; }
        private void OnGUI()
        {
            if (!m_Driver || !m_Legs) return;
            GUILayout.BeginArea(new Rect(16, 16, 440, 175), GUI.skin.box);
            GUILayout.Label("ALIEN SANDBOX / foundation only");
            GUILayout.Label(m_Driver.Status + " | speed " + m_Legs.Speed.ToString("F1") + " m/s");
            GUILayout.Label("Planted: " + m_Legs.PlantedFeet + " / 4 | reach clamps: " + m_Legs.ReachClamps);
            GUILayout.Label("Walls / ceiling / corners are pending adhesion fixtures.");
            GUILayout.Label("No combat, navigation, hit registration or networking.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pause")) m_Driver.SetRunning(false);
            if (GUILayout.Button("Run")) m_Driver.SetRunning(true);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
