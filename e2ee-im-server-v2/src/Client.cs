using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace E2EEServer
{
    public class Client
    {
        public ServerMain main;
        public UserInfo info;

        public TcpClient client;     // The TCP client.
        public NetworkStream stream; // Data stream.
        public SslStream ssl;        // For connection encryption.
        public BinaryReader br;      // Read data.
        public BinaryWriter bw;      // For writing.

        public Client(ServerMain main, TcpClient client)
        {
            this.main = main;
            this.client = client;

            Thread thread = new Thread(SetupConnection);
            thread.Start();
        }

        // Receive incoming packets.
        private void Receiver()
        {
            info.loggedIn = true;

            try
            {
                // Main message loop
                while(client.Connected)
                {
                    UInt32 type = br.ReadUInt32();

                    switch(type)
                    {
                        // Sending a message
                        case ((UInt32)IM_PacketBytes.DIRM_SEND):
                        {
                            string to = info.currentDMOther; //br.ReadString();

                            string iv = br.ReadString();
                            string msg = br.ReadString();

                            Console.WriteLine($"Msg: {info.username} -> {to}: {msg}");

                            // Get the conversation.
                            var tup = new Tuple<string, string>(info.username, to);
                            DirectMessageConvo convo;
                            bool exists = main.dms.TryGetValue(tup, out convo);
                            if (!exists)
                            {
                                // Try tuple other way around
                                tup = new Tuple<string, string>(to, info.username);
                                exists = main.dms.TryGetValue(tup, out convo);
                            }
                            if (exists)
                            {
                                convo.MessageSend(info.username, to, iv, msg);
                            }
                        } break;

                        // Join a room
                        case ((UInt32)IM_PacketBytes.ROOM_JOIN):
                        {
                            string roomname = br.ReadString();

                            RoomInfo room;
                            if (main.rooms.TryGetValue(roomname, out room))
                            {
                                if (room.members.Count < room.MaxUsers)
                                {
                                    info.currentRoom = room;
                                    room.members.Add(info.username, info);

                                    bw.Write((UInt32)IM_PacketBytes.ROOM_JOINED);
                                    bw.Write(room.Name);
                                    bw.Flush();

                                    room.MessageToAll($"{info.username} joined the room.");

                                    Console.WriteLine($"{info.username} is joining '{room.Name}'");
                                }
                                else
                                {
                                    bw.Write((UInt32)IM_PacketBytes.ERR_ROOM_FULL);
                                    bw.Flush();

                                    Console.WriteLine($"{info.username} tried to join full room '{room.Name}'");
                                }
                            }
                            else
                            {
                                bw.Write((UInt32)IM_PacketBytes.ERR_ROOM_NEXIST);
                                bw.Flush();
                            }
                        } break;

                        // Leave a room
                        case ((UInt32)IM_PacketBytes.ROOM_LEAVE):
                        {
                            string roomname = br.ReadString();

                            RoomInfo room;
                            if (main.rooms.TryGetValue(roomname, out room))
                            {
                                // Remove from member list.
                                if (room.members.ContainsKey(info.username))
                                {
                                    room.members.Remove(info.username);
                                }

                                // No current room.
                                info.currentRoom = null;

                                bw.Write((UInt32)IM_PacketBytes.ROOM_LEAVE);
                                bw.Write(room.Name);
                                bw.Flush();

                                room.MessageToAll($"{info.username} left the room.");

                                Console.WriteLine($"{info.username} left room '{room.Name}'");
                            }
                            else
                            {
                                bw.Write((UInt32)IM_PacketBytes.ERR_ROOM_NEXIST);
                                bw.Flush();
                            }
                        } break;

                        // Sending a message to a room
                        case ((UInt32)IM_PacketBytes.ROOM_MSG_SEND):
                        {
                            string roomname = br.ReadString();
                            string msg = br.ReadString();

                            RoomInfo room;
                            if (main.rooms.TryGetValue(roomname, out room))
                            {
                                if (!room.members.ContainsKey(info.username))
                                {
                                    // Not in this room...
                                    bw.Write((UInt32)IM_PacketBytes.ERR_ROOM_NOTMEMBER);
                                    bw.Flush();

                                    Console.WriteLine($"Attempt by {info.username} to write into a room they are not part ('{room.Name}') of with: {msg}");

                                    // Exit switch block
                                    break;
                                }

                                Console.WriteLine($"Msg: [Room '{room.Name}'] {info.username} : {msg}");

                                // Iterate through room members and send them the message.
                                foreach(UserInfo u in room.members.Values)
                                {
                                    // Send to all users in room including the writer.
                                    u.connection.bw.Write((UInt32)IM_PacketBytes.ROOM_MSG_RECEIVED);
                                    u.connection.bw.Write(room.Name);
                                    u.connection.bw.Write(info.username);
                                    u.connection.bw.Write(msg);
                                    u.connection.bw.Flush();
                                }
                            }
                        } break;

                        // Join/Create a direct message conversation with another user.
                        case ((UInt32)IM_PacketBytes.DIRM_BEGIN):
                        {
                            // The users in the DM.
                            string user1 = info.username;
                            string user2 = br.ReadString();

                            // Check that the other username exists
                            UserInfo infoUser2;
                            if (main.users.TryGetValue(user2, out infoUser2))
                            {
                                // Check if a convo with these users already exists.
                                var tup = new Tuple<string, string>(user1, user2);
                                DirectMessageConvo convo;
                                bool exists = main.dms.TryGetValue(tup, out convo);
                                if (!exists)
                                {
                                    // Try tuple other way around
                                    tup = new Tuple<string, string>(user2, user1);
                                    exists = main.dms.TryGetValue(tup, out convo);
                                }
                                if (exists)
                                {
                                    Console.WriteLine($"Conversation between users '{user1}' & '{user2}' already exists.");

                                    // Respond with all messages of DM. (10 is maximum.)
                                    int msgCount = convo.Messages.Count;
                                    var messages = convo.Messages;

                                    // Successfuly joined.
                                    bw.Write((UInt32)IM_PacketBytes.DIRM_JOIN_OK);

                                    bw.Write((UInt32)IM_PacketBytes.DIRM_RETRIEVE);
                                    bw.Write(user2); // Name of user that DM is with.

                                    // Maybe compress these messages? Probably quite bad if there's alot of messages...

                                    // This will indicate how many messages to read.
                                    bw.Write((UInt32)msgCount);
                                    for (int i = 0; i < msgCount; ++i)
                                    {
                                        bw.Write(messages[i].Item1); // Author
                                        bw.Write(messages[i].Item2); // Initialisation vector for encryption.
                                        bw.Write(messages[i].Item3); // Message.
                                    }
                                    bw.Flush();
                                }
                                else
                                {
                                    Console.WriteLine($"Generating conversation between users '{user1}' & '{user2}'.");

                                    {
                                        // Begin Diffie-Hellman key exchange

                                        // Create conversation
                                        convo = new DirectMessageConvo(info, infoUser2);
                                        main.dms.Add(tup, convo);

                                        // Tell User1 to generate AG.
                                        bw.Write((UInt32)IM_PacketBytes.DIFHEL_0);
                                        bw.Flush();

                                        // Get user1's AG and send it to the conversation.
                                        BigInteger ag;
                                        UInt32 response = br.ReadUInt32();
                                        if (response == (UInt32)IM_PacketBytes.DIFHEL_1)
                                        {
                                            UInt16 agSize = br.ReadUInt16();
                                            ag = new BigInteger(br.ReadBytes(agSize));
                                            //Console.WriteLine($"Read User1's Diffie-Hellman packet! AG:{ag}");
                                            convo.DifHel_SetAG(ag);

                                            // Tell User2 to calculate the BG.
                                            infoUser2.connection.bw.Write((UInt32)IM_PacketBytes.DIFHEL_0_B);
                                            infoUser2.connection.bw.Write(tup.Item1); // Send info about the DM.
                                            infoUser2.connection.bw.Write(tup.Item2);
                                            infoUser2.connection.bw.Flush();
                                        }
                                    }

                                    // Join success.
                                    bw.Write((UInt32)IM_PacketBytes.DIRM_JOIN_OK);

                                    // Send the retreive message even though there's nothing to retrieve.
                                    // This will update UI on user1's screen.
                                    bw.Write((UInt32)IM_PacketBytes.DIRM_RETRIEVE);
                                    bw.Write(user2);     // Name of user that DM is with.
                                    bw.Write((UInt32)0); // Zero messages

                                    bw.Flush();
                                }

                                info.currentDMOther = user2;
                            }
                            else
                            {
                                // User doesn't exist; cannot create Direct Message.
                                bw.Write((UInt32)IM_PacketBytes.ERR_USER_NEXIST);
                                bw.Flush();
                            }
                        } break;

                        case ((UInt32)IM_PacketBytes.DIFHEL_1_B):
                        {
                            string userA = br.ReadString();
                            string userB = br.ReadString();
                            UInt16 bgSize = br.ReadUInt16();
                            BigInteger bg = new BigInteger(br.ReadBytes(bgSize));

                            var tup = new Tuple<string, string>(userA, userB);
                            DirectMessageConvo convo;
                            if (main.dms.TryGetValue(tup, out convo))
                            {
                                //Console.WriteLine($"Read User2's Diffie-Hellman packet! BG:{bg}");
                                convo.DifHel_SetBG(bg);

                                convo.DifHel_Exchange();
                            }
                        } break;
                    }
                }
            }
            catch (IOException e)
            {
                Console.WriteLine($"IOException {e.Message}");
            }

            info.loggedIn = false;
        }

        /// <summary>Setup connection and login</summary>
        private void SetupConnection ()
        {
            stream = client.GetStream();

            // For encrypted connection.
            ssl = new SslStream(client.GetStream(), false);
            ssl.AuthenticateAsServer(main.certificate, false, SslProtocols.Tls, true);

            // Setup I/O streams.
            br = new BinaryReader(ssl, Encoding.UTF8);
            bw = new BinaryWriter(ssl, Encoding.UTF8);

            // Client and server greeting.
            bw.Write((UInt32)IM_PacketBytes.HELLO);
            bw.Flush(); // Clear buffer and send data to client.

            UInt32 read = br.ReadUInt32();
            if (read == (UInt32)IM_PacketBytes.HELLO)
            {
                // Hello is good.
            }

            // Logging in.
            while (true)
            {
                UInt32 msg;
                try
                {
                    msg = br.ReadUInt32();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Exception caught: {e.Message}");

                    CloseConnection();
                    return;
                }
                string username = br.ReadString();

                // If username is already being used.
                if (main.users.ContainsKey(username))
                {
                    bw.Write((UInt32)IM_PacketBytes.ERR_EXIST);
                    bw.Flush();

                    // Try again
                    continue;
                }

                // Check if username is valid
                if (username.Length > 14 || username.Length < 3)
                {
                    bw.Write((UInt32)IM_PacketBytes.ERR_LONG_USERNAME);
                    bw.Flush();

                    // Try again.
                    continue;
                }

                // Logging in
                if (msg == (UInt32)IM_PacketBytes.LOGIN)
                {
                    // Login success
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"{username} has logged in.");
                    Console.ResetColor();

                    // Create userinfo.
                    info = new UserInfo(username, this);
                    main.users.Add(username, info);

                    // Assosciate connection.
                    bw.Write((UInt32)IM_PacketBytes.OK);
                    bw.Flush();

                    // Success, break out of loop.
                    break;
                }
            }

            Receiver();

            CloseConnection();
        }

        /// <summary>Close the connection.</summary>
        private void CloseConnection ()
        {
            // Remove user.
            if (main != null && info != null && main.users.ContainsKey(info.username))
            {
                // Remove from rooms
                if (info.currentRoom != null)
                {
                    if (info.currentRoom.members.ContainsKey(info.username))
                    {
                        info.currentRoom.members.Remove(info.username);
                    }
                    info.currentRoom.MessageToAll($"{info.username} lost connection.");
                    info.currentRoom = null;
                }

                main.users.Remove(info.username);

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{info.username} has disconnected.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"An user (not logged in) has disconnected.");
                Console.ResetColor();
            }

            // Cleanup
            br.Close();
            bw.Close();
            ssl.Close();
            stream.Close();
            client.Close();
        }
    }

    // Used to identify each packet.
    public enum IM_PacketBytes : UInt32
    {
        // General
        OK = 0,                 // Everything good.
        LOGIN = 1,              // Login.
        ERR_LONG_USERNAME = 2,  // Username too long.
        ERR_EXIST = 3,          // Username exists.
        RECEIVED = 5,           // Message receieved.

        // Room stuff
        ROOM_JOIN = 6,          // Join a room.
        ROOM_CREATE = 7,        // Create a room.
        ROOM_LEAVE = 8,         // Leave a room.
        ROOM_LIST = 9,          // List the rooms.
        ROOM_JOINED = 10,       // Joined the room.
        ERR_ROOM_NEXIST = 11,   // Room doesn't exist.
        ERR_ROOM_FULL = 12,     // Room is full.

        ROOM_MSG_SEND = 13,     // Send a message to the room.
        ROOM_MSG_RECEIVED = 14, // Received message from room.
        ROOM_ANNMSG_RECEIVED = 15, // Received announcement message from room.

        ERR_ROOM_NOTMEMBER = 16,   // Not a member of the room.

        // Used for connection to server.
        HELLO = 65536,

        // Direct messaging.
        DIRM_BEGIN = 17,    // Join or create the DM with another user.
        DIRM_DELETE = 18,   // Delete the conversation.
        DIRM_RETRIEVE = 19, // Retrieve all messages from a DM.
        DIRM_JOIN_OK = 20,  // Successfully joined.
        DIRM_SEND = 4,      // Send direct message.

        ERR_USER_NEXIST = 21, // User doesn't exist.

        // Diffie-Hellman key exchange stuff:
        DIFHEL_0   = 65000, // First step ; Alice receives n & g,  and  constructs ag.
        DIFHEL_0_B = 65001, // First step ; Bob receives   n & g,  and  constructs bg.
        DIFHEL_1   = 65002, // Second step; Alice's ag is read into  the convo object.
        DIFHEL_1_B = 65003, // Second step; Bob's   bg is read into  the convo object.
        DIFHEL_2   = 65004, // Final step ; bg/ag are sent to Bob/Alice  respectively; they generate same symmetric key.
    };
}
