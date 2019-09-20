using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;

namespace E2EEServer
{
    /*
        Basically a room with two users only.
        A user must create a conversation
        with another user by "beginning".
        DM Messages will show 'notifications'.

        This will be the first form of E2EE in the app.
    */

    public class DirectMessageConvo
    {
        private UserInfo user1, user2;

        private List<Tuple<string, string, string>> messages = new List<Tuple<string, string, string>>();

        public List<Tuple<string, string, string>> Messages { get => messages; }

        // Diffie-Hellman parameters.
        private BigInteger AG, BG;

        public DirectMessageConvo(UserInfo user1, UserInfo user2)
        {
            this.user1 = user1;
            this.user2 = user2;
        }

        public void MessageSend(string author, string recipient, string iv, string msg)
        {
            messages.Add(new Tuple<string, string, string>(author, iv, msg));

            // Remove messages until there are only 10.
            while (messages.Count > 10)
            {
                messages.RemoveAt(0);
            }

            // Send back to both users.
            user1.connection.bw.Write((UInt32)IM_PacketBytes.RECEIVED);
            user1.connection.bw.Write(author);
            user1.connection.bw.Write(recipient);
            user1.connection.bw.Write(iv);
            user1.connection.bw.Write(msg);
            user1.connection.bw.Flush();

            user2.connection.bw.Write((UInt32)IM_PacketBytes.RECEIVED);
            user2.connection.bw.Write(author);
            user2.connection.bw.Write(recipient);
            user2.connection.bw.Write(iv);
            user2.connection.bw.Write(msg);
            user2.connection.bw.Flush();
        }

        /// <summary>Set user 1's AG.</summary>
        public void DifHel_SetAG(BigInteger ag)
        {
            AG = ag;
        }

        /// <summary>Set user 2's BG.</summary>
        public void DifHel_SetBG (BigInteger bg)
        {
            BG = bg;
        }

        /// <summary>Begin next phase of Diffie-Hellman. This sends AG and BG to users 1 and 2 
        /// respectively so they can both construct the shared secret.</summary>
        public void DifHel_Exchange()
        {
            var ag = AG.ToByteArray();
            UInt16 agSize = (UInt16)ag.Length;
            var bg = BG.ToByteArray();
            UInt16 bgSize = (UInt16)bg.Length;

            // Send BG over to user 1.
            user1.connection.bw.Write((UInt32)IM_PacketBytes.DIFHEL_2);
            user1.connection.bw.Write(user2.username);
            user1.connection.bw.Write(true); // Alice -> true
            user1.connection.bw.Write(agSize);
            user1.connection.bw.Write(ag);
            user1.connection.bw.Write(bgSize);
            user1.connection.bw.Write(bg);
            user1.connection.bw.Flush();

            // Send AG over to user 2
            user2.connection.bw.Write((UInt32)IM_PacketBytes.DIFHEL_2);
            user2.connection.bw.Write(user1.username);
            user2.connection.bw.Write(false); // Bob -> false.
            user2.connection.bw.Write(agSize);
            user2.connection.bw.Write(ag);
            user2.connection.bw.Write(bgSize);
            user2.connection.bw.Write(bg);
            user2.connection.bw.Flush();
        }
    }
}
