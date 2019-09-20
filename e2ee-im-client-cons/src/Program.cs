using System;
using System.Collections.Generic;
using System.Threading;

namespace E2EEClientCommandLine
{
    public class Program
    {
        // Create main object.
        public static void Main (string[] args) => new Program().Invoke();

        private bool loggedIn = false, waitingForResponse = false, responseOK = false;
        private IMClient client;
        private string username = string.Empty;
        private string roomName = string.Empty;
        private bool inRoom = false, viewingDM = false;
        private string dmOtherUser = string.Empty;

        public static readonly string HEADER = "mikejzx's end-to-end encrypted messaging client.";

        // Invoke the client.
        public void Invoke()
        {
            ClearConsole(string.Empty);

            SetupAndConnect();

            // Block until connected.
            while (client.Connecting);
            if (!client.Connected)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nNot connected.");
                Console.ResetColor();
                Console.WriteLine("Press any key to continue...");
                Console.ReadLine();
                return;
            }

            Login();

            // Main menu.
        displayMainMenu:
            ClearConsole(" -- -- -- MAIN MENU -- -- --");
            Console.WriteLine("  - Type 'direct' for direct messaging menu.");
            Console.WriteLine("  - Or type 'room' for room menu.");
            while (true)
            {
                string read = Console.ReadLine();
                if (read.ToLower() == "room")
                {
                    // Take user too room selection.
                    if (Rooms())
                    {
                        WriteMessagesToRoom();
                        inRoom = false;
                    }

                    Console.WriteLine(string.Empty);
                    goto displayMainMenu;
                }
                else if (read.ToLower() == "direct")
                {
                    if(DirectMessaging())
                    {
                        WriteMessagesToDM();
                        viewingDM = false;
                    }

                    goto displayMainMenu;
                }
                else if (read.ToLower() == "exit" || read.ToLower() == "quit")
                {
                    break;
                }
                // Neither room nor direct.
                Console.WriteLine("Invalid response.");
            }

            client.Disconnect();

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        private void WriteMessagesToDM ()
        {
            while (viewingDM)
            {
                string msg = Console.ReadLine();
                if (msg.Length < 1) { continue; }
                if (msg == "/leave") { break; }
                Console.SetCursorPosition(msg.Length, Console.CursorTop - 1);
                while (Console.CursorLeft != 0)
                {
                    // Write backspaces to remove the message line.
                    Console.Write("\b \b");
                }

                try
                {
                    client.SendMessageToDM(dmOtherUser, msg);
                }
                catch (Exception e)
                {
                    ErrorLog("There was an error sending your message: " + e.Message);
                }
            }
        }

        private void WriteMessagesToRoom()
        {
            // No way to leave room currently...
            while(inRoom)
            {
                string msg = Console.ReadLine();
                if (msg.Length < 1) { continue; }
                if (msg == "/leave") { break; }
                Console.SetCursorPosition(msg.Length, Console.CursorTop - 1);
                while (Console.CursorLeft != 0)
                {
                    // Write backspaces to remove the message line.
                    Console.Write("\b \b");
                }

                client.SendMessageToRoom(roomName, msg);
            }

            client.LeaveCurrentRoom();
        }

        private bool DirectMessaging()
        {
            ClearConsole(" -- -- -- DIRECT MESSAGES -- -- -- ");
            Console.WriteLine("  - Enter the name of the user you wish to start a secure direct messaging session with.");
            Console.WriteLine("  - Type 'back' to exit.");

            while (true)
            {
                string u = Console.ReadLine();
                if (u.Length < 1)
                {
                    continue;
                }

                // Go back
                if (u.ToLower() == "back")
                {
                    return false;
                }

                // Join/Create the DM.
                if (!client.BeginDM(u))
                {
                    // Tried to start DM with themselves.
                    ErrorLog("Please enter a nickname other than your own.");
                    continue;
                }
                waitingForResponse = true;
                responseOK = false;

                // Wait for response.
                while (waitingForResponse);
                if (responseOK)
                {
                    // Successfully accepted.
                    return true;
                }
            }
        }

