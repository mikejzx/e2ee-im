using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace E2EEServer
{
    public class UserInfo
    {
        public string username;
        public bool loggedIn;
        public Client connection;
        public RoomInfo currentRoom;
        public string currentDMOther;

        public UserInfo(string user, Client conn = null)
        {
            this.username = user;

            if (conn != null)
            {
                this.loggedIn = true;
                this.connection = conn;
            }
            else
            {
                this.loggedIn = false;
            }
        }
    }
}
