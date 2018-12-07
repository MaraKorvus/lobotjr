using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using PartyGroup;
using System.Runtime.CompilerServices;
using TwitchMessages;
using System.Net;
using Newtonsoft.Json;
using System.Threading;
using System.Collections.Concurrent;

[assembly: InternalsVisibleTo("ClientTests")]

namespace Client
{

    /// <summary>
    /// Client object which controls messaging to the 
    /// server and management of command events and their 
    /// respective handlers.
    /// </summary>
    public interface ITwitchClient
    {

        string User { get; }

        #region Message Events
        event AddOperationRequestEventHandler AddOperationHandler;
        void OnEvent(AddOperationRequestEventArgs e);
        event CancelOperationRequestEventHandler CancelOperationHandler;
        void OnEvent(CancelOperationRequestEventArgs e);
        event DeleteOperationRequestEventHandler DeleteOperationHandler;
        void OnEvent(DeleteOperationRequestEventArgs e);
        event EditOperationRequestEventHandler EditOperationHandler;
        void OnEvent(EditOperationRequestEventArgs e);
        event InfoOperationRequestEventHandler InfoOperationHandler;
        void OnEvent(InfoOperationRequestEventArgs e);
        event PrivEventHandler PrivHandler;
        void OnEvent(PrivEventArgs e);
        event PrivRequestEventHandler PrivRequestHandler;
        void OnEvent(PrivRequestEventArgs e);
        event WhisperEventHandler WhisperHandler;
        void OnEvent(WhisperEventArgs e);
        event WhisperRequestEventHandler WhisperRequestHandler;
        void OnEvent(WhisperRequestEventArgs e);
        event DefaultMessageEventHandler DefaultMessageHandler;
        void OnEvent(DefaultMessageEventArgs e);

        #endregion

        /// <summary>
        /// This sends the given message adding any prefix's that are required to make it valid for sending.
        /// This message is not guaranteed to be sent instantly and may queued before being sent.
        /// </summary>
        /// <param name="message"></param>
        void SendMessage(string message);
        /// <summary>
        /// This sends the given message adding any prefix's that are required to make it valid for sending.
        /// Unlike SendMessage this will send this message instantly regardless of any limitations set by this 
        /// object. 
        /// This should only be used if server commands need to be sent or time sensitive messages are required.
        /// Although caution should be taken in sending many messages in this way in a small time period as some servers 
        /// may timeout users for such behaviour. 
        /// The message sent time should be stored and used if a timer based system is used on SendMessage, therefore it is
        /// reasonable to assume that using this method will cause some delay for other queued messages.
        /// </summary>
        /// <param name="message"></param>
        void SendInstantMessage(string message);
        /// <summary>
        /// This sends a whispered message to the respective user, adding the prefix's that are required to make it valid 
        /// for sending. This message is not guaranteed to be sent instantly and may be queued before being sent.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="message"></param>
        void Whisper(string user, string message);
        /// <summary>
        /// This sends a whispered message to each respective user in the given IParty, adding the prefix's that are 
        /// required to make it valid for sending. This message is not guaranteed to be sent instantly and may 
        /// be queued before being sent.
        /// </summary>
        /// <param name="party"></param>
        /// <param name="message"></param>
        void Whisper(IParty party, string message);
        /// <summary>
        /// This sends a whispered message to the respective user, adding the prefix's that are required to make it valid 
        /// for sending.
        /// Unlike Whisper this will send this message instantly regardless of any limitations set by this 
        /// object. 
        /// This should only be used if server commands need to be sent or time sensitive messages are required.
        /// Although caution should be taken in sending many messages using this method in a small time period as some servers 
        /// may timeout users for such behaviour. 
        /// The message sent time should be stored and used when a timer based system is used on Whisper, therefore it is
        /// reasonable to assume that using this method will cause some delay for other queued messages.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="message"></param>
        void InstantWhisper(string user, string message);

        void Run();
    }

    internal class TwitchIrcClient : ITwitchClient
    {

        public string User { get; }
        private string channel;
        private readonly StreamReader reader;
        private readonly StreamWriter writer;
        private bool IsAlive { get; set; }
        private readonly BlockingCollection<string> queue;
        private DateTime lastMessage;
        private int cooldown;
        private readonly OperationRequestEventRaiser opRequestEventRaiser;
        private readonly MessageEventRaiser privEventRaiser;
        private readonly MessageEventRaiser whisperEventRaiser;

