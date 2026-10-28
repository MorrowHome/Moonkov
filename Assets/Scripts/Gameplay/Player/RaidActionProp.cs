using UnityEngine;

namespace Unity.MP_FPS
{
    // Metre-space anchors on an action variant of the actual inventory model.
    // The original loot/icon prefab stays sealed and is never modified.
    public sealed class RaidActionProp : MonoBehaviour
    {
        public Transform Grip, Contact, Cap, Plunger;
        public GameObject Sealed, Opened;
        public GameObject Needle;
        public float PlungerTravel;
        [System.Serializable] public struct Part { public string Name; public Transform Transform; }
        public Part[] Parts;
        public bool Authored;
        public Transform FindPart(string name)
        {
            if(Parts!=null)foreach(var part in Parts)if(part.Name==name)return part.Transform;
            if(name=="Cap")return Cap;if(name=="Plunger")return Plunger;
            if(name=="Sealed"&&Sealed)return Sealed.transform;if((name=="Opened"||name=="Body")&&Opened)return Opened.transform;
            if(name=="Needle"&&Needle)return Needle.transform;return null;
        }
        public void Present(float phase, bool injecting)
        {
            bool open = phase >= .18f;
            if (Sealed) Sealed.SetActive(!open);
            if (Opened) Opened.SetActive(!Sealed || open);
            if (Cap) Cap.gameObject.SetActive(!open);
            if (Needle) Needle.SetActive(open);
            if (Plunger)
                Plunger.localPosition = Vector3.down * (injecting ? PlungerTravel * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.46f, .72f, phase)) : 0);
        }
    }
}
