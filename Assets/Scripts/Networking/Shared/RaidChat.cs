// 聊天的消息格式、接收范围和限频规则都在这里。服务器筛选频道听众，客户端状态按序号记录可展示的历史和当前发送反馈。
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS {
    public enum RaidChatChannel : byte {
        Team,
        Global
    }

    public enum RaidChatResult : byte {
        Ready,
        Accepted,
        Unavailable,
        NoTeam,
        Empty,
        TooLong,
        InvalidText,
        RateLimited,
        Expired
    }

    // 玩家提交当前聊天会话的编号、文本和频道。名字、存活、队伍身份由服务器按连接填写。
    public struct RaidChatRequestRpc : IRpcCommand {
        public ulong Session;
        public uint RequestId;
        public RaidChatChannel Channel;
        public FixedString512Bytes Text;
    }

    // 服务端为消息分配排序序号，同时带来源请求号，接收方可以显示昵称和频道并挡掉重发。
    public struct RaidChatMessageRpc : IRpcCommand {
        public ulong Session;
        public uint Sequence, RequestId;
        public int SenderId;
        public RaidChatChannel Channel;
        public bool Dead;
        public FixedString64Bytes Name;
        public FixedString512Bytes Text;
    }

    // RequestId 为 0 时通知聊天可用性，也涵盖新一局和队伍权限变化。
    public struct RaidChatResultRpc : IRpcCommand {
        public ulong Session;
        public uint AnnouncementSequence;
        public uint RequestId;
        public bool TeamAvailable;
        public RaidChatResult Result;
    }

    public static class RaidChatRules {
        public const int MaxCharacters = 120, HistoryLimit = 100;

        public static RaidChatResult Normalize(string source, out string text) {
            text = "";
            if (source == null) return RaidChatResult.Empty;
            var builder = new StringBuilder(source.Length);
            int count = 0;
            for (int i = 0; i < source.Length; i++) {
                char c = source[i];
                // 先去控制字符和换行分隔符。UTF-16 的代理对检验完整才按一个字符计，避免拆掉表情后发出乱码。
                if (char.IsControl(c) || c == '\u2028' || c == '\u2029') continue;
                if (char.IsSurrogate(c)) {
                    if (!char.IsHighSurrogate(c) || i + 1 >= source.Length || !char.IsLowSurrogate(source[i + 1]))
                        return RaidChatResult.InvalidText;
                    builder.Append(c).Append(source[++i]);
                } else { builder.Append(c); }
                if (++count > MaxCharacters) return RaidChatResult.TooLong;
            }
            text = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(text) ? RaidChatResult.Empty : RaidChatResult.Accepted;
        }

        // 全场频道所有当局成员可见。队伍频道同时比较队伍号和比赛分组，避免同号队伍跨实例看见。
        public static bool Receives(
            RaidChatChannel channel,
            int senderTeam,
            string senderGroup,
            int receiverTeam,
            string receiverGroup) {
            return channel == RaidChatChannel.Global || (channel == RaidChatChannel.Team && senderTeam >= 0 &&
                                                         senderTeam == receiverTeam && senderGroup == receiverGroup);
        }
    }

    public sealed class RaidChatBudget {
        private double m_Tokens = 3, m_At;
        public uint LastRequest { get; private set; }

        // 一条连接只认越来越新的请求号。更早的则丢弃，避免断线或重复按键导致再次发同样文本。
        public bool Begin(uint request) {
            if (request == 0 || request <= LastRequest) return false;
            LastRequest = request;
            return true;
        }

        public bool Spend(double now) {
            // 随现实经过时间恢复发送额度并限制积攒上限。连续发几条后要等可用额度重新增加。
            m_Tokens = Math.Min(3, m_Tokens + Math.Max(0, now - m_At));
            m_At = now;
            if (m_Tokens < 1) return false;
            m_Tokens--;
            return true;
        }
    }

    public sealed class RaidChatClientState : IComponentData {
        public ulong Session;
        public bool TeamAvailable;
        public uint NextRequest, PendingRequest, LastSequence, AnnouncementSequence;
        public int Revision;
        public double PendingAt;
        public string Draft = "";
        public RaidChatChannel Channel;
        public RaidChatResult Feedback = RaidChatResult.Ready;
        public readonly List<RaidChatMessageRpc> Messages = new(RaidChatRules.HistoryLimit);

        // 离局或换聊天会话时清频道权限、内容、草稿和编号。界面通过修订号刷新。
        public void Reset(ulong session = 0) {
            Session = session;
            AnnouncementSequence = 0;
            TeamAvailable = false;
            NextRequest = PendingRequest = LastSequence = 0;
            Draft = "";
            Channel = RaidChatChannel.Team;
            Feedback = RaidChatResult.Ready;
            Messages.Clear();
            Revision++;
        }

        public void Receive(RaidChatResultRpc result) {
            // 零号回执只是告知频道权限与当前会话。普通回执要对应正在等待的发送，成功才清草稿。
            if (result.RequestId == 0) {
                if (result.AnnouncementSequence < AnnouncementSequence) return;
                if (result.Session != Session) Reset(result.Session);
                AnnouncementSequence = result.AnnouncementSequence;
                TeamAvailable = result.TeamAvailable;
                Revision++;
                return;
            }
            if (result.Session != Session || result.RequestId != PendingRequest) return;
            PendingRequest = 0;
            Feedback = result.Result;
            if (result.Result == RaidChatResult.Accepted) Draft = "";
            Revision++;
        }

        public void Receive(RaidChatMessageRpc message) {
            // 旧会话和重复序号的消息不进列表。达到上限踢掉最早一条，仍保留最新消息。
            if (message.Session != Session || Session == 0 || message.Sequence <= LastSequence) return;
            LastSequence = message.Sequence;
            if (Messages.Count == RaidChatRules.HistoryLimit) Messages.RemoveAt(0);
            Messages.Add(message);
            Revision++;
        }
    }
}