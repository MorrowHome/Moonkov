using UnityEngine;

namespace Unity.MP_FPS.MoonAlien
{
    public sealed class AlienAdhesionReadout : MonoBehaviour
    {
        [SerializeField] private AlienAdhesionRoute[] m_Routes;
        private ProceduralAlienLegs[] m_Legs;
        public void Configure(AlienAdhesionRoute[] routes) => m_Routes = routes;
        private void Start()
        {
            m_Legs = new ProceduralAlienLegs[m_Routes.Length];
            for (int i = 0; i < m_Routes.Length; i++) m_Legs[i] = m_Routes[i].GetComponent<ProceduralAlienLegs>();
        }
        private void OnGUI()
        {
            if (m_Routes == null || m_Legs == null) return;
            GUILayout.BeginArea(new Rect(16, 16, 620, 260), GUI.skin.box);
            GUILayout.Label("ADHESION ACCEPTANCE / authored route prototype / no combat or autonomous navigation");
            for (int i = 0; i < m_Routes.Length; i++)
            {
                if (!m_Routes[i] || !m_Legs[i]) continue;
                GUILayout.Label(m_Routes[i].name + ": " + m_Routes[i].Status);
                GUILayout.Label("Contacts " + m_Legs[i].PlantedFeet + " | IK reach clamps " + m_Legs[i].ReachClamps);
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pause")) foreach (var route in m_Routes) route.SetRunning(false);
            if (GUILayout.Button("Run")) foreach (var route in m_Routes) route.SetRunning(true);
            if (GUILayout.Button("1.5 m/s")) foreach (var route in m_Routes) route.SetFast(false);
            if (GUILayout.Button("6 m/s")) foreach (var route in m_Routes) route.SetFast(true);
            GUILayout.EndHorizontal();
            GUILayout.Label("Delete a supporting collider in Play to test safe hold. Stop/Play resets the course.");
            GUILayout.EndArea();
        }
    }
}
