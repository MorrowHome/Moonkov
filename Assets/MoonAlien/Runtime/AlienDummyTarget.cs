using UnityEngine;

namespace Unity.MP_FPS.MoonAlien
{
    public sealed class AlienDummyTarget : MonoBehaviour
    {
        [SerializeField] private bool m_Automatic;
        private float m_Time;
        private void Update()
        {
            if (!m_Automatic) return;
            m_Time += Time.deltaTime;
            // Deliberately moves through cover for a sensing test, never drives the alien brain.
            transform.localPosition = new Vector3(0f, 0f, 2.5f + Mathf.PingPong(m_Time * .8f, 7f));
        }
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 265, 670, 75), GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Dummy: front")) { m_Automatic = false; transform.localPosition = new Vector3(0, 0, 2.5f); }
            if (GUILayout.Button("Dummy: behind cover")) { m_Automatic = false; transform.localPosition = new Vector3(0, 0, 9f); }
            if (GUILayout.Button("Dummy: walk")) { m_Automatic = true; m_Time = 0; }
            GUILayout.EndHorizontal();
            GUILayout.Label("Dummy reposition is a test control. Alien learns a new position only after LOS + range + FOV.");
            GUILayout.EndArea();
        }
    }
}
