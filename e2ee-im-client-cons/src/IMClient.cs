using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace E2EEClientCommandLine
{
    public class IMClient
    {
        private Thread m_TcpThread;
        private bool m_Connected = false;
        private bool m_Connecting = false;
        private bool m_LoggedIn = false;
        private string m_Username = string.Empty;
        private bool m_WaitingForResponse = false;
        private bool m_ResponseSuccess = false;

        public TcpClient client;     // The TCP client.
        public NetworkStream stream; // Data stream.
        public SslStream ssl;        // For connection encryption.
        public BinaryReader br;      // Read data.
        public BinaryWriter bw;      // For writing.

        // Events
        public event EventHandler OnLoginOK, OnRoomLeave, OnDirectMessageJoinOK;
        public event IMErrorEventHandler OnLoginFailed,
            OnConnectionFail, OnDisconnected, OnRoomJoinFail, OnDirectMessageJoinFail;
        public event IMReceivedEventHandler OnMessageReceived;
        public event IMRoomReceivedEventHandler OnRoomMessageReceived;
        public event IMRoomJoinedEventHandler OnRoomJoined;
        public event IMRoomAnnounceReceivedEventHandler OnRoomAnnounceReceived;
        public event IMDMMsgsRetrievedEventHandler OnDMMsgsRetrieved;

        public string Server { get => "localhost"; }
        public int Port { get => 236; }
        public bool LoggedIn { get => m_LoggedIn; }
        public string Username { get => m_Username; }
        public bool Connected { get => m_Connected; }
        public bool Connecting { get => m_Connecting; }
        public string CurrentRoomName { get; private set; }

        /// This contains all shared decryption keys for direct messages.
        /// The key of the dictionary is the username of the other person.
        private Dictionary<string, byte[]> dmKeyring = new Dictionary<string, byte[]>();
        private BigInteger diffhel_sec = 0; // Diffie-Hellman private variable. ('a' if user A, 'b' if user B)

        /// <summary>Setup connection and login</summary>
        private void SetupConnection ()
        {
            m_Connected = false;
            m_Connecting = true;
            // Set up the client.
            try
            {
                client = new TcpClient(Server, Port);
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nSocketException: { e.Message }");
                Console.ResetColor();

                OnConnectionFail(this, new IMErrorEventArgs(IMError.ERR_SERVER_UNAVAILABLE));
                m_Connecting = false;
                return;
            }
            stream = client.GetStream();

            // Setup SSL. When server is authenticating, client has to confirm
            // certificate.  SSLStream checks this certificate and passes
            // result to the callback method.
            ssl = new SslStream(client.GetStream(), false,
                new RemoteCertificateValidationCallback(ValidateCertificate));

            // Authenticate.
            ssl.AuthenticateAsClient("im-server");

            // Setup I/O streams.
            br = new BinaryReader(ssl, Encoding.UTF8);
            bw = new BinaryWriter(ssl, Encoding.UTF8);

            // Client and server greeting.
            UInt32 response = br.ReadUInt32();
            if (response == (UInt32)IM_PacketBytes.HELLO)
            {
                // Hello is good.
            }
            bw.Write((UInt32)IM_PacketBytes.HELLO);
            bw.Flush();

            m_Connected = true;
            m_Connecting = false;

            // While not logged in basically.
            m_WaitingForResponse = true;
            while (true)
            {
                // Wait for username input.
                while (m_WaitingForResponse);

                // Login
                bw.Write((UInt32)(IM_PacketBytes.LOGIN));
                bw.Write(Username);
                bw.Flush();
                response = br.ReadUInt32();

                // Check for success
                if (response == (UInt32)IM_PacketBytes.OK)
                {
                    // Success
                    OnLoginOK(this, new EventArgs());
                    m_WaitingForResponse = false;
                    break;
                }

                // Failed
                IMErrorEventArgs err = new IMErrorEventArgs((IMError)response);
                OnLoginFailed(this, err);

                // Flag to wait for new username attempt.
                m_WaitingForResponse = true;
            }
            // Logged in, run main loop.
            Receiver();

            CloseConnection();
        }

        // Receive incoming packets.
        private void Receiver ()
        {
            m_LoggedIn = true;

            try
            {
                // Message loop
                while (client.Connected)
                {
                    UInt32 type = br.ReadUInt32();

                    switch (type)
                    {
                        // Received a message.
                        case ((UInt32)IM_PacketBytes.RECEIVED):
                        {
                            string from = br.ReadString();
                            string to = br.ReadString();
                            string iv = br.ReadString(); // Read the iv.
                            string encryptedMsg = br.ReadString();
                            string decrypted;

                            // Decrypt the message
                            string keyIndex = from == Username ? to : from; // If it's from ourself, index with other person's name.
                            byte[] encryptionKey;
                            if (dmKeyring.TryGetValue(keyIndex, out encryptionKey))
                            {
                                decrypted = CryptographicMethods.DecryptStringFromBase64_AES(encryptedMsg, encryptionKey, Convert.FromBase64String(iv));
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"Could not decrypt message using key from '{from}'; A shared secret symmetric encryption key was not found in your keyring.");
                                Console.ResetColor();
                                break;
                            }

                            OnMessageReceived(this, new IMReceivedEventArgs(from, decrypted));
                        } break;

                        // Received a room message.
                        case ((UInt32)IM_PacketBytes.ROOM_MSG_RECEIVED):
                        {
                            string roomname = br.ReadString();
                            string from = br.ReadString();
                            string msg = br.ReadString();
                            OnRoomMessageReceived(this, new IMRoomReceivedEventArgs(roomname, from, msg));
                        } break;

                        // Retrieving messages from a DM.
                        case ((UInt32)IM_PacketBytes.DIRM_RETRIEVE):
                        {
                            string otherUser = br.ReadString();
                            UInt32 msgCount = br.ReadUInt32();
                            var messages = new List<Tuple<string, string>>((int)msgCount);

                            // Read messages into author-message pair buffer.
                            for (int i = 0; i < msgCount; ++i)
                            {
                                string a = br.ReadString();
                                string m = br.ReadString();
                                messages.Add(new Tuple<string, string>(a, m));
                            }
                            OnDMMsgsRetrieved(this, new IMDMMsgsRetrievedEventArgs(otherUser, messages));

                        } break;

                        case ((UInt32)IM_PacketBytes.DIRM_JOIN_OK):
                        {
                            OnDirectMessageJoinOK(this, new EventArgs());
                        } break;

                        // Received room announcement.
                        case ((UInt32)IM_PacketBytes.ROOM_ANNMSG_RECEIVED):
                        {
                            string roomname = br.ReadString();
                            string msg = br.ReadString();
                            OnRoomAnnounceReceived(this, new IMRoomAnnounceReceivedEventArgs(roomname, msg));
                        } break;

                        // Joined a room
                        case ((UInt32)IM_PacketBytes.ROOM_JOINED):
                        {
                            string roomname = br.ReadString();
                            CurrentRoomName = roomname;
                            OnRoomJoined(this, new IMRoomJoinedEventArgs(roomname));
                        } break;

                        // User left the room.
                        case ((UInt32)IM_PacketBytes.ROOM_LEAVE):
                        {
                            CurrentRoomName = string.Empty;
                            OnRoomLeave(this, new EventArgs());
                        } break;

                        // Diffie-Hellman key exchange part 1. We send AG from here
                        case ((UInt32)IM_PacketBytes.DIFHEL_0):
                        {
                            // User A's secret integer.
                            byte[] buffer = new byte[DifHelConstants.N_SIZE_BITS / 8];
                            using (var rng = new RNGCryptoServiceProvider())
                            {
                                rng.GetNonZeroBytes(buffer);
                            }
                            // Divide by 2 to make sure it's smaller than n. Couldn't
                            // get rng to work in a range...
                            diffhel_sec = BigInteger.Divide(new BigInteger(buffer), 2);
                            if (diffhel_sec < 0) { diffhel_sec = -diffhel_sec; }
                            //diffhel_sec = 4;

                            // Calculated g^a mod n
                            BigInteger ag = BigInteger.ModPow(DifHelConstants.g,  diffhel_sec, DifHelConstants.n);

                            //Console.WriteLine($"a: {diffhel_sec}, ag:{ag}");

                            bw.Write((UInt32)IM_PacketBytes.DIFHEL_1);
                            var bytes = ag.ToByteArray();
                            bw.Write((UInt16)bytes.Length);
                            bw.Write(bytes);
                            bw.Flush();
                        } break;

                        // Also Diffie-Hellman key exchange part 1. We send BG from here
                        case ((UInt32)IM_PacketBytes.DIFHEL_0_B):
                        {
                            // Start next packet
                            bw.Write((UInt32)IM_PacketBytes.DIFHEL_1_B);
                            bw.Write(br.ReadString()); // Pass username pair back to server.
                            bw.Write(br.ReadString()); // ^^

                            // User B's secret integer.
                            byte[] buffer = new byte[DifHelConstants.N_SIZE_BITS / 8];
                            using (var rng = new RNGCryptoServiceProvider())
                            {
                                rng.GetNonZeroBytes(buffer);
                            }
                            // Divide by 2 to make sure it's smaller than n. Couldn't
                            // get rng to work in a range...
                            diffhel_sec = BigInteger.Divide(new BigInteger(buffer), 2);
                            if (diffhel_sec < 0) { diffhel_sec = -diffhel_sec; }
                            //diffhel_sec = 3;

                            // Calculated g^b mod n
                            BigInteger bg = BigInteger.ModPow(DifHelConstants.g, diffhel_sec, DifHelConstants.n);

                            //Console.WriteLine($"b: {diffhel_sec}, bg:{bg}");

                            var bytes = bg.ToByteArray();
                            bw.Write((UInt16)bytes.Length);
                            bw.Write(bytes);
                            bw.Flush();
                        } break;

                        // Final Diffie-Hellman step; generate the key, god damnit.
                        case ((UInt32)IM_PacketBytes.DIFHEL_2):
                        {
                            string otherUser = br.ReadString();
                            bool alice    = br.ReadBoolean();
                            UInt16 agSize = br.ReadUInt16();
                            BigInteger ag = new BigInteger(br.ReadBytes(agSize));
                            UInt16 bgSize = br.ReadUInt16();
                            BigInteger bg = new BigInteger(br.ReadBytes(bgSize));
                            BigInteger sec = 1; // The shared secret.

                            // Alice generates secret with:
                            // abg = bg^a mod n
                            //
                            // Bob   generates secret with:
                            // abg = ag^b mod n
                            //
                            sec = BigInteger.ModPow(alice ? bg : ag, diffhel_sec, DifHelConstants.n);

                            //Console.WriteLine($"{Username}'s secret is {sec} !");

                            // The secrets are the same between user1 and user2.
                            // Now we use a KDF to generate the key.
                            byte[] derivedKey = CryptographicMethods.DeriveKeyFromSecret(sec.ToString());
                            dmKeyring.Add(otherUser, derivedKey);

                        } break;

                        // User doesn't exist
                        case ((UInt32)IM_PacketBytes.ERR_USER_NEXIST):
                        {
                            OnDirectMessageJoinFail(this, new IMErrorEventArgs(IMError.ERR_USER_NEXIST));
                        } break;

                        // Room doesn't exist; couldn't join.
                        case ((UInt32)IM_PacketBytes.ERR_ROOM_NEXIST):
                        {
                            OnRoomJoinFail(this, new IMErrorEventArgs(IMError.ERR_ROOM_NEXIST));
                        } break;

                        // Room is full
                        case ((UInt32)IM_PacketBytes.ERR_ROOM_FULL):
                        {
                            OnRoomJoinFail(this, new IMErrorEventArgs(IMError.ERR_ROOM_FULL));
                        } break;
                    }
                }
            }
            catch (IOException e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nIOException {e.Message}");
                Console.ResetColor();
            }

            m_LoggedIn = false;
        }

        /// <summary>Send a secure, encrypted, direct message.</summary>
        public void SendMessageToDM (string user, string msg)
        {
            string encrypted = string.Empty;
            byte[] encryptionKey;
            string iv;
            if (dmKeyring.TryGetValue(user, out encryptionKey))
            {
                using (Aes aes = Aes.Create())
                {
                    iv = Convert.ToBase64String(aes.IV);
                    encrypted = CryptographicMethods.EncryptStringToBase64_AES(msg, encryptionKey, aes.IV);
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Could not send message to user '{user}'; A shared secret encryption key was not found in your keyring.");
                Console.ResetColor();
                return;
            }

            // Check if encryption worked.
            if (encrypted == string.Empty)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error encrypting message...");
                Console.ResetColor();
                return;
            }

            bw.Write((UInt32)IM_PacketBytes.DIRM_SEND);
            bw.Write(iv);
            bw.Write(encrypted);
            bw.Flush();
        }

        /// <summary>Send message to a room.</summary>
        public void SendMessageToRoom(string room, string msg)
        {
            bw.Write((UInt32)IM_PacketBytes.ROOM_MSG_SEND);
            bw.Write(room);
            bw.Write(msg);
            bw.Flush();
        }

        /// <summary>Join the specified room.</summary>
        public void JoinRoom (string roomname)
        {
            bw.Write((UInt32)IM_PacketBytes.ROOM_JOIN);
            bw.Write(roomname);
            bw.Flush();
        }

        /// <summary>Leave the current room.</summary>
        public void LeaveCurrentRoom()
        {
            bw.Write((UInt32)IM_PacketBytes.ROOM_LEAVE);
            bw.Write(CurrentRoomName);
            bw.Flush();
        }

        /// <summary>Request to start a direct message session with user.</summary>
        public void BeginDM (string user)
        {
            bw.Write((UInt32)IM_PacketBytes.DIRM_BEGIN);
            bw.Write(user);
            bw.Flush();
        }

        /// <summary>Close the connection.</summary>
        private void CloseConnection ()
        {
            if (!m_Connected) { return; }

            // Cleanup
            if (br != null) { br.Close(); }
            if (bw != null) { bw.Close(); }
            if (ssl != null) { ssl.Close(); }
            if (stream != null) { stream.Close(); }
            if (client != null) { client.Close(); }

            OnDisconnected(this, new IMErrorEventArgs(IMError.ERR_CLOSED));
        }
        
        /// <summary>Disconnect from the server.</summary>
        public void Disconnect () => CloseConnection();

        public void Connect()
        {
            if (m_Connected) { return; }

            m_Connecting = true;

            // Connect and communicate to server in a new thread.
            m_TcpThread = new Thread(new ThreadStart(SetupConnection));
            m_TcpThread.Start();
        }

        public void Login(string username)
        {
            m_Username = username;
            m_WaitingForResponse = false;
        }

        /// <summary>Validate the certificate. We will allow untrusted certificates.</summary>
        public static bool ValidateCertificate (object sender, X509Certificate cert,
            X509Chain chain, SslPolicyErrors sslPolicyErr) => true;
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
