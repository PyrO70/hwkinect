using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Balloons;
using Balloons.Messaging;
using Balloons.Messaging.Model;

namespace Balloons.Server
{
	public class Server
	{
        #region Public interface
        public Server(int port)
        {
            m_port = port;
            m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream,
                                  ProtocolType.Tcp);
            m_queue = new CircularQueue<Message>(64);
            m_nextScreenID = 0;
            m_nextBalloonID = 0;
            m_screens = new List<Screen>();
            m_bubbles = new Dictionary<int, ServerBalloon>();
            m_reader = new FeedReader(this, "http://localhost", 1000);
            //m_reader.Start();
            
            m_random = new Random();
        }
        
        public static void Main(string[] args)
        {
            Server server = new Server(4000);
            server.Run();
        }
        
        public void Run()
        {
            using(m_socket)
            {
                // Listen on the given port
                m_socket.Bind(new IPEndPoint(IPAddress.Any, m_port));
                m_socket.Listen(0);
                Console.WriteLine("Waiting for clients to connect...");
                m_socket.BeginAccept(AcceptCompleted, null);
                while(true)
                {
                    Message msg = m_queue.Dequeue();
                    if(!HandleMessage(msg))
                    {
                        break;
                    }
                }
                Console.WriteLine("Server is stopping.");
            }
        }
        
        /// <summary>
        /// Send a message to the server. It will be handled in the server's thread.
        /// </summary>
        public void EnqueueMessage(Message message)
        {
            m_queue.Enqueue(message);
        }

        /// <summary>
        /// Send a message to the server, changing its sender. It will be handled in the server's thread.
        /// </summary>
        public void EnqueueMessage(Message message, object sender)
        {
            if(message != null && sender != null)
            {
                message.Sender = sender;
            }
            m_queue.Enqueue(message);
        }

        public ServerBalloon GetBubble(int BalloonID)
        {
            lock(m_bubbles)
            {
                return m_bubbles[BalloonID];
            }
        }
        
        public Screen GetScreen(int screenID) {
            lock(m_screens) {
                foreach(Screen v in m_screens) {
                    if(screenID == v.ID) {
                        return v;
                    }
                }
                return null;
            }
        }

        public Dictionary<int, ServerBalloon> Balloons()
        {
            lock(m_bubbles) {
                return m_bubbles;
            }
        }
       
        #endregion
        #region Implementation
        private int m_port;
        private Socket m_socket;
        private CircularQueue<Message> m_queue;
        private int m_nextScreenID;
        private int m_nextBalloonID;
        private List<Screen> m_screens;
        private Dictionary<int, ServerBalloon> m_bubbles;
        private FeedReader m_reader;
        
        private Random m_random;
        
        /// <summary>
        /// Handle a message. Must be called from the server's thread.
        /// </summary>
        /// <returns>
        /// True if the message has been handled, false if messages should stop being processed.
        /// </returns>
        private bool HandleMessage(Message msg)
        {
            if(msg == null)
            {
                return false;
            }
            switch(msg.Type)
            {
            case MessageType.Connected:
                return HandleScreenConnected((ConnectedMessage)msg);
            case MessageType.Disconnected:
                return HandleScreenDisconnected((DisconnectedMessage)msg);
            case MessageType.ChangeScreen:
                return HandleChangeScreen((ChangeScreenMessage)msg);
            case MessageType.NewBalloon:
                return HandleNewBalloon((NewBalloonMessage)msg);
            case MessageType.PopBalloon:
                return HandlePopBalloon((PopBalloonMessage)msg);
            default:
                // Disconnect when receiving unknown messages
                return false;
            }
        }
        
        private bool HandleScreenConnected(ConnectedMessage msg)
        {
            int screenID = m_nextScreenID++;
            Screen screen = new Screen("Screen-" + screenID, screenID, msg.Connection, this);
            m_screens.Add( screen);
            lock(m_bubbles)
            {
                for(int i = 0; i < ServerBalloon.NewBalloonsForScreen; i++)
                {
                    // Create a new balloon for the screen
                    ServerBalloon b = CreateBalloon();
                    Direction dir;
                    float y;
                    Vector2D velocity;
                    if((b.ID % 2) == 0)
                    {
                        dir = Direction.Left;
                        velocity = ServerBalloon.VelocityRight;
                        y = 0.2f;
                    }
                    else
                    {
                        dir = Direction.Right;
                        velocity = ServerBalloon.VelocityLeft;
                        y = 0.1f;
                    }
                    screen.EnqueueMessage(new NewBalloonMessage(b.ID, dir, y, velocity), this);   
                }
            }
            return true;
        }
        
