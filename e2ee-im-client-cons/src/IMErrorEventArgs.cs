using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace E2EEClientCommandLine
{
    public enum IMError : UInt32
    {
        ERR_LONG_USERNAME = IM_PacketBytes.ERR_LONG_USERNAME,
        ERR_EXISTS = IM_PacketBytes.ERR_EXIST,
        ERR_ROOM_NEXIST = IM_PacketBytes.ERR_ROOM_NEXIST,
        ERR_ROOM_FULL = IM_PacketBytes.ERR_ROOM_FULL,
        ERR_ROOM_NOTMEMBER = IM_PacketBytes.ERR_ROOM_NOTMEMBER,
        ERR_USER_NEXIST = IM_PacketBytes.ERR_USER_NEXIST,

        ERR_CLOSED = 65534,
        ERR_SERVER_UNAVAILABLE = 65535,
    };

    public delegate void IMErrorEventHandler (object sender, IMErrorEventArgs e);
    public class IMErrorEventArgs : EventArgs
    {
        public IMError Error { get; private set; }

        public IMErrorEventArgs (IMError error)
        {
            this.Error = error;
        }
    }

    // For receiving messages.
    public delegate void IMReceivedEventHandler (object sender, IMReceivedEventArgs e);
    public class IMReceivedEventArgs : EventArgs
    {
        public string From { get; private set; }
        public string Message { get; private set; }

        public IMReceivedEventArgs (string user, string msg)
        {
            this.From = user;
            this.Message = msg;
        }
    }

    // For joining room
    public delegate void IMRoomJoinedEventHandler (object sender, IMRoomJoinedEventArgs e);
    public class IMRoomJoinedEventArgs : EventArgs
    {
        public string RoomName { get; private set; }

        public IMRoomJoinedEventArgs(string roomname) => this.RoomName = roomname;
    }

    // For receiving room messages.
    public delegate void IMRoomReceivedEventHandler (object sender, IMRoomReceivedEventArgs e);
    public class IMRoomReceivedEventArgs : EventArgs
    {
        public string RoomName { get; private set; }
        public string From { get; private set; }
        public string Message { get; private set; }

        public IMRoomReceivedEventArgs (string roomname, string user, string msg)
        {
            this.RoomName = roomname;
            this.From = user;
            this.Message = msg;
        }
    }

    // For receiving room announcements.
    public delegate void IMRoomAnnounceReceivedEventHandler (object sender, IMRoomAnnounceReceivedEventArgs e);
    public class IMRoomAnnounceReceivedEventArgs : EventArgs
    {
        public string RoomName { get; private set; }
        public string Message { get; private set; }

        public IMRoomAnnounceReceivedEventArgs (string roomname, string msg)
        {
            this.RoomName = roomname;
            this.Message = msg;
        }
    }

    // For DM retrieval.
    public delegate void IMDMMsgsRetrievedEventHandler (object sender, IMDMMsgsRetrievedEventArgs e);
    public class IMDMMsgsRetrievedEventArgs : EventArgs
    {
        public string OtherUser { get; private set; }
        public List<Tuple<string, string>> Messages { get; private set; }

        public IMDMMsgsRetrievedEventArgs (string otherUser, List<Tuple<string, string>> messages)
        {
            this.OtherUser = otherUser;
            this.Messages = messages;
        }
    }
}
