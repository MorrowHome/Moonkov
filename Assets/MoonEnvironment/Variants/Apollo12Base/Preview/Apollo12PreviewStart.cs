using UnityEngine;
using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS.Moon.Preview
{
    /// <summary>
    /// Local map preview only. BindPlayer resets the shared camera during Awake,
    /// so choose the starting view once in Start; MMB switching stays available.
    /// This does not belong on the network character or the reusable environment.
    /// </summary>
    public sealed class Apollo12PreviewStart : MonoBehaviour
    {
        [SerializeField] private DollSingerView view;

        private void Start()
        {
            if (view) view.SetFirstPerson(true);
        }
    }
}
