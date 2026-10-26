using UnityEngine;

namespace Unity.MP_FPS.Client
{
    public sealed class HaloProgressArtwork : ScriptableObject
    {
        public Sprite Petal, OuterArc, InnerArc, Cell;
        public Color Color = new Color(1f,.18f,.42f,1);
    }
}
