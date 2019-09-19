using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace E2EEServer
{
    /// <summary>Main server code.</summary>
    public class ServerMain
    {
        public IPAddress ip = IPAddress.Parse("127.0.0.1");
        public int port = 236;      // The port to host server.
        public bool running = true; // Server running?
        public TcpListener server;  // TCP server.

        public Dictionary<string, UserInfo> users = new Dictionary<string, UserInfo>();
        public Dictionary<string, RoomInfo> rooms = new Dictionary<string, RoomInfo>();
        public Dictionary<Tuple<string, string>, DirectMessageConvo> dms = new Dictionary<Tuple<string, string>, DirectMessageConvo>();

        // Password is "instant"...
        public X509Certificate2 certificate = new X509Certificate2("server.pfx", "instant");

        /// <summary>The main method.</summary>
        public static void Main (string[] args) 
        {
            ServerMain p = new ServerMain();
            p.Invoke();

            Console.WriteLine("\nPress RETURN to continue...");
            Console.ReadLine();
        }

        /// <summary>Start the server.</summary>
        public void Invoke()
        {
            Console.Title = "e2ee-im Server";
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.WriteLine("-- -- -- -- e2ee-im Server V2 -- -- -- --");
            Console.ResetColor();

            // Create and start server.
            server = new TcpListener(ip, port);
            server.Start();
            Console.WriteLine("Server started...");

            // Create default room.
            rooms.Add("Global room", new RoomInfo("Global room", 0, 10));

            // Listen to incoming connections.
            try
            {
                while (running)
                {
                    Console.WriteLine("Waiting for connections...");

                    // This blocks the thread until a request comes in.
                    TcpClient tcpClient = server.AcceptTcpClient();
                    Console.ForegroundColor = ConsoleColor.Green;
                    IPEndPoint clientEndpoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
                    string clientIP = clientEndpoint.Address.ToString();
                    Console.WriteLine($"Client connection from [{clientIP}:{clientEndpoint.Port.ToString()}]");
                    Console.ResetColor();

                    Client client = new Client(this, tcpClient);
                }
            }
            catch (SocketException e)
            {
                Console.WriteLine($"SocketException: {e}");
            }
            finally
            {
                server.Stop();
            }
        }
    }
}
