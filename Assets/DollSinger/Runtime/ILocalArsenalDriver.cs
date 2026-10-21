namespace Unity.MP_FPS.DollSinger
{
    // The standalone character delegates gameplay rules across the runtime assembly boundary.
    public interface ILocalArsenalDriver
    {
        int Ammo(DollSingerHaloAim.HaloWeapon weapon);
        bool Reloading { get; }
        void CancelBurst();
        bool Step(DollSingerHaloAim halo, DollSingerHaloAim.HaloWeapon weapon, bool held,
            bool pressed, bool reload, bool aiming, float deltaTime);
    }
}
