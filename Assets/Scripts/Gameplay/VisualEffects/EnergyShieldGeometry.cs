using UnityEngine;

namespace Unity.MP_FPS
{
    // Original low-cost meshes, shared by client avatars. No model imports or colliders.
    internal static class EnergyShieldGeometry
    {
        private static Mesh s_Housing,s_Aperture,s_Sphere,s_Collar,s_Box,s_NeckBand,s_BowLoop,s_BowTail,s_CatEar,s_Visor,s_EnergyEar;
        private static int s_Users;
        internal static Mesh Aperture => s_Aperture;
        internal static Mesh Housing => s_Housing;
        internal static Mesh Sphere => s_Sphere;
        internal static Mesh Collar => s_Collar;
        internal static Mesh Box => s_Box;
        internal static Mesh NeckBand => s_NeckBand;
        internal static Mesh BowLoop => s_BowLoop;
        internal static Mesh BowTail => s_BowTail;
        internal static Mesh CatEar => s_CatEar;
        internal static Mesh Visor => s_Visor;
        internal static Mesh EnergyEar => s_EnergyEar;
        internal static void Retain()
        {
            if(s_Users++>0 && s_Sphere)return;
            s_Housing=HousingMesh();s_Aperture=ApertureMesh();s_Sphere=SphereMesh(48,24);s_Collar=TorusMesh(48,8);s_Box=BoxMesh();
            s_NeckBand=NeckBandMesh();s_BowLoop=BowLoopMesh();s_BowTail=BowTailMesh();
            s_Visor=VisorMesh();s_EnergyEar=EnergyEarMesh();
            s_CatEar=RibbonMesh("Neko bell / cat ear",new[]{new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(0,1,.12f)},new Vector2[3],new[]{0,1,2});
        }
        internal static void Release()
        {
            s_Users=Mathf.Max(0,s_Users-1);if(s_Users!=0)return;
            Destroy(s_Housing);s_Housing=null;Destroy(s_Aperture);s_Aperture=null;Destroy(s_Sphere);Destroy(s_Collar);Destroy(s_Box);s_Sphere=s_Collar=s_Box=null;
            Destroy(s_NeckBand);Destroy(s_BowLoop);Destroy(s_BowTail);s_NeckBand=s_BowLoop=s_BowTail=null;
            Destroy(s_CatEar);s_CatEar=null;
            Destroy(s_Visor);s_Visor=null;
            Destroy(s_EnergyEar);s_EnergyEar=null;
        }
        internal static float HelmetProfile(float v)=>Mathf.Sqrt(Mathf.Max(0,1-Mathf.Pow(v*2-1,2)));
        internal static Vector3 VisorPoint(float u,float v)
        {
            float angle=u*Mathf.PI*2;
            float radius=Mathf.Lerp(.17f,HelmetProfile(v),Mathf.SmoothStep(0,1,v/.12f));
            return new Vector3(Mathf.Sin(angle)*radius,v,Mathf.Cos(angle)*radius);
        }
        private static Mesh ApertureMesh()
        {
            const int count=48;var vertices=new Vector3[count+1];var uv=new Vector2[count+1];var indices=new int[count*3];
            uv[count]=Vector2.one*.5f;
            for(int i=0;i<count;i++)
            {
                float a=i*Mathf.PI*2/count;vertices[i]=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);uv[i]=new Vector2(vertices[i].x,vertices[i].y)*.5f+Vector2.one*.5f;
                indices[i*3]=count;indices[i*3+1]=i;indices[i*3+2]=(i+1)%count;
            }
            return RibbonMesh("Subspace storage / aperture",vertices,uv,indices);
        }
        private static Mesh HousingMesh()
        {
            var outline=new[]{new Vector2(-.34f,-.5f),new Vector2(.34f,-.5f),new Vector2(.5f,-.34f),new Vector2(.5f,.34f),
                new Vector2(.34f,.5f),new Vector2(-.34f,.5f),new Vector2(-.5f,.34f),new Vector2(-.5f,-.34f)};
            var vertices=new Vector3[48];var uv=new Vector2[48];var indices=new System.Collections.Generic.List<int>();
            for(int i=0;i<8;i++)
            {
                var a=outline[i];var b=outline[(i+1)%8];vertices[i]=new Vector3(a.x,a.y,.5f);vertices[i+8]=new Vector3(a.x,a.y,-.5f);
                int side=16+i*4;vertices[side]=vertices[i];vertices[side+1]=new Vector3(b.x,b.y,.5f);vertices[side+2]=new Vector3(b.x,b.y,-.5f);vertices[side+3]=vertices[i+8];
                indices.AddRange(new[]{side,side+2,side+1,side,side+3,side+2});
                if(i<6)indices.AddRange(new[]{0,i+1,i+2,8,i+10,i+9});
            }
            var mesh=new Mesh{name="Shield projector / chamfered housing",hideFlags=HideFlags.DontSave};mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=indices.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static Mesh VisorMesh()
        {
            const int columns=48,rows=24;
            var vertices=new Vector3[(columns+1)*(rows+1)];var uv=new Vector2[vertices.Length];
            var triangles=new int[columns*rows*6];int k=0;
            for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++)
            {
                int i=y*(columns+1)+x;float u=x/(float)columns,v=y/(float)rows;
                vertices[i]=VisorPoint(u,v);uv[i]=new Vector2(u,v);
                if(x==columns||y==rows)continue;
                int b=i+1,c=i+columns+1;
                triangles[k++]=i;triangles[k++]=b;triangles[k++]=c;
                triangles[k++]=b;triangles[k++]=c+1;triangles[k++]=c;
            }
            var mesh=new Mesh{name="Neko space seal / complete rounded canopy",hideFlags=HideFlags.DontSave};
            mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();
            var bounds=mesh.bounds;bounds.Expand(.35f);mesh.bounds=bounds;return mesh;
        }
        private static Mesh EnergyEarMesh()
        {
            const int segments=36,rings=5;
            var vertices=new Vector3[2*(segments*(rings+1)+1)];var uv=new Vector2[vertices.Length];
            var triangles=new System.Collections.Generic.List<int>();
            var corners=new[]{new Vector2(-.5f,0),new Vector2(.5f,0),new Vector2(.06f,1)};
            for(int side=0;side<2;side++)
            {
                int offset=side*(segments*(rings+1)+1),center=offset+segments*(rings+1);
                vertices[center]=new Vector3(.02f,.35f,side==0?.20f:-.14f);uv[center]=new Vector2(.5f,1);
                for(int ring=0;ring<=rings;ring++)for(int s=0;s<segments;s++)
                {
                    int corner=s/12;float t=(s%12)/12f;
                    Vector2 c=corners[corner],a=Vector2.Lerp(c,corners[(corner+2)%3],.14f),b=Vector2.Lerp(c,corners[(corner+1)%3],.14f);
                    Vector2 outline=(1-t)*(1-t)*a+2*(1-t)*t*c+t*t*b;
                    float radius=1-ring/(float)(rings+1);Vector2 p=Vector2.Lerp(new Vector2(.02f,.35f),outline,radius);
                    int i=offset+ring*segments+s;vertices[i]=new Vector3(p.x,p.y,(side==0?.20f:-.14f)*(1-radius*radius));uv[i]=new Vector2(s/(float)segments,1-radius);
                    int next=offset+ring*segments+(s+1)%segments;
                    if(ring<rings){int inner=i+segments,innerNext=next+segments;Add(i,next,inner);Add(next,innerNext,inner);}
                    else Add(i,next,center);
                }
                void Add(int a,int b,int c){if(side==0){triangles.Add(a);triangles.Add(b);triangles.Add(c);}else{triangles.Add(c);triangles.Add(b);triangles.Add(a);}}
            }
            var mesh=new Mesh{name="Neko space seal / rounded luminous ear",hideFlags=HideFlags.DontSave};mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static void Destroy(Object obj)
        {if(!obj)return;if(Application.isPlaying)Object.Destroy(obj);else Object.DestroyImmediate(obj);}
        private static Mesh SphereMesh(int columns,int rows)
        {
            var vertices=new Vector3[(columns+1)*(rows+1)];var uv=new Vector2[vertices.Length];
            var triangles=new int[columns*rows*6];int index=0;
            for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++)
            {
                float v=y/(float)rows,u=x/(float)columns,latitude=v*Mathf.PI,longitude=u*Mathf.PI*2;
                int i=y*(columns+1)+x;vertices[i]=new Vector3(Mathf.Sin(latitude)*Mathf.Cos(longitude),-Mathf.Cos(latitude),Mathf.Sin(latitude)*Mathf.Sin(longitude));uv[i]=new Vector2(u,v);
                if(x==columns || y==rows)continue;
                int a=i,b=i+1,c=i+columns+1,d=c+1;
                triangles[index++]=a;triangles[index++]=c;triangles[index++]=b;
                triangles[index++]=b;triangles[index++]=c;triangles[index++]=d;
            }
            var mesh=new Mesh{name="Original energy field / shared sphere",hideFlags=HideFlags.DontSave};
            mesh.vertices=vertices;mesh.normals=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateBounds();return mesh;
        }
        private static Mesh TorusMesh(int columns,int sides)
        {
            var vertices=new Vector3[(columns+1)*(sides+1)];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];
            var triangles=new int[columns*sides*6];int index=0;
            for(int x=0;x<=columns;x++)for(int y=0;y<=sides;y++)
            {
                float angle=x/(float)columns*Mathf.PI*2,section=y/(float)sides*Mathf.PI*2;
                Vector3 radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));int i=x*(sides+1)+y;
                normals[i]=radial*Mathf.Cos(section)+Vector3.up*Mathf.Sin(section);
                vertices[i]=radial*(1+.115f*Mathf.Cos(section))+Vector3.up*(.115f*Mathf.Sin(section));uv[i]=new Vector2(x/(float)columns,y/(float)sides);
                if(x==columns || y==sides)continue;int a=i,b=i+1,c=i+sides+1,d=c+1;
                triangles[index++]=a;triangles[index++]=b;triangles[index++]=c;triangles[index++]=b;triangles[index++]=d;triangles[index++]=c;
            }
            var mesh=new Mesh{name="Original seal collar / shared torus",hideFlags=HideFlags.DontSave};mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateBounds();return mesh;
        }
        private static Mesh NeckBandMesh()
        {
            const int segments=64;
            // Closed, bevelled cross-section. The inner wall is the fitted neck ellipse.
            var profile=new[]{new Vector2(1,-.5f),new Vector2(1.045f,-.5f),new Vector2(1.085f,-.42f),
                new Vector2(1.085f,.42f),new Vector2(1.045f,.5f),new Vector2(1,.5f)};
            var vertices=new Vector3[(segments+1)*profile.Length];var uv=new Vector2[vertices.Length];
            var triangles=new int[segments*profile.Length*6];int k=0;
            for(int x=0;x<=segments;x++)for(int p=0;p<profile.Length;p++)
            {
                float a=x*Mathf.PI*2/segments;int i=x*profile.Length+p;
                vertices[i]=new Vector3(Mathf.Sin(a)*profile[p].x,profile[p].y,Mathf.Cos(a)*profile[p].x);
                uv[i]=new Vector2(x/(float)segments,p/(float)profile.Length);
                if(x==segments)continue;int next=x*profile.Length+(p+1)%profile.Length,j=i+profile.Length;
                triangles[k++]=i;triangles[k++]=j;triangles[k++]=next;
                triangles[k++]=next;triangles[k++]=j;triangles[k++]=next+profile.Length;
            }
            var mesh=new Mesh{name="Neko collar / bevelled neck band",hideFlags=HideFlags.DontSave};
            mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static Mesh BowLoopMesh()
        {
            const int columns=12,rows=6;
            var vertices=new Vector3[(columns+1)*(rows+1)];var uv=new Vector2[vertices.Length];
            var triangles=new int[columns*rows*6];int k=0;
            for(int x=0;x<=columns;x++)for(int y=0;y<=rows;y++)
            {
                float t=x/(float)columns,u=y/(float)rows*2-1;int i=x*(rows+1)+y;
                float height=.12f+.43f*Mathf.Sin(t*Mathf.PI*.72f);
                vertices[i]=new Vector3(t,u*height,.28f*Mathf.Sin(t*Mathf.PI)*(1-u*u)-.07f*Mathf.Abs(u));
                uv[i]=new Vector2(t,(u+1)*.5f);
                if(x==columns||y==rows)continue;int b=i+1,c=i+rows+1;
                triangles[k++]=i;triangles[k++]=c;triangles[k++]=b;
                triangles[k++]=b;triangles[k++]=c;triangles[k++]=c+1;
            }
            return RibbonMesh("Neko collar / folded satin bow",vertices,uv,triangles);
        }
        private static Mesh BowTailMesh()
        {
            var vertices=new[]{new Vector3(-.25f,0,0),new Vector3(.25f,0,0),new Vector3(.5f,-1,.12f),
                new Vector3(0,-.8f,.18f),new Vector3(-.35f,-1,.12f)};
            return RibbonMesh("Neko collar / notched ribbon tail",vertices,new Vector2[5],new[]{0,1,3,1,2,3,0,3,4});
        }
        private static Mesh RibbonMesh(string name,Vector3[] front,Vector2[] frontUV,int[] indices)
        {
            // Duplicate the back surface with opposite normals; folded fabric is visible from both sides.
            int count=front.Length;var vertices=new Vector3[count*2];var uv=new Vector2[count*2];
            System.Array.Copy(front,vertices,count);System.Array.Copy(front,0,vertices,count,count);
            System.Array.Copy(frontUV,uv,count);System.Array.Copy(frontUV,0,uv,count,count);
            var triangles=new int[indices.Length*2];System.Array.Copy(indices,triangles,indices.Length);
            for(int i=0;i<indices.Length;i+=3){triangles[indices.Length+i]=indices[i+2]+count;triangles[indices.Length+i+1]=indices[i+1]+count;triangles[indices.Length+i+2]=indices[i]+count;}
            var mesh=new Mesh{name=name,hideFlags=HideFlags.DontSave};mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static Mesh BoxMesh()
        {
            var vertices=new Vector3[24];var normals=new Vector3[24];var uv=new Vector2[24];var indices=new int[36];
            var faces=new[]{Vector3.forward,Vector3.back,Vector3.right,Vector3.left,Vector3.up,Vector3.down};
            for(int f=0;f<6;f++)
            {
                Vector3 normal=faces[f],right=Vector3.Cross(Mathf.Abs(normal.y)>.5f?Vector3.forward:Vector3.up,normal),up=Vector3.Cross(normal,right);
                for(int v=0;v<4;v++){float x=v==0 || v==3?-.5f:.5f,y=v<2?-.5f:.5f;int i=f*4+v;vertices[i]=normal*.5f+right*x+up*y;normals[i]=normal;uv[i]=new Vector2(x+.5f,y+.5f);}
                int a=f*4,j=f*6;indices[j]=a;indices[j+1]=a+1;indices[j+2]=a+2;indices[j+3]=a;indices[j+4]=a+2;indices[j+5]=a+3;
            }
            var mesh=new Mesh{name="Original gear housing / shared box",hideFlags=HideFlags.DontSave};mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=indices;mesh.RecalculateBounds();return mesh;
        }
    }
}
