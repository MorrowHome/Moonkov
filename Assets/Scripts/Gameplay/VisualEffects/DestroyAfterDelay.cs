using UnityEngine;

namespace Unity.MP_FPS
{
    public class DestroyAfterDelay : MonoBehaviour
    {
        public float Lifetime = 1f;

        private void Start()
        {
            Destroy(gameObject, Lifetime);
        }
    }
}