        private bool Rooms()
        {
            ClearConsole(" -- -- -- ROOMS MENU -- -- --");
            Console.WriteLine("  - Type 'list' to list rooms, 'join' to join one.");
            Console.WriteLine("  - Or just type 'global' to join the default room.");
            Console.WriteLine("  - (Type 'back' to go back.)");

            while (true)
            {
                string room = Console.ReadLine();
                if (room.Length < 1)
                {
                    continue;
                }

                // Join default room.
                if (room.ToLower() == "global")
                {
                    client.JoinRoom("Global room");
                    waitingForResponse = true;
                    responseOK = false;

                    // Wait for response.
                    while (waitingForResponse);
                    if (responseOK)
                    {
                        // Successful room connection..
                        return true;
                    }
                }
                else if (room.ToLower() == "back")
                {
                    return false;
                }
                else
                {
                    Console.WriteLine("Unknown command.");
                }
            }
        }

        // Setup the client and connect to server.
        private void SetupAndConnect()
        {
            client = new IMClient();
            client.OnLoginOK += (object sender, EventArgs args) => {
                Thread.Sleep(50);
                waitingForResponse = false;
                responseOK = true;
            };
            client.OnLoginFailed += (object sender, IMErrorEventArgs args) => {
                Thread.Sleep(50);
                waitingForResponse = false;
                responseOK = false;

                // Print error.
                ErrorLog($"Login Error { args.Error }");
            };
            client.OnDisconnected += (object sender, IMErrorEventArgs args) => {
                ErrorLog($"Disconnected: { args.Error }");
            };
            client.OnConnectionFail += (object sender, IMErrorEventArgs args) => {
                ErrorLog($"Connection failed: { args.Error }");
            };

            client.OnRoomJoined += (object sender, IMRoomJoinedEventArgs args) => {
                Thread.Sleep(50);
                waitingForResponse = false;
                responseOK = true;
                inRoom = true;
                Console.WriteLine($"Joined room '{args.RoomName}'.");
                roomName = args.RoomName;

                // Print a header.
                ClearConsole($" -- -- -- ROOM: {roomName} -- -- --");
            };
            client.OnRoomJoinFail += (object sender, IMErrorEventArgs args) => {
                Thread.Sleep(50);
                inRoom = false;
                roomName = string.Empty;

                waitingForResponse = false;
                responseOK = false;

                ErrorLog($"Unable to join room: '{ args.Error }'");
            };
            client.OnRoomMessageReceived += (object sender, IMRoomReceivedEventArgs args) => {
                PrintMsg(args.From, args.Message);
            };
            client.OnRoomAnnounceReceived += (object sender, IMRoomAnnounceReceivedEventArgs args) => {
                PrintMsg("ROOM", args.Message, ConsoleColor.Red);
            };
            client.OnRoomLeave += (object sender, EventArgs args) => {
                Console.WriteLine($"Left the room '{roomName}'.");
                inRoom = false;
                roomName = string.Empty;
            };

            // Callbacks for direct messages.
            client.OnDirectMessageJoinFail += (object sender, IMErrorEventArgs args) => {
                waitingForResponse = false;
                responseOK = false;

                if (args.Error == IMError.ERR_USER_NEXIST)
                {
                    ErrorLog("Could not join/create direct message convo; user does not exist.");
                }
                else
                {
                    ErrorLog("Error joining/creating direct message convo: " + args.Error);
                }
            };
            client.OnDirectMessageJoinOK += (object sender, EventArgs args) => {
                waitingForResponse = false;
                responseOK = true;
                viewingDM = true;
            };
            client.OnDMMsgsRetrieved += (object sender, IMDMMsgsRetrievedEventArgs args) => {
                viewingDM = true;
                dmOtherUser = args.OtherUser;

                // Print a header.
                ClearConsole($" -- -- -- DIRECT MESSAGES: {dmOtherUser} -- -- --");

                // Print out all the messages.
                var mes = args.Messages;
                for (int i = 0; i < mes.Count; ++i)
                {
                    PrintMsg(mes[i].Item1, mes[i].Item2);
                }
            };
            // Receive message
            client.OnMessageReceived += (object sender, IMReceivedEventArgs args) => {
                // Print it out if viewing the DM.
                if (viewingDM && (args.From == dmOtherUser || args.From == username))
                {
                    PrintMsg(args.From, args.Message);
                }
                else
                {
                    // Cache old cursor value
                    int cursorTopOld = Console.CursorTop;
                    int cursorLefOld = Console.CursorLeft;

                    // Set cursor to second row.
                    Console.CursorTop = 1;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write($"\rLogged in as '{username}' ");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"(you have [{0}] new direct msgs.)");
                    Console.ResetColor();
                    Console.CursorTop = cursorTopOld;
                    Console.CursorLeft = cursorLefOld;
                }
            };

