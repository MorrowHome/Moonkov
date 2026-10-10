using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Shared deterministic fracture shapes for cosmetic scree and collidable outcrops.</summary>
    internal sealed class MoonRockMesh
    {
        private readonly Vector3[][] shapes = new Vector3[16][];
        private readonly List<int> faces = new();
        private const int Sides = 8;
        public MoonRockMesh(int seed)
        {
            var random = new System.Random(seed);
            for (int s=0;s<shapes.Length;s++)
            {
                var vertices = new Vector3[26];
                for (int ring=0;ring<3;ring++)
                    for (int side=0;side<Sides;side++)
                    {
                        float angle=side*Mathf.PI*2/Sides;
                        float radius=(ring==1 ? 1 : .63f)*(float)(.8+random.NextDouble()*.35);
                        vertices[ring*Sides+side]=new Vector3(Mathf.Cos(angle)*radius+(ring-1)*.12f,
                            (ring-1)*.62f+(float)(random.NextDouble()-.5)*.16f,Mathf.Sin(angle)*radius);
                    }
                vertices[24]=new Vector3(-.05f,-.78f,.1f);
                vertices[25]=new Vector3(.16f,.75f,-.06f);
                shapes[s]=vertices;
            }
            for (int side=0;side<Sides;side++)
            {
                int next=(side+1)%Sides;
                AddFace(24,side,next);AddFace(25,16+next,16+side);
                for (int ring=0;ring<2;ring++)
                {
                    int a=ring*Sides+side,b=ring*Sides+next,c=(ring+1)*Sides+side,d=(ring+1)*Sides+next;
                    AddFace(a,c,b);AddFace(b,c,d);
                }
            }
        }

        private void AddFace(int a,int b,int c)
        {
            // Keep all variants outward-facing regardless of ring construction order.
            Vector3[] v=shapes[0];
            if (Vector3.Dot(Vector3.Cross(v[b]-v[a],v[c]-v[a]),v[a]+v[b]+v[c])<0) (b,c)=(c,b);
            faces.Add(a);faces.Add(b);faces.Add(c);
        }

        public void Append(System.Random random,Vector3 position,Vector3 normal,float diameter,float freshness,
            List<Vector3> vertices,List<Vector2> uv,List<Color> colors,List<int> triangles)
        {
            Vector3[] shape=shapes[random.Next(shapes.Length)];
            Quaternion rotation=Quaternion.FromToRotation(Vector3.up,normal)
                *Quaternion.Euler(0,(float)random.NextDouble()*360,0);
            Vector3 scale=new(diameter*.5f,diameter*(float)(.28+random.NextDouble()*.23),
                diameter*(float)(.38+random.NextDouble()*.22));
            float tone=(float)(.65+random.NextDouble()*.5);
            // Per-vertex dust follows the buried base, not a single plane shared by the chunk.
            foreach (int index in faces)
            {
                Vector3 point=shape[index];
                triangles.Add(vertices.Count);
                vertices.Add(position+rotation*Vector3.Scale(point,scale));
                uv.Add(new Vector2(point.x,point.z));
                colors.Add(new Color(tone,freshness,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.5f,.1f,point.y)),1));
            }
        }
    }
}
