using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS
{
    public static class HaloArsenal
    {
        public readonly struct Entry
        {
            public readonly string Code, Name;
            public readonly int Capacity, Energy, Pellets, BurstCount;
            public readonly float Interval, Damage, Reload, Equip, Speed, Spread, Gravity, Scope, BurstCooldown, BlastRadius;
            public readonly bool Automatic;
            public readonly uint Id;
            public Entry(uint id,string code,string name,int capacity,int energy,float interval,float damage,float reload,float equip,float speed,float spread=0,int pellets=1,float gravity=1.62f,float scope=0,bool automatic=false,int burst=1,float burstCooldown=.28f,float blastRadius=0)
            { Id=id;Code=code;Name=name;Capacity=capacity;Energy=energy;Interval=interval;Damage=damage;Reload=reload;Equip=equip;Speed=speed;Spread=spread;Pellets=pellets;Gravity=gravity;Scope=scope;Automatic=automatic;BurstCount=burst;BurstCooldown=burstCooldown;BlastRadius=blastRadius; }
            public DollSingerHaloAim.HaloWeapon Gesture => (DollSingerHaloAim.HaloWeapon)(Id-1);
        }
        public static readonly Entry[] Entries = {
            new Entry(6,"cicada","Cicada / 蝉鸣",36,2,.065f,20,1.7f,.18f,240,3.4f,automatic:true),
            new Entry(7,"trine","Trine / 三律",24,4,.085f,32,2.2f,.24f,420,.6f,burst:3),
            new Entry(8,"aegis","Aegis / 天枢",72,4,.11f,34,4.2f,.55f,360,1.9f,automatic:true),
            new Entry(9,"vesper","Vesper / 望舒",12,8,.32f,65,2.4f,.30f,650,.12f,scope:2.8f),
            new Entry(10,"thorn","Thorn / 星棘",8,10,.55f,22,2.5f,.27f,530,1.4f,5),
            new Entry(11,"sundown","Sundown / 坠日",3,18,1.05f,320,3.4f,.42f,22,0,1,1.62f,blastRadius:3.5f),
            new Entry(12,"rondo","Rondo / 回环",16,6,.07f,40,2.1f,.24f,360,.55f,burst:2,burstCooldown:.38f),
            new Entry(13,"tempest","Tempest / 岚轮",14,7,.26f,110,2.8f,.32f,34,1.2f,1,3.24f,automatic:true,blastRadius:1.35f),
            new Entry(14,"nocturne","Nocturne / 夜曲",10,14,.62f,65,2.6f,.34f,780,.18f,2,scope:3.5f)
        };
        public static Entry? Find(uint id) { int i=(int)id-6; return i>=0 && i<Entries.Length ? Entries[i] : (Entry?)null; }
    }
}