        #region Message Events
        public event AddOperationRequestEventHandler AddOperationHandler;
        public void OnEvent(AddOperationRequestEventArgs e) { AddOperationHandler(this, e); }
        public event CancelOperationRequestEventHandler CancelOperationHandler;
        public void OnEvent(CancelOperationRequestEventArgs e) { CancelOperationHandler(this, e); }
        public event DeleteOperationRequestEventHandler DeleteOperationHandler;
        public void OnEvent(DeleteOperationRequestEventArgs e) { DeleteOperationHandler(this, e); }
        public event EditOperationRequestEventHandler EditOperationHandler;
        public void OnEvent(EditOperationRequestEventArgs e) { EditOperationHandler(this, e); }
        public event InfoOperationRequestEventHandler InfoOperationHandler;
        public void OnEvent(InfoOperationRequestEventArgs e) { InfoOperationHandler(this, e); }
        public event PrivEventHandler PrivHandler;
        public virtual void OnEvent(PrivEventArgs e) { PrivHandler(this, e); }
        public event PrivRequestEventHandler PrivRequestHandler;
        public virtual void OnEvent(PrivRequestEventArgs e) { PrivRequestHandler(this, e); }
        public event WhisperEventHandler WhisperHandler;
        public virtual void OnEvent(WhisperEventArgs e) { WhisperHandler(this, e); }
        public event WhisperRequestEventHandler WhisperRequestHandler;
        public virtual void OnEvent(WhisperRequestEventArgs e) { WhisperRequestHandler(this, e); }
        public event DefaultMessageEventHandler DefaultMessageHandler;
        public virtual void OnEvent(DefaultMessageEventArgs e) { DefaultMessageHandler(this, e); }
        #endregion

        public TwitchIrcClient(StreamReader sr, StreamWriter sw, string user, string channel,
            int cooldown, OperationRequestEventRaiser opRequestEventRaiser,
            MessageEventRaiser privMessageEventRaiser,
            MessageEventRaiser whisperMessageEventRaiser)
        {
            this.reader = sr;
            this.writer = sw;
            this.queue = new BlockingCollection<string>();//new Queue<string>();
            this.User = user;
            this.channel = channel;
            this.cooldown = cooldown;
            this.opRequestEventRaiser = opRequestEventRaiser;
            this.privEventRaiser = privMessageEventRaiser;
            this.whisperEventRaiser = whisperMessageEventRaiser;
            AsyncRead();
        }


        public void SendMessage(string message)
        {
            queue.Add(message);
        }

        public void Whisper(string user, string message)
        {
            message = ".w " + user + " " + message;
            SendMessage(message);
        }

        public void Whisper(IParty party, string message)
        {
            foreach (var p in party.Players)
                Whisper(p.Name, message);
        }

        public void SendInstantMessage(string message)
        {
            string im = ":" + User + "!" + User + "@" + User + ".tmi.twitch.tv PRIVMSG #" + channel + " :";
            im += message;
            writer.WriteLine(im);
            writer.Flush();
            lastMessage = DateTime.Now;
        }

        public void InstantWhisper(string user, string message)
        {
            message = ".w " + user + " " + message;
            SendInstantMessage(message);
        }

        public void InstantWhisper(IParty party, string message)
        {
            foreach (var p in party.Players)
                InstantWhisper(p.Name, message);
        }

        public void Run()
        {
            IsAlive = true;
            lastMessage = DateTime.Now;

            while (IsAlive)
            {
                string message = queue.Take();
                if (DateTime.Now.Subtract(lastMessage).TotalMilliseconds < cooldown)
                    Thread.Sleep(cooldown - (int)(DateTime.Now.Subtract(lastMessage)).TotalMilliseconds);
                try
                {
                    Console.WriteLine("sending -> " + message);
                    SendInstantMessage(message);

                }
                catch (Exception ex)
                {
                    Console.Write(ex);
                }


            }
        }

        private async void AsyncRead()
        {
            string raw;
            while ((raw = await reader.ReadLineAsync()) != null)
            {
                if (raw.Equals("PING :tmi.twitch.tv"))
                {
                    writer.WriteLine("PONG :tmi.twitch.tv");
                    writer.Flush();
                }
                else if (Regex.IsMatch(raw, opRequestEventRaiser.AcceptingRegex))
                    opRequestEventRaiser.CreateEventAndRaise(this, raw);
                else if (Regex.IsMatch(raw, privEventRaiser.AcceptingRegex))
                    privEventRaiser.CreateEventAndRaise(this, raw);
                else if (Regex.IsMatch(raw, whisperEventRaiser.AcceptingRegex))
                    whisperEventRaiser.CreateEventAndRaise(this, raw);
                else
                    OnEvent(new DefaultMessageEventArgs(raw));

            }
        }
    }

    public static class TwitchUtils
    {
        public static IDictionary<VIEWER_TYPE, ISet<string>> FetchCurrentViewers(
            string botName, string channel)
        {
            var viewers = new Dictionary<VIEWER_TYPE, ISet<string>>();
            viewers.Add(VIEWER_TYPE.SUB, FetchSubList(botName, channel));
            FetchChattersAndFillViewers(channel, viewers);
            return viewers;
        }

