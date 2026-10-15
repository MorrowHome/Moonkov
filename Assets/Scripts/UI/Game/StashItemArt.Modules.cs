using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class StashItemArt
    {
        private void Module(HaloModule module)
        {
            uint color=module==HaloModule.Capacitor || module==HaloModule.FanArray ? 0xA57320u :
                module==HaloModule.PulseController || module==HaloModule.HitMarker ? 0x785DA3u : 0x267E83u;
            Vector2 Point(float radius,float angle)
            { float a=angle*Mathf.Deg2Rad;return new Vector2(50+radius*Mathf.Cos(a),50+radius*Mathf.Sin(a)); }
            void Arc(float radius,float from,float span)
            {
                m_Painter.strokeColor=C(color);m_Painter.lineWidth=3*m_Scale;m_Painter.BeginPath();
                m_Painter.Arc(P(50,50),radius*m_Scale,Angle.Degrees(from),Angle.Degrees(from+span));m_Painter.Stroke();
            }
            switch(module)
            {
                case HaloModule.Capacitor:
                    for(int i=0;i<6;i++) Arc(32,i*60+5,48);
                    Arc(23,0,360); break;
                case HaloModule.ChargeBridge:
                    for(int i=0;i<6;i++) {Arc(34,i*60-9,18);Line(color,4,Point(17,i*60),Point(34,i*60));} break;
                case HaloModule.FocusLens:
                    for(int i=0;i<4;i++) {Arc(25,45+i*90-25,50);Line(color,3,Point(37,45+i*90-12),Point(25,45+i*90),Point(37,45+i*90+12));} break;
                case HaloModule.FanArray:
                    for(int i=0;i<6;i++) {Arc(37,i*60-18,36);Line(color,3,Point(37,i*60-18),Point(20,i*60),Point(37,i*60+18));} break;
                case HaloModule.PulseController:
                    Arc(28,25,310);
                    for(int i=0;i<3;i++) {var p=Point(29,220+i*50);Line(color,4,p+new Vector2(0,-7),p+new Vector2(7,0),p+new Vector2(0,7),p+new Vector2(-7,0),p+new Vector2(0,-7));} break;
                case HaloModule.HitMarker:
                    Arc(33,30,300);Line(color,4,new Vector2(50,30),new Vector2(70,50),new Vector2(50,70),new Vector2(30,50),new Vector2(50,30));break;
            }
        }
    }
}
