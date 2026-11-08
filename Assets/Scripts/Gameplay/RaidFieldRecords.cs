using System;
using System.Collections.Generic;
using UnityEngine;
using static Unity.MP_FPS.Client.ResearchQuestCopy;

namespace Unity.MP_FPS
{
    // Read-only copies of station logs. The quest evidence and loot inventories have their own authority.
    internal sealed class RaidFieldRecords : IDisposable
    {
        internal const int MaxTablets = 6;
        private const float PreferredSpacing = 28f;

        internal readonly struct Tablet
        {
            internal readonly Renderer Screen;
            internal readonly int Record;
            internal Tablet(Renderer screen, int record) { Screen = screen; Record = record; }
            internal string Title => s_Records[Record].Title;
            internal string Source => s_Records[Record].Source;
            internal string Body => s_Records[Record].Body;
        }

        private readonly struct Entry
        {
            private readonly string m_TitleZh, m_TitleEn, m_SourceZh, m_SourceEn, m_BodyZh, m_BodyEn;
            internal Entry(string titleZh, string titleEn, string sourceZh, string sourceEn, string bodyZh, string bodyEn)
            {
                m_TitleZh = titleZh; m_TitleEn = titleEn; m_SourceZh = sourceZh; m_SourceEn = sourceEn;
                m_BodyZh = bodyZh; m_BodyEn = bodyEn;
            }
            internal string Title => Text(m_TitleZh, m_TitleEn);
            internal string Source => Text(m_SourceZh, m_SourceEn);
            internal string Body => Text(m_BodyZh, m_BodyEn);
        }

        private static readonly Entry[] s_Records =
        {
            new("留给夜班", "For the night shift", "生活区交班条 / 事故前三天", "Crew handover / Three days before the incident",
                "通风泵又响了一夜。我把两袋热饮留在值班桌下，蓝签那袋没加糖。今晚轮到观察室的人休息，除非项目部再把换班划掉。",
                "The vent pump rattled all night again. I left two hot drink pouches under the duty desk. The blue one has no sugar. The observation crew is due a night off, unless Project crosses out the shift change again."),
            new("复测申请", "Request to retest", "Sera Nadir 博士 / 事故前一天", "Dr. Sera Nadir / One day before the incident",
                "换过探头，响应仍和场脉冲对得上。今天连没进过实验舱的对照样本也有反应。我把复测结果附在暂停申请上，等明早的回执。",
                "We replaced the probe. The response still lines up with the field pulses. Today even the control specimen that never entered the chamber responded. I attached the repeat measurements to the suspension request. The reply is due in the morning."),
            new("第三张腕带", "The third wristband", "医务区手记 / 事故当晚", "Infirmary notes / Night of the incident",
                "送进来的第三个人还戴着研究员腕带。问名字时他没有回答，只在隔离柜开始共振时抬起头。前两个人也在同一刻动了。",
                "The third admission was still wearing a research wristband. He didn't answer when I asked his name. He raised his head when the cabinet began to resonate. The first two moved at the same instant."),
            new("弹药领用单", "Ammunition ledger", "站内安保 / 事故当晚", "Station security / Night of the incident",
                "前两组射击都没能让受污染的人停下。有人从观察室拖出一台场控制器。它响了一下，走廊静了几秒。安保把领用单寄给了实验室，枪和那台机器用的是两套编号。",
                "Neither volley stopped the corrupted crew. Someone dragged a field controller out of observation. It pulsed once and the corridor went quiet for a few seconds. Security sent the ledger to the lab. The guns and the controller use different asset numbers."),
            new("通道请求", "Passage request", "Asterion 安保值守 / 事故当晚", "Asterion security desk / Night of the incident",
                "门禁突然切到总部权限。我报了隔离区里面的人数，对面却先问样本柜有没有掉线。门外的指示灯一直是绿的。里面的人还是出不来。",
                "Access control switched to headquarters without warning. I reported the headcount inside containment. They asked first whether the sample cabinet was still online. The light outside stayed green. Nobody inside could get out."),
            new("没发出去的名单", "The unsent roster", "通讯值班记录 / 事故当晚", "Comms duty log / Night of the incident",
                "人员名单排在遥测包后面。我把它提到队首，系统下一秒又按优先级重排。备用平板里留了一份。要是有人进站，先找人。",
                "The personnel roster was queued behind telemetry. I moved it to the top. The system sorted it back a second later. I kept a copy on a spare tablet. If anyone reaches the station, look for people first."),
            new("货签副本", "Copy of a cargo label", "转运交接 / 事故前一天", "Transfer handover / One day before the incident",
                "昨天交接的那批隔离件，今早在货单上改成了校准工装。封条编号一个也没换。交接人叫我按新版抄，旧版已经从共享盘撤下。",
                "The sealed units we signed over yesterday became calibration tooling on this morning's manifest. Every seal number stayed the same. The clerk told me to use the new list. The old one was already off the shared drive."),
            new("窗边的地球", "Earth at the window", "个人草稿 / 事故前四天", "Personal draft / Four days before the incident",
                "今天终于等到地球露出半边。给家里录的片子没录完，楼下通知又要延长试验。我把文件存成明天再发。",
                "Half of Earth finally came into view today. I didn't finish the video for home. Downstairs announced another extension to the test. I saved the file under Send Tomorrow.")
        };