        private bool HandleScreenDisconnected(DisconnectedMessage msg)
        {
            Console.WriteLine("Screen disconnected");
            Screen s = GetScreen(msg.ScreenID);

            // Gets screen's balloons
            var balloons = s.GetBalloons();
            // Gets left and right screens
            Screen left = GetPreviousScreen(s);
            Screen right = GetNextScreen(s);
            if(left == s || right == s) {
                // if next or previous screen are equal to current screen
                // it means that this is the only screen left
                // set the balloons' screen to null,
                // they will be reacffected when a new screen connects
                foreach(KeyValuePair<int, ServerBalloon> i in balloons)
                {
                    i.Value.Screen = null;
                }
            } else {
                foreach(KeyValuePair<int, ServerBalloon> i in balloons)
                {
                    // Choose randomly between left or right screen
                    int random = m_random.Next(1);
                    if(random == 0) {
                        left.EnqueueMessage(new NewBalloonMessage(i.Value.ID, Direction.Right, 0.1f, ServerBalloon.VelocityLeft), this);
                    } else {
                        right.EnqueueMessage(new NewBalloonMessage(i.Value.ID, Direction.Left, 0.1f, ServerBalloon.VelocityRight), this);
                    }
                }
            }
            m_screens.Remove(s);
            return true;
        }
        
        private bool HandleChangeScreen(ChangeScreenMessage csm) {
            Screen oldScreen = (Screen)csm.Sender;
            Screen newScreen = ChooseNewScreen(oldScreen, csm.Direction);
            ChangeScreen(csm.BalloonID, newScreen);
            Direction newDirection = csm.Direction;
            if(csm.Direction == Direction.Left)
            {
                newDirection = Direction.Right;
            }
            else if(csm.Direction == Direction.Right)
            {
                newDirection = Direction.Left;
            }
            newScreen.EnqueueMessage(new NewBalloonMessage(csm.BalloonID, newDirection, csm.Y, csm.Velocity), this);
            return true;
        }
        
        private bool HandleNewBalloon(NewBalloonMessage nbm)
        {
            lock(m_screens) {
                if(m_bubbles.ContainsKey(nbm.BalloonID)) {
                    // Balloon already present !
                    return true;
                }
                if(m_screens.Count == 0) {
                    // No screen to display balloon -- sad
                    return true;
                }
                
                int screen_idx = m_random.Next(m_screens.Count);
                m_bubbles[nbm.BalloonID] = new ServerBalloon(nbm.BalloonID);
                m_screens[screen_idx].EnqueueMessage(nbm, this);
            }
            return true;
        }
        
        private bool HandlePopBalloon(PopBalloonMessage pbm) {
            lock(m_bubbles) {
                ServerBalloon b = GetBubble(pbm.BalloonID);
                if(m_bubbles.Remove(pbm.BalloonID) && b.Screen != null) {
                    b.Screen.EnqueueMessage(pbm, this); // Notify Screen
                }
            }
            return true;
        }
        
        private void AcceptCompleted(IAsyncResult result)
        {
            try
            {
                Socket conn = m_socket.EndAccept(result);
                EnqueueMessage(new ConnectedMessage(conn));
                m_socket.BeginAccept(AcceptCompleted, null);
            }
            catch(Exception e)
            {
                Console.WriteLine("Error with accept: {0}", e);
            }
        }

        private List<ServerBalloon> orphansBalloons()
        {
            var list = new List<ServerBalloon>();
            lock(m_bubbles)
            {
                foreach(KeyValuePair<int, ServerBalloon> i in m_bubbles)
                {
                    if(i.Value.Screen == null) {
                        list.Add(i.Value);
                    }
                }
            }
            return list;
        }
         
        private Screen GetNextScreen(Screen s) {
            int screen_idx = ScreenIndex(s);
            if(screen_idx == -1)
                return null;
            screen_idx = screen_idx != m_screens.Count - 1 ? screen_idx + 1 : 0;
            return m_screens[screen_idx];
        }
        
        private Screen GetPreviousScreen(Screen s) {
            int screen_idx = ScreenIndex(s);
            if(screen_idx == -1)
                return null;
            screen_idx = screen_idx != 0 ? screen_idx - 1 : m_screens.Count -1;
            return m_screens[screen_idx];
        }
        
        private int ScreenIndex(Screen s) {
            lock(m_screens) {
                int i = 0;
                foreach(Screen v in m_screens) {
                    if(s.ID == v.ID) {
                        return i;
                    }
                    i++;
                }
                return -1;
            }
        }
        
        private Screen ChooseNewScreen(Screen oldScreen, Direction direction)
        {
            lock(m_screens) {
                if(direction == Direction.Left) {
                    return GetPreviousScreen(oldScreen);
                } else if(direction == Direction.Right) {
                    return GetNextScreen(oldScreen);
                } else {
                    return null;
                }
            }
        }
        
        public void ChangeScreen(int BalloonID, Screen newScreen)
        {
            lock(m_bubbles)
            {
                ServerBalloon b = GetBubble(BalloonID);
                b.Screen = newScreen;
            }
        }

        private ServerBalloon CreateBalloon()
        {
            lock(m_bubbles)
            {
                int BalloonID = m_nextBalloonID++;
                ServerBalloon b = new ServerBalloon(BalloonID);
                m_bubbles[BalloonID] = b;
                return b;
            }
        }
        #endregion
	}
}
