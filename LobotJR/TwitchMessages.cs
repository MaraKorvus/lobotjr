using Client;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TwitchMessages
{
    #region Messages

    public interface Message
    {
        DateTime Recieved { get; }
        string Raw { get; }
    }

    public class TwitchTags
    {
        private readonly string tags;

        public TwitchTags(string tags)
        {
            this.tags = tags;
        }

        public string GetValue(string tag)
        {
            if (!tags.Contains(tag))
                return "";
            int i = tags.IndexOf(String.Format("{0}=", tag));
            int ii = tags.IndexOf(";", i) - (i + tag.Length + 1);
            if (ii == -1)
            {
                int j = tags.IndexOf(" ", i);
                if (j != -1 && j < tags.Length - 1)
                    ii = tags.IndexOf(" ", i) - (i + tag.Length + 1);
                else
                    ii = tags.Length - (i + tag.Length + 1);
            }


            return tags.Substring(i + tag.Length + 1, ii);
        }
    }

    public interface UserMessage : Message
    {
        IReadOnlyCollection<string> Badges { get; }
        string Colour { get; }
        string DisplayName { get; }
        IReadOnlyCollection<int> Emotes { get; }
        string MessageId { get; }
        bool isTurbo { get; }
        string UserId { get; }
        string UserType { get; }
        string User { get; }
        string Message { get; }
    }

    public abstract class AbstractUserMessageEventArgs : EventArgs, UserMessage
    {
        protected readonly TwitchTags tags;
        public IReadOnlyCollection<string> Badges
        {
            get
            {
                string badgeGroup = tags.GetValue("badges");
                if (badgeGroup.Contains(","))
                    return badgeGroup.Split(',');
                return new String[] { badgeGroup };
            }
        }
        public string Colour { get { return tags.GetValue("color"); } }
        public string DisplayName { get { return tags.GetValue("display-name"); } }
        public IReadOnlyCollection<int> Emotes
        {
            get
            {
                string emoteGroups = tags.GetValue("emotes");
                if (emoteGroups.Equals(string.Empty))
                    return new int[0];

                string[] emoteGroup = emoteGroups.Split('/');
                int[] emotes = new int[emoteGroup.Length];

                for (int i = 0; i < emoteGroup.Length; i++)
                {
                    string eg = emoteGroup[i];
                    int j = emoteGroups.IndexOf(eg);
                    int jj = eg.IndexOf(":");
                    emotes[i] = int.Parse(emoteGroups.Substring(j, j + jj));
                }

                return emotes;
            }
        }
        public bool isTurbo { get { return bool.Parse(tags.GetValue("turbo")); } }
        public string Message { get; }
        public string MessageId { get { return tags.GetValue("id"); } }
        public string Raw { get; }
        public DateTime Recieved { get; }
        public string User { get; }
        public string UserId { get { return tags.GetValue("user-id"); } }
        public string UserType { get { return tags.GetValue("user-type"); } }

        public AbstractUserMessageEventArgs(TwitchTags tags, string user,
            string message, string raw)
        {
            this.tags = tags;
            this.User = user;
            this.Message = message;
            this.Raw = raw;
        }
    }

    public static class RequestUtils
    {
        public static bool IsModOrBroadcaster(IRequest request)
        {
            foreach (var badge in request.Badges)
                if (badge.Contains("moderator") ||
                    badge.Contains("broadcaster"))
                    return true;
            return false;
        }
    }

    public interface IRequest : UserMessage
    {
        string RequestName { get; }
        string RequestValue { get; }
    }

    public interface MessageEventRaiser
    {
        string AcceptingRegex { get; }
        void CreateEventAndRaise(ITwitchClient client, string raw);
    }

    #region Twitch State Messages

    // no history of using state messages, these messages only help create state
    //around the channel and globalstate for connections, therefore for the time being use
    //default

    public class DefaultMessageEventArgs : Message
    {
        public string Raw { get; }
        public DateTime Recieved { get; }

        public DefaultMessageEventArgs(string raw)
        {
            this.Recieved = DateTime.Now;
            this.Raw = raw;
        }
    }
    public delegate void DefaultMessageEventHandler(object sender, DefaultMessageEventArgs e);


    #endregion

    #region Priv

    public class PrivMessageEventRaiser : MessageEventRaiser
    {
        public string AcceptingRegex
        {
            get
            {
                return "([\\s\\S]*)?.?:(\\w*)!\\w*@\\w*.tmi.twitch.tv PRIVMSG #(\\w*) " +
                    ":([\\s\\S]*)";
            }
        }
        private static string requestRegex = "!(\\w*) ?([\\s\\S]*)";

        public PrivMessageEventRaiser()
        {

        }

        public void CreateEventAndRaise(ITwitchClient client, string raw)
        {
            Regex r = new Regex(AcceptingRegex);
            MatchCollection mc = r.Matches(raw);

            if (mc.Count == 1)
            {
                string tags = string.Empty;
                string user = string.Empty;
                string channel = string.Empty;
                string message = string.Empty;
                GroupCollection gc = mc[0].Groups;
                tags = gc[1].Value;
                user = gc[2].Value;
                channel = gc[3].Value;
                message = gc[4].Value;

                if (message.StartsWith("!"))
                    CreateRequestAndRaise(client, tags, user, channel, message, raw);
                else
                    CreateMessageAndRaise(client, tags, user, channel, message, raw);

            }
        }

        private void CreateMessageAndRaise(ITwitchClient client, string tags, string user,
            string channel, string message, string raw)
        {
            TwitchTags twitchTags = new TwitchTags(tags);
            PrivEventArgs e = new PrivEventArgs(twitchTags, user, channel, message, raw);
            client.OnEvent(e);
        }

        private void CreateRequestAndRaise(ITwitchClient client, string tags,
            string user, string channel, string message, string raw)
        {

            Regex r = new Regex(requestRegex);
            MatchCollection mc = r.Matches(message);

            if (mc.Count == 1)
            {
                string requestName = string.Empty;
                string requestValue = string.Empty;
                GroupCollection gc = mc[0].Groups;
                requestName = gc[1].Value;
                requestValue = gc[2].Value;

                TwitchTags twitchTags = new TwitchTags(tags);
                PrivRequestEventArgs e = new PrivRequestEventArgs(twitchTags, user, channel,
                    requestName, requestValue, message, raw);
                client.OnEvent(e);
            }
            else
                CreateMessageAndRaise(client, tags, user, channel, message, raw);

        }
    }

    public interface IPrivMessage : UserMessage
    {
        bool HasBits { get; }
        int Bits { get; }
        bool IsMod { get; }
        string RoomId { get; }
        bool IsUserSub { get; }
        string Channel { get; }
    }

    public interface IPrivRequest : IPrivMessage, IRequest
    {
    }

    public class PrivEventArgs : AbstractUserMessageEventArgs, IPrivMessage
    {
        public int Bits
        {
            get
            {
                string bits = tags.GetValue("bits");
                if (bits.Equals(string.Empty))
                    return 0;
                return int.Parse(bits);
            }
        }
        public string Channel { get; }
        public bool HasBits { get { return !tags.GetValue("bits").Equals(string.Empty); } }
        public bool IsMod { get { return bool.Parse(tags.GetValue("mod")); } }
        public bool IsUserSub { get { return bool.Parse(tags.GetValue("subscriber")); } }
        public string RoomId { get { return tags.GetValue("room-id"); } }

        internal PrivEventArgs(TwitchTags tags, string user, string channel,
            string message, string raw) : base(tags, user, message, raw)
        {
            this.Channel = channel;
        }

    }
    public delegate void PrivEventHandler(object sender, PrivEventArgs e);

    public class PrivRequestEventArgs : PrivEventArgs, IPrivRequest
    {
        public string RequestName { get; }
        public string RequestValue { get; }

        public PrivRequestEventArgs(TwitchTags tags, string user, string channel,
            string requestName, string requestValue, string message, string raw) :
            base(tags, user, channel, message, raw)
        {
            this.RequestName = requestName;
            this.RequestValue = requestValue;
        }


    }
    public delegate void PrivRequestEventHandler(object sender, PrivRequestEventArgs e);




    #endregion

    #region Whisper

    public class WhisperMessageEventRaiser : MessageEventRaiser
    {

        private static string requestRegex = "!(\\w*) ?([\\s\\S]*)";
        public string AcceptingRegex
        {
            get
            {
                return "([\\s\\S]*):(\\w*)!\\w*@\\w*.tmi.twitch.tv WHISPER (\\w*) :([\\s\\S]*)";
            }
        }

        public void CreateEventAndRaise(ITwitchClient client, string raw)
        {
            Regex r = new Regex(AcceptingRegex);
            MatchCollection mc = r.Matches(raw);

            if (mc.Count == 1)
            {
                string tags = string.Empty;
                string user = string.Empty;
                string message = string.Empty;
                GroupCollection gc = mc[0].Groups;
                tags = gc[1].Value;
                user = gc[2].Value;
                message = gc[4].Value;

                if (message.StartsWith("!"))
                    CreateRequestAndRaise(client, tags, user, message, raw);
                else
                    CreateMessageAndRaise(client, tags, user, message, raw);
            }
        }

        private void CreateMessageAndRaise(ITwitchClient client, string tags, string user,
            string message, string raw)
        {
            TwitchTags twitchTags = new TwitchTags(tags);
            WhisperEventArgs e = new WhisperEventArgs(twitchTags, user, message, raw);
            client.OnEvent(e);
        }

        private void CreateRequestAndRaise(ITwitchClient client, string tags, string user,
            string message, string raw)
        {
            Regex r = new Regex(requestRegex);
            MatchCollection mc = r.Matches(message);

            if (mc.Count == 1)
            {
                string requestName = string.Empty;
                string requestValue = string.Empty;
                GroupCollection gc = mc[0].Groups;
                requestName = gc[1].Value;
                requestValue = gc[2].Value;

                TwitchTags twitchTags = new TwitchTags(tags);
                WhisperRequestEventArgs e = new WhisperRequestEventArgs(twitchTags, user,
                    requestName, requestValue, raw);
                client.OnEvent(e);
            }
            else
                CreateMessageAndRaise(client, tags, user, message, raw);
        }
    }

    public interface IWhisperMessage : UserMessage
    {
        string ThreadId { get; }
    }

    public interface IWhisperRequest : IWhisperMessage, IRequest
    {

    }

    public class WhisperEventArgs : AbstractUserMessageEventArgs, IWhisperMessage
    {
        public string ThreadId { get { return tags.GetValue("thread-id"); } }

        public WhisperEventArgs(TwitchTags tags, string user, string message, string raw) :
            base(tags, user, message, raw)
        {
        }


    }
    public delegate void WhisperEventHandler(object sender, WhisperEventArgs e);

    public class WhisperRequestEventArgs : WhisperEventArgs, IWhisperRequest
    {

        public string RequestName { get; }
        public string RequestValue { get; }

        public WhisperRequestEventArgs(TwitchTags tags, string user, string requestName,
            string requestValue, string raw) :
            base(tags, user, string.Format("{0} {1}", requestName, requestValue), raw)
        {
            this.RequestName = requestName;
            this.RequestValue = requestValue;
        }

    }
    public delegate void WhisperRequestEventHandler(object sender, WhisperRequestEventArgs e);

    #endregion

    #region OperationalRequest

    public class OperationRequestEventRaiser : MessageEventRaiser
    {
        public string AcceptingRegex
        {
            get
            {
                return "([\\s\\S]*)?.?:(\\w*)!\\w*@\\w*.tmi.twitch.tv [\\s\\S]* " +
                    ":!op (\\w*)([^!]*)? !(\\w*)?([\\s\\S]*)|([\\s\\S]*)?.?:(\\w*)!\\w*@\\w*.tmi.twitch.tv [\\s\\S]* :!set=(\\S+) ?([\\s\\S]*)";
            }
        }

        public void CreateEventAndRaise(ITwitchClient client, string raw)
        {
            Regex r = new Regex(AcceptingRegex);
            MatchCollection mc = r.Matches(raw);

            if (mc.Count == 1)
            {
                GroupCollection gc = mc[0].Groups;

                string rawTags, user, operation, opTags, requestName, requestValue;
                TwitchTags tags;

                if (!string.IsNullOrEmpty(gc[1].Value))
                {
                    rawTags = gc[1].Value;
                    tags = new TwitchTags(rawTags);
                    user = gc[2].Value;
                    operation = gc[3].Value;
                    opTags = gc[4].Value;
                    requestName = gc[5].Value;
                    requestValue = gc[6].Value;
                }
                else
                {
                    rawTags = gc[7].Value;
                    tags = new TwitchTags(rawTags);
                    user = gc[8].Value;
                    operation = "edit";
                    opTags = string.Empty;
                    requestName = gc[9].Value;
                    requestValue = gc[10].Value;
                }

                if (operation.Equals("add", StringComparison.CurrentCultureIgnoreCase))
                {
                    AddOperationRequestEventArgs e = new AddOperationRequestEventArgs(tags,
                        user, opTags, requestName, requestValue, raw);
                    if (RequestUtils.IsModOrBroadcaster(e))
                        client.OnEvent(e);
                }
                else if (operation.Equals("cancel", StringComparison.CurrentCultureIgnoreCase))
                {
                    CancelOperationRequestEventArgs e = new CancelOperationRequestEventArgs(
                        tags, user, requestName, requestValue, raw);
                    if (RequestUtils.IsModOrBroadcaster(e))
                        client.OnEvent(e);
                }
                else if (operation.Equals("delete", StringComparison.CurrentCultureIgnoreCase))
                {
                    DeleteOperationRequestEventArgs e = new DeleteOperationRequestEventArgs(
                        tags, user, requestName, requestValue, raw);
                    if (RequestUtils.IsModOrBroadcaster(e))
                        client.OnEvent(e);
                }
                else if (operation.Equals("edit", StringComparison.CurrentCultureIgnoreCase))
                {
                    EditOperationRequestEventArgs e = new EditOperationRequestEventArgs(
                        tags, user, opTags, requestName, requestValue, raw);
                    if (RequestUtils.IsModOrBroadcaster(e))
                        client.OnEvent(e);
                }
                else if (operation.Equals("info", StringComparison.CurrentCultureIgnoreCase))
                {
                    InfoOperationRequestEventArgs e = new InfoOperationRequestEventArgs(
                        tags, user, requestName, requestValue, raw);
                    if (RequestUtils.IsModOrBroadcaster(e))
                        client.OnEvent(e);
                }

            }
        }
    }

    public class AbstractOperationRequestEventArgs : AbstractUserMessageEventArgs, IRequest
    {
        public string Operation { get; }
        public string RequestName { get; }
        public string RequestValue { get; }
        public bool IsWhisper { get { return Raw.Contains("WHISPER"); } }

        public AbstractOperationRequestEventArgs(TwitchTags tags, string user,
            string operation, string requestName, string requestValue, string raw)
            : base(tags, user, string.Format("!op {0} {1} {2}", operation,
                 requestName, requestValue), raw)
        {
            this.Operation = operation;
            this.RequestName = requestName;
            this.RequestValue = requestValue;
        }


    }

    public class AddOperationRequestEventArgs : AbstractOperationRequestEventArgs
    {
        private readonly TwitchTags requestTags;

        public AddOperationRequestEventArgs(TwitchTags tags, string user,
            string requestTags, string requestName, string requestValue,
            string raw) : base(tags, user, "add", requestName, requestValue, raw)
        {
            this.requestTags = new TwitchTags(requestTags);
        }

        public int GetCooldown()
        {
            string cd = requestTags.GetValue("cd");
            if (cd.Length != 0 && Regex.IsMatch(cd, "\\d+"))
                return int.Parse(cd);
            return 0;
        }

        public string[] GetPermittedUsers()
        {
            string users = requestTags.GetValue("ut");
            if (users.Length != 0)
                if (users.Contains("|"))
                    return users.Split('|');
                else
                    return new string[] { users };
            return new string[] { };
        }


    }
    public delegate void AddOperationRequestEventHandler(object sender,
        AddOperationRequestEventArgs e);

    public class CancelOperationRequestEventArgs : AbstractOperationRequestEventArgs
    {
        public CancelOperationRequestEventArgs(TwitchTags tags, string user,
            string requestName, string requestValue, string raw)
            : base(tags, user, "cancel", requestName, requestValue, raw)
        {
        }
    }
    public delegate void CancelOperationRequestEventHandler(object sender,
        CancelOperationRequestEventArgs e);

    public class DeleteOperationRequestEventArgs : AbstractOperationRequestEventArgs
    {
        public DeleteOperationRequestEventArgs(TwitchTags tags, string user,
            string requestName, string requestValue, string raw)
            : base(tags, user, "delete", requestName, requestValue, raw)
        {
        }
    }
    public delegate void DeleteOperationRequestEventHandler(object sender,
        DeleteOperationRequestEventArgs e);

    public class EditOperationRequestEventArgs : AbstractOperationRequestEventArgs
    {
        private readonly TwitchTags requestTags;

        public bool isCooldownDefault
        {
            get
            {
                return string.IsNullOrEmpty(requestTags.GetValue("cd"));
            }
        }
        public bool isPermittedUsersDefault
        {
            get
            {
                return string.IsNullOrEmpty(requestTags.GetValue("ut"));
            }
        }


        public EditOperationRequestEventArgs(TwitchTags tags, string user, string requestTags,
            string requestName, string requestValue, string raw) :
            base(tags, user, "edit", requestName, requestValue, raw)
        {
            this.requestTags = new TwitchTags(requestTags);
        }

        public int GetCooldown()
        {
            string cd = requestTags.GetValue("cd");
            if (cd.Length != 0 && Regex.IsMatch(cd, "\\d+"))
                return int.Parse(cd);
            return 0;
        }

        public string[] GetPermittedUsers()
        {
            string users = requestTags.GetValue("ut");
            if (users.Length != 0)
                if (users.Contains("|"))
                    return users.Split('|');
                else
                    return new string[] { users };
            return new string[] { };
        }
    }
    public delegate void EditOperationRequestEventHandler(object sender,
        EditOperationRequestEventArgs e);

    public class InfoOperationRequestEventArgs : AbstractOperationRequestEventArgs
    {
        public InfoOperationRequestEventArgs(TwitchTags tags, string user,
            string requestName, string requestValue, string raw)
            : base(tags, user, "info", requestName, requestValue, raw)
        {
        }
    }
    public delegate void InfoOperationRequestEventHandler(object sender,
        InfoOperationRequestEventArgs e);

    #endregion

    #endregion
}