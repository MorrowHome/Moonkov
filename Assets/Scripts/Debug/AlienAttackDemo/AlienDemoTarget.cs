namespace Unity.MP_FPS
{
    // A real humanoid target identity, without input, transport, cameras or profile data.
    public sealed class AlienDemoTarget : PlayerGhost
    {
        public new void Awake() { }
        public override bool AllowAutoRegisterForUpdates() => false;
        public override void OnGhostLinked() { }
        public override void OnGhostPreDestroy() { }
    }
}
