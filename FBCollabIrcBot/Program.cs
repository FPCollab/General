using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;
using Meebey.SmartIrc4net;

namespace FBCollabIrcBot
{
    class IrcBot
    {
        public IrcClient Irc { get; private set; }
        public string Address { get; set; }
        public int Port { get; set; }
        public string Channel { get; set; }
        public IrcBot(string server, string channel)
        {
            Irc = new IrcClient();
            Address = server;
            Port = 6667;
            Channel = channel;
            Irc.AutoReconnect = true;
            Irc.Encoding = Encoding.UTF8;
            Irc.SendDelay = 200;
            Irc.ActiveChannelSyncing = true;
            Irc.OnRawMessage += new IrcEventHandler(Irc_OnRawMessage);
        }

        void Irc_OnRawMessage(object sender, IrcEventArgs e)
        {
            Console.WriteLine(e.Data.Message);
        }
        public void Start()
        {
            Irc.Connect(Address,Port);
            Irc.Login("FPGitBot", "FPGitBot", 0, "FPGitBot");
            Irc.RfcJoin(Channel);
            new Thread(o => ((IrcClient)o).Listen()).Start(Irc);
        }
        public void Stop()
        {
            Irc.Disconnect();
        }
        public void SendMsg(string msg)
        {
            if (!Irc.JoinedChannels.Contains(Channel))
                Irc.RfcJoin(Channel);
            Irc.SendMessage(SendType.Message, Channel, msg);
        }
    }
    public class WebServer
    {
        public HttpListener Listener { get; private set; }
        public event Action<object, HttpListenerContext> OnReceive;

        public WebServer(params string[] prefixes)
        {
            Listener = new HttpListener();
            foreach (var p in prefixes)
                Listener.Prefixes.Add(p);
        }
        public void Start()
        {
            if (!Listener.IsListening)
            {
                Listener.Start();
                Listener.BeginGetContext(receive, Listener);
            }
        }
        public void Stop()
        {
            if (Listener.IsListening)
                Listener.Stop();
        }

        void receive(IAsyncResult ar)
        {
            try
            {
                var listen = (HttpListener)ar.AsyncState;
                var context = listen.EndGetContext(ar);
                if (OnReceive != null)
                    OnReceive(listen, context);
                listen.BeginGetContext(receive, listen);
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }


    }
    class GithubBot
    {
        WebServer server;
        IrcBot bot;
        public GithubBot()
        {
            server = new WebServer("http://*:82/");
            bot = new IrcBot("irc.gamesurge.net","#fpcollab");

            server.OnReceive += server_OnReceive;
        }

        public void Start()
        {
            bot.Start();
            server.Start();
        }
        public void Stop()
        {
            bot.Stop();
            server.Stop();
        }


        object GetInfo(object obj, params object[] objs)
        {
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i] is string && obj is Dictionary<string, object>)
                {
                    var key = (string)objs[i];
                    var dict = (Dictionary<string, object>)obj;
                    if (!dict.TryGetValue(key, out obj))
                        return null;
                }
                else if (obj is ArrayList)
                {
                    int num = 0;
                    if (objs[i] is string)
                    {
                        if (!int.TryParse((string)objs[i], out num))
                            return null;
                    }
                    else if (objs[i] is int)
                    {
                        num = (int)objs[i];
                    }
                    else
                    {
                        return null;
                    }
                    var list = (ArrayList)obj;
                    if (num < 0 || num > list.Count)
                        return null;
                    obj = list[num];
                }
                else
                {
                    return null;
                }
            }
            return obj;
        }

        void HandlePayload(string payloadstr)
        {
            if (string.IsNullOrEmpty(payloadstr))
                return;

            var payload = fastJSON.JSON.Instance.Parse(payloadstr) as Dictionary<string, object>;
            if (payload == null)
                return;

            var commits = (ArrayList)GetInfo(payload, "commits");
            if (commits == null)
                return;

            var reponame = (string)GetInfo(payload, "repository", "name");
            if (reponame == null)
                return;

            var author = (string)GetInfo(commits, 0, "author", "name");
            if (author == null)
                return;

            bot.SendMsg(string.Format("{0} commits to repo {1} by {2}", commits.Count, reponame, author));
        }

        void server_OnReceive(object sender, HttpListenerContext context)
        {
            if (context.Request.HttpMethod.ToLower() != "post" ||
                context.Request.Url.AbsolutePath.ToLower() != "/feed.php")
            {
                context.Response.Close();
                return;
            }

            var reader = new StreamReader(context.Request.InputStream);
            var post = HttpUtility.ParseQueryString(reader.ReadToEnd(), context.Request.ContentEncoding);

            HandlePayload(post["payload"]);

            context.Response.OutputStream.Write(Encoding.ASCII.GetBytes("Okay"), 0, 4);
            context.Response.Close();
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var bot = new GithubBot();
            bot.Start();

            Console.ReadLine();

            bot.Stop();
        }
    }
}