        private readonly List<Tablet> m_Tablets = new(MaxTablets);
        private readonly List<GameObject> m_Models = new(MaxTablets);
        private MoonRaidMap m_Map;
        private ulong m_Session;
        private int m_Round;
        internal IReadOnlyList<Tablet> Tablets => m_Tablets;

        internal bool Present(MoonRaidMap map, ulong session, int round)
        {
            if (map == m_Map && session == m_Session && round == m_Round) return false;
            Dispose();
            if (!map || session == 0 || !map.LootVisual(0)) return true;
            m_Map = map; m_Session = session; m_Round = round;
            var random = new System.Random(unchecked((int)(session ^ (session >> 32)) + round * 397));
            var sites = ChooseSites(map.LootPositions, random);
            var records = ShuffledIndices(s_Records.Length, random);
            for (int i = 0; i < sites.Count; i++)
            {
                var tray = map.LootVisual(sites[i]);
                if (!tray) continue;
                var model = PhysicalItemVisuals.Create(ResearchQuestRules.Tablet, map.transform);
                if (!model) break;
                m_Models.Add(model);
                model.name = "Station field record " + (records[i] + 1).ToString("00");
                var pose = model.transform;
                pose.SetPositionAndRotation(tray.TransformPoint(new Vector3(.03f, RaidSupplyCacheVisuals.TopTrayHeight + .001f, 0)),
                    tray.rotation * Quaternion.Euler(0, random.Next(-24, 25), 0) * pose.localRotation);
                var screen = model.GetComponentInChildren<MeshRenderer>();
                if (!screen) { model.SetActive(false); continue; }
                var up = tray.up;
                var bounds = screen.localBounds;
                var mesh = screen.transform;
                var half = bounds.extents;
                float extent = Mathf.Abs(Vector3.Dot(up, mesh.TransformVector(Vector3.right * half.x))) +
                    Mathf.Abs(Vector3.Dot(up, mesh.TransformVector(Vector3.up * half.y))) +
                    Mathf.Abs(Vector3.Dot(up, mesh.TransformVector(Vector3.forward * half.z)));
                float bottom = Vector3.Dot(up, mesh.TransformPoint(bounds.center) - pose.position) - extent;
                pose.position -= up * bottom;
                m_Tablets.Add(new Tablet(screen, records[i]));
            }
            return true;
        }

        internal static List<int> ChooseSites(Vector3[] positions, System.Random random)
        {
            int count = Mathf.Min(MaxTablets, positions.Length);
            var selected = new List<int>(count);
            var shuffled = ShuffledIndices(positions.Length, random);
            foreach (int index in shuffled)
            {
                bool spaced = true;
                foreach (int previous in selected)
                    if ((positions[previous] - positions[index]).sqrMagnitude < PreferredSpacing * PreferredSpacing)
                    { spaced = false; break; }
                if (!spaced) continue;
                selected.Add(index);
                if (selected.Count == count) return selected;
            }
            // Compact layouts can still present records even when no two crates are 28 m apart.
            foreach (int index in shuffled)
            {
                if (!selected.Contains(index)) selected.Add(index);
                if (selected.Count == count) break;
            }
            return selected;
        }

        private static int[] ShuffledIndices(int count, System.Random random)
        {
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            for (int i = count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                (order[i], order[other]) = (order[other], order[i]);
            }
            return order;
        }

        public void Dispose()
        {
            foreach (var model in m_Models)
            {
                if (!model) continue;
                model.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(model);
                else UnityEngine.Object.DestroyImmediate(model);
            }
            m_Models.Clear(); m_Tablets.Clear();
            m_Map = null; m_Session = 0; m_Round = 0;
        }
    }
}
