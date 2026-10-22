using UnityEngine;

namespace Unity.MP_FPS
{
    // Original low-cost meshes, shared by client avatars. No model imports or colliders.
    internal static class EnergyShieldGeometry
    {
        private static Mesh s_Sphere,s_Collar,s_Box;
        private static int s_Users;
        internal static Mesh Sphere => s_Sphere;
        internal static Mesh Collar => s_Collar;
        internal static Mesh Box => s_Box;
        internal static void Retain()
        {
            if(s_Users++>0 && s_Sphere)return;
            s_Sphere=SphereMesh(48,24);s_Collar=TorusMesh(48,8);s_Box=BoxMesh();
        }
        internal static void Release()
        {
            s_Users=Mathf.Max(0,s_Users-1);if(s_Users!=0)return;
            Destroy(s_Sphere);Destroy(s_Collar);Destroy(s_Box);s_Sphere=s_Collar=s_Box=null;
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