            Console.Write("Connecting");

            // Show dots when connecting...
            Thread dotThread = new Thread(() => {
                // Wait until connect is called.
                while (!client.Connecting) ;

                // Do this while connecting...
                int i = 0;
                while (client.Connecting)
                {
                    Thread.Sleep(250);
                    Console.Write(".");
                    ++i;
                    if (i > 3)
                    {
                        // Write 3 backspaces.
                        Console.Write("\b \b\b \b\b \b\b \b");
                        i = 0;
                    }
                }
            });
            dotThread.Start();

            // Connect
            client.Connect();

            dotThread.Join();

            Console.WriteLine("\rConnection established.   ");
        }

        private void Login()
        {
            // Keep looping until and break on success.
            while (true)
            {
                // Ask for nickname
                Console.WriteLine("Enter a nickname:");
                username = Console.ReadLine();

                // Setup client.
                client.Login(username);

                waitingForResponse = true;
                responseOK = false;

                // Wait until server responds.
                while (waitingForResponse);
                if (responseOK)
                {
                    // Successful log in.
                    break;
                }
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Logged in as { username } !");
            Console.ResetColor();
        }

        private void PrintMsg(string author, string msg, ConsoleColor userCol = ConsoleColor.Yellow)
        {
            if (author != username)
            {
                Console.ForegroundColor = userCol;
                Console.Write($" {author}: ");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write(" Me: ");
            }
            Console.ResetColor();
            Console.WriteLine(msg);
        }

        private void ErrorLog(string err)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(err);
            Console.ResetColor();
        }
        private void ClearConsole(string header)
        {
            Console.Clear();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(HEADER);
            if (username != string.Empty)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Logged in as '{username}'");
            }
            Console.WriteLine(string.Empty);

            if (header != string.Empty)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(header);
            }

            Console.ResetColor();
        }

        public static void PrintFormatted (string str)
        {
            if (!str.Contains("%$"))
            {
                ConsoleColor[] cols = new ConsoleColor[] {
                    ConsoleColor.Red, ConsoleColor.DarkRed,
                    ConsoleColor.Cyan, ConsoleColor.Magenta,
                    ConsoleColor.Blue, ConsoleColor.Yellow,
                    ConsoleColor.Green, ConsoleColor.White,
                    ConsoleColor.DarkMagenta
                };
                char[] chars = str.ToCharArray();
                int last = 0;
                for (int i = 0; i < chars.Length - 2; ++i)
                {
                    if (chars[i] == '%' && chars[i + 1] == '$')
                    {
                        // Parse char
                        int idx = chars[i + 3] - '0';
                        if (idx < 0 || idx > 9)
                        {
                            // Not a colour
                            continue;
                        }
                        if (idx != 9)
                        {
                            // Set colour
                            Console.ForegroundColor = cols[idx];
                        }
                        else
                        {
                            // 9 resets colour.
                            Console.ResetColor();
                        }
                        Console.Write(str.Substring(last, str.Length - i));

                        last = i;
                    }
                }
                Console.ResetColor();
                Console.WriteLine(string.Empty);
            }
            else
            {
                Console.ResetColor();
                Console.WriteLine(str);
            }
        }
    }
}