        private static ISet<string> FetchSubList(string botName, string channel)
        {
            ISet<string> users = new HashSet<string>();
            channel = channel.ToLower();
            string link = "https://api.twitch.tv/kraken/channels/" + channel +
                "/subscriptions?limit=100&offset=0";

            try
            {
                string subsAuth = System.IO.File.ReadAllText(@"subsAuth.txt");
                do
                {
                    var request = (HttpWebRequest)WebRequest.Create(link);
                    request.Accept = "application/vnd.twitchtv.v3+json";
                    request.Headers.Add("Client-ID", "c95v57t6nfrpts7dqk2urruyc8d0ln1");
                    request.Headers.Add("Authorization", string.Format("OAuth {0}", subsAuth));
                    request.UserAgent = "LobosJrBot";

                    using (var response = request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        var data = reader.ReadToEnd();
                        var subRoot = JsonConvert
                            .DeserializeObject<SubscriberData.RootObject>(data);
                        foreach (var sub in subRoot.subscriptions)
                            users.Add(sub.user.name);
                        if (subRoot.subscriptions.Count == 0)
                            link = string.Empty;
                        else
                            link = subRoot._links.next;
                    }
                } while (string.IsNullOrWhiteSpace(link));

                return users;

            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Error fetching sub info. Exception: {0}",
                    ex));
                return users;
            }

        }

        private static void FetchChattersAndFillViewers(string channel,
            IDictionary<VIEWER_TYPE, ISet<string>> viewers)
        {

            try
            {
                using (var client = new WebClient())
                {
                    client.Proxy = null;
                    string url = "https://tmi.twitch.tv/group/user/" + channel + "/chatters";

                    var json = client.DownloadString(url);
                    var data = JsonConvert.DeserializeObject<Data>(json);

                    viewers.Add(VIEWER_TYPE.ADMIN, new HashSet<string>());
                    foreach (var admin in data.chatters.admins)
                        viewers[VIEWER_TYPE.ADMIN].Add(admin);

                    viewers.Add(VIEWER_TYPE.MODERATOR, new HashSet<string>());
                    foreach (var mod in data.chatters.moderators)
                        viewers[VIEWER_TYPE.MODERATOR].Add(mod);

                    viewers.Add(VIEWER_TYPE.STAFF, new HashSet<string>());
                    foreach (var staff in data.chatters.staff)
                        viewers[VIEWER_TYPE.STAFF].Add(staff);

                    viewers.Add(VIEWER_TYPE.NON_SUB, new HashSet<string>());
                    foreach (var viewer in data.chatters.viewers)
                        if (!viewers[VIEWER_TYPE.SUB].Contains(viewer))
                            viewers[VIEWER_TYPE.NON_SUB].Add(viewer);

                }

            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Error fetching chatters info. Exception: {0}",
                    ex));
            }
        }

        public enum VIEWER_TYPE
        {
            MODERATOR,
            ADMIN,
            STAFF,
            NON_SUB,
            SUB
        }
    }

    /// <summary>
    /// Factory class for creating new TwitchClients
    /// </summary>
    public class TwitchClientFactory
    {
        /// <summary>
        /// Create method to hide implementation of ITwitchClient. 
        /// This method should connect to the server once sending the required connecting strings required. 
        /// Therefore the ITwitchClient returned will have been connected to the server on return, however 
        /// it does not access whether the additional server settings have been successful. This for example could
        /// mean that an attempt has been made to connect to a channel which has failed, the client would still
        /// be returned connected to the server but not connected to the channel. 
        /// The DefaultMessageHandler should be used to review if a GlobalUserState message is sent by the
        /// server.
        /// </summary>
        /// <param name="url"></param>
        /// <param name="port"></param>
        /// <param name="user"></param>
        /// <param name="password"></param>
        /// <param name="channel"></param>
        /// <param name="recievers"></param>
        /// <returns></returns>
        public ITwitchClient create(string url, int port, string user, string password,
            string channel, int cooldown, OperationRequestEventRaiser opEventRaiser,
            PrivMessageEventRaiser privEventRaiser,
            WhisperMessageEventRaiser whisperEventRaiser)
        {
            try
            {
                TcpClient tcp = new TcpClient(url, port);
                StreamReader sr = new StreamReader(tcp.GetStream());
                StreamWriter sw = new StreamWriter(tcp.GetStream());

                sw.WriteLine("CAP REQ :twitch.tv/membership\r\n");
                sw.Flush();
                sw.WriteLine("CAP REQ :twitch.tv/tags\r\n");
                sw.Flush();
                sw.WriteLine("CAP REQ :twitch.tv/commands\r\n");
                sw.Flush();
                sw.WriteLine("PASS " + password);
                sw.WriteLine("NICK " + user);
                sw.WriteLine("USER " + user + " 8 * :" + user);
                sw.Flush();
                sw.WriteLine(":" + user + "!" + user + "@" + user + ".tmi.twitch.tv JOIN #" + channel);
                sw.Flush();

                return new TwitchIrcClient(sr, sw, user, channel, cooldown, opEventRaiser,
                    privEventRaiser, whisperEventRaiser);

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw ex;
            }


        }
    }

}