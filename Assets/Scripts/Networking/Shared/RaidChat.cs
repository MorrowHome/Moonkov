using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    public enum RaidChatChannel : byte { Team, Global }
    public enum RaidChatResult : byte { Ready, Accepted, Unavailable, NoTeam, Empty, TooLong, InvalidText, RateLimited, Expired }
    public struct RaidChatRequestRpc : IRpcCommand
    {
        public ulong Session;
        public uint RequestId;
        public RaidChatChannel Channel;
        public FixedString512Bytes Text;
    }
    public struct RaidChatMessageRpc : IRpcCommand
    {
        public ulong Session;
        public uint Sequence, RequestId;
        public int SenderId;
        public RaidChatChannel Channel;
        public bool Dead;
        public FixedString64Bytes Name;
        public FixedString512Bytes Text;
    }
    // RequestId=0 announces availability, including a new match and team changes.
    public struct RaidChatResultRpc : IRpcCommand
    {
        public ulong Session;
        public uint AnnouncementSequence;
        public uint RequestId;
        public bool TeamAvailable;
        public RaidChatResult Result;
    }
    public static class RaidChatRules
    {
        public const int MaxCharacters = 120, HistoryLimit = 100;
        public static RaidChatResult Normalize(string source, out string text)
        {
            text = "";
            if (source == null) return RaidChatResult.Empty;
            var builder = new StringBuilder(source.Length);
            int count = 0;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (char.IsControl(c) || c == '\u2028' || c == '\u2029') continue;
                if (char.IsSurrogate(c))
                {
                    if (!char.IsHighSurrogate(c) || i + 1 >= source.Length || !char.IsLowSurrogate(source[i + 1])) return RaidChatResult.InvalidText;
                    builder.Append(c).Append(source[++i]);
                }
                else builder.Append(c);
                if (++count > MaxCharacters) return RaidChatResult.TooLong;
            }
            text = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(text) ? RaidChatResult.Empty : RaidChatResult.Accepted;
        }
        public static bool Receives(RaidChatChannel channel, int senderTeam, string senderGroup, int receiverTeam, string receiverGroup)
            => channel == RaidChatChannel.Global || channel == RaidChatChannel.Team && senderTeam >= 0 &&
                senderTeam == receiverTeam && senderGroup == receiverGroup;
    }
    public sealed class RaidChatBudget
    {
        private double m_Tokens = 3, m_At;
        public uint LastRequest { get; private set; }
        public bool Begin(uint request)
        {
            if (request == 0 || request <= LastRequest) return false;
            LastRequest = request; return true;
        }
        public bool Spend(double now)
        {
            m_Tokens = Math.Min(3, m_Tokens + Math.Max(0, now - m_At)); m_At = now;
            if (m_Tokens < 1) return false;
            m_Tokens--; return true;
        }
    }
    public sealed class RaidChatClientState : IComponentData
    {
        public ulong Session;
        public bool TeamAvailable;
        public uint NextRequest, PendingRequest, LastSequence, AnnouncementSequence;
        public int Revision;
        public double PendingAt;
        public string Draft = "";
        public RaidChatChannel Channel;
        public RaidChatResult Feedback = RaidChatResult.Ready;
        public readonly List<RaidChatMessageRpc> Messages = new List<RaidChatMessageRpc>(RaidChatRules.HistoryLimit);
        public void Reset(ulong session = 0)
        {
            Session = session; AnnouncementSequence = 0; TeamAvailable = false; NextRequest = PendingRequest = LastSequence = 0;
            Draft = ""; Channel = RaidChatChannel.Team; Feedback = RaidChatResult.Ready;
            Messages.Clear(); Revision++;
        }
        public void Receive(RaidChatResultRpc result)
        {
            if (result.RequestId == 0)
            {
                if (result.AnnouncementSequence < AnnouncementSequence) return;
                if (result.Session != Session) Reset(result.Session);
                AnnouncementSequence = result.AnnouncementSequence;
                TeamAvailable = result.TeamAvailable; Revision++; return;
            }
            if (result.Session != Session || result.RequestId != PendingRequest) return;
            PendingRequest = 0; Feedback = result.Result;
            if (result.Result == RaidChatResult.Accepted) Draft = "";
            Revision++;
        }
        public void Receive(RaidChatMessageRpc message)
        {
            if (message.Session != Session || Session == 0 || message.Sequence <= LastSequence) return;
            LastSequence = message.Sequence;
            if (Messages.Count == RaidChatRules.HistoryLimit) Messages.RemoveAt(0);
            Messages.Add(message); Revision++;
        }
    }
}
