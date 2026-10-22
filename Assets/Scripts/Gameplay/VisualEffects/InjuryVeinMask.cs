using UnityEngine;

namespace Unity.MP_FPS
{
    // Original deterministic vascular art, built once and shared. No per-frame texture work.
    internal static class InjuryVeinMask
    {
        private static Texture2D s_Texture;
        private static int s_Users;
        internal static Texture2D Retain()
        {
            s_Users++;if(s_Texture)return s_Texture;
            const int size=512;var pixels=new Color32[size*size];var random=new System.Random(73951);
            float Next(float low,float high)=>Mathf.Lerp(low,high,(float)random.NextDouble());
            void Stamp(Vector2 a,Vector2 b,float width,int channel)
            {
                a*=size;b*=size;float radius=width*size;
                int x0=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.x,b.x)-radius-1)),x1=Mathf.Min(size-1,Mathf.CeilToInt(Mathf.Max(a.x,b.x)+radius+1));
                int y0=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.y,b.y)-radius-1)),y1=Mathf.Min(size-1,Mathf.CeilToInt(Mathf.Max(a.y,b.y)+radius+1));
                Vector2 ab=b-a;float length=Mathf.Max(.001f,ab.sqrMagnitude);
                for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                {
                    Vector2 p=new Vector2(x+.5f,y+.5f),nearest=a+ab*Mathf.Clamp01(Vector2.Dot(p-a,ab)/length);
                    byte value=(byte)(Mathf.Clamp01(radius+.65f-Vector2.Distance(p,nearest))*255);
                    int i=y*size+x;if(channel==0)pixels[i].r=(byte)Mathf.Max(pixels[i].r,value);else pixels[i].g=(byte)Mathf.Max(pixels[i].g,value);
                }
            }
            void Grow(Vector2 start,Vector2 direction,float length,float width,int depth)
            {
                Vector2 p=start;
                for(int step=0;step<12;step++)
                {
                    float angle=Next(-.18f,.18f);float c=Mathf.Cos(angle),s=Mathf.Sin(angle);
                    direction=new Vector2(direction.x*c-direction.y*s,direction.x*s+direction.y*c).normalized;
                    Vector2 next=p+direction*(length/12);Stamp(p,next,width*Mathf.Lerp(1,.5f,step/11f),0);
                    if(depth>0 && (step==3||step==7))
                    {
                        float side=random.Next(2)==0?-1:1;
                        Grow(next,(direction+new Vector2(-direction.y,direction.x)*side*.9f).normalized,length*.44f,width*.5f,depth-1);
                    }
                    p=next;
                }
            }
            for(int i=0;i<36;i++)
            {
                int edge=i%4;float t=Next(.04f,.96f);
                Vector2 start=edge==0?new Vector2(-.015f,t):edge==1?new Vector2(1.015f,t):edge==2?new Vector2(t,-.015f):new Vector2(t,1.015f);
                Grow(start,(new Vector2(.5f,.5f)-start).normalized,Next(.16f,.34f),Next(.0018f,.004f),2);
            }
            for(int i=0;i<38;i++)
            {
                float angle=Next(0,Mathf.PI*2);var p=Vector2.one*.5f+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*.48f;
                float radius=Next(.003f,.018f);Stamp(p,p+Vector2.down*Next(.001f,.012f),radius,1);
                if(i%4==0)Stamp(p,p+Vector2.down*Next(.025f,.08f),radius*.28f,1);
            }
            s_Texture=new Texture2D(size,size,TextureFormat.RGBA32,false,true){name="Original injury / vessels and blood beads",hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            s_Texture.SetPixels32(pixels);s_Texture.Apply(false,true);return s_Texture;
        }
        internal static void Release()
        {
            s_Users=Mathf.Max(0,s_Users-1);if(s_Users!=0||!s_Texture)return;
            if(Application.isPlaying)Object.Destroy(s_Texture);else Object.DestroyImmediate(s_Texture);s_Texture=null;
        }
    }
}
