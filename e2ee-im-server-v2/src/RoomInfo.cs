using System;
using System.Collections.Generic;

/*
    Class for a 'room' where users can chat.
*/

namespace E2EEServer
{
    public class RoomInfo
    {
        private string m_Roomname = string.Empty;
        private int m_Id = 0, m_MaxUsers = 0;

        public Dictionary<string, UserInfo> members = new Dictionary<string, UserInfo>();

        public string Name { get => m_Roomname; }
        public int MaxUsers { get => m_MaxUsers; }

        public RoomInfo (string name, int id, int maxusers)
        {
            this.m_Roomname = name;
            this.m_Id = id;
            this.m_MaxUsers = maxusers;
        }

        /// <summary>Send a message to all in the room from the 'room'.</summary>
        public void MessageToAll (string msg)
        {
            // Iterate through every member and send them the msg.
            foreach (UserInfo u in members.Values)
            {
                u.connection.bw.Write((UInt32)IM_PacketBytes.ROOM_ANNMSG_RECEIVED);
                u.connection.bw.Write(Name);
                u.connection.bw.Write(msg);
                u.connection.bw.Flush();
            }
        }
    }
}
