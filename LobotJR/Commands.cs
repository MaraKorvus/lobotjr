using Client;
using LobotJR;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TwitchMessages;

namespace Command
{

    public interface ICommand
    {
        string Name { get; }
        IReadOnlyList<string> PermittedUsers { get; }
        int Cooldown { get; }
        bool IsCustom { get; }
        bool HasPermission(string userType);
        string Cancel();
        /// <summary>
        /// Recommended format for overriding classes is: 
        /// "Command: [command name] ([custom command note]).
        ///[Request type]
        ///only.Available to users: [Permitted Users]
        ///Cooldown: [cooldown]
        ///seconds. [Custom description of purpose of command]".
        ///example of below is what is used in the EchoCommand using Stringbuilder.
        ///<code>
        ///StringBuilder sb = new StringBuilder("Command: ");
        ///sb.append(getName());
        ///sb.append(" (custom echo command).");
        ///sb.append(" PRIVMSG only. ");
        ///sb.append("Available to users: ");
        ///getPermittedUsers().forEach((pu) ->
        ///{
        /// sb.append(pu);
        ///sb.append(" ");
        ///});
        ///sb.append("Cooldown: ");
        ///sb.append(getCooldown());
        ///sb.append(" seconds. ");
        ///sb.append("Value: ");
        ///sb.append(echo);
        ///return sb.toString();
        ///</code>
        /// </summary>
        /// <returns>
        /// String of information about the command to be sent back to the
        ///requester.
        ///</returns>
        string Info();
    }

    public interface Command<T> : ICommand where T : IRequest
    {
        string Execute(T request);
    }

    public abstract class AbstractCommand<T> : Command<T> where T : IRequest
    {
        private DateTime lastSuccessfulExecute;
        public string Name { get; }
        public int Cooldown { get; }
        public IReadOnlyList<string> PermittedUsers { get; }
        public abstract bool IsCustom { get; }

        public AbstractCommand(string name, int cooldown, params string[] users)
        {
            this.lastSuccessfulExecute = DateTime.Now;
            this.Name = name;
            this.Cooldown = cooldown;
            this.PermittedUsers = users;
        }

        public AbstractCommand(string name) : this(name, 0)
        {

        }

        public bool HasPermission(string userType)
        {
            if (PermittedUsers.Count == 0)
                return true;
            return PermittedUsers.Any(pu =>
            {
                return pu.Equals(userType, StringComparison.CurrentCultureIgnoreCase);
            });
        }


        public string Execute(T request)
        {
            string result = string.Empty;
            if (DateTime.Now.Subtract(lastSuccessfulExecute).TotalSeconds > Cooldown)
                result = ExecuteCommand(request);
            if (string.IsNullOrEmpty(result))
                lastSuccessfulExecute = DateTime.Now;
            return result;
        }
        public abstract string ExecuteCommand(T request);
        public abstract string Cancel();
        public abstract string Info();
    }

    public class CommandManager
    {
        private ITwitchClient client;
        private readonly CustomCommandFactory customCommandFactory;
        private readonly ICustomCommandRepository customCommandRepository;
        private readonly ISet<Command<IPrivRequest>> privCommands;
        private readonly ISet<Command<IWhisperRequest>> whisperCommands;

        public CommandManager(ITwitchClient client,
            CustomCommandFactory ccf, ICustomCommandRepository ccr)
        {
            this.client = client;
            this.customCommandFactory = ccf;
            this.customCommandRepository = ccr;
            this.privCommands = new HashSet<Command<IPrivRequest>>();
            privCommands.UnionWith(ccr.GetAll());
            this.whisperCommands = new HashSet<Command<IWhisperRequest>>();
        }

        public void Handle(object sender, AddOperationRequestEventArgs e)
        {
            CustomCommand cc = customCommandFactory.Create(e.RequestName, e.RequestValue,
                e.GetCooldown(), e.GetPermittedUsers());
            if (AddCommand(cc))
            {
                string message = string.Format("!{0} command successfully added!", cc.Name);
                if (e.IsWhisper)
                    client.Whisper(e.User, message);
                else
                    client.SendMessage(message);
            }
        }

        public void Handle(object sender, CancelOperationRequestEventArgs e)
        {
            string message = string.Empty;
            ICommand command = FetchFromAll(e.RequestName);

            if (command != null)
                message = command.Cancel();

            if (!string.IsNullOrWhiteSpace(message))
                if (e.IsWhisper)
                    client.Whisper(e.User, message);
                else
                    client.SendMessage(message);
        }

        public void Handle(object sender, DeleteOperationRequestEventArgs e)
        {
            CustomCommand command = FetchFromSet(e.RequestName,
                customCommandRepository.GetAll());

            string message = string.Empty;

            if (DeleteCommand(command))
                message = string.Format("!{0} command has been deleted!", command.Name);

            if (!string.IsNullOrEmpty(message))
            {
                if (e.IsWhisper)
                    client.Whisper(e.User, message);
                else
                    client.SendMessage(message);
            }

        }

        public void Handle(object sender, EditOperationRequestEventArgs e)
        {
            string message = string.Empty;
            CustomCommand cc = FetchFromSet(e.RequestName, customCommandRepository.GetAll());

            if (DeleteCommand(cc))
            {
                int cooldown = e.isCooldownDefault ? cc.Cooldown : e.GetCooldown();
                string[] users = e.isPermittedUsersDefault ? cc.PermittedUsers.ToArray() :
                    e.GetPermittedUsers();

                CustomCommand toAdd = customCommandFactory.Create(e.RequestName,
                    e.RequestValue, e.GetCooldown(), e.GetPermittedUsers());

                if (AddCommand(toAdd))
                    message = string.Format("!{0} command has been updated!", e.RequestName);
                else if (AddCommand(cc))
                    message = string.Format("!{0} command has NOT been updated, please " +
                        "the edit operation syntax for errors.",
                        e.RequestName);
                else
                    message = string.Format("!{0} has been corrupted during updating, " +
                        "please using the add operation to make this command.");

            }
            else
                message = "Unable to change the command at this time!";

            client.SendMessage(message);

        }

        public void Handle(object sender, InfoOperationRequestEventArgs e)
        {
            string message = string.Empty;

            ICommand command = FetchFromAll(e.RequestName);

            if (command != null)
                message = command.Info();

            if (!string.IsNullOrWhiteSpace(message))
                client.Whisper(e.User, message);
        }

        public void Handle(object sender, PrivRequestEventArgs e)
        {
            string message = string.Empty;
            Command<IPrivRequest> command = FetchFromSet(e.RequestName, privCommands);

            message = command?.Execute(e);

            if (!string.IsNullOrEmpty(message))
                client.SendMessage(message);
        }

        public void Handle(object sender, WhisperRequestEventArgs e)
        {
            string message = string.Empty;
            Command<IWhisperRequest> command = FetchFromSet(e.RequestName, whisperCommands);

            message = command?.Execute(e);

            if (!string.IsNullOrEmpty(message))
                client.Whisper(e.User, message);
        }

        private ICommand FetchFromAll(string requestName)
        {
            ICommand command = privCommands.SingleOrDefault(c =>
            {
                return (c.Name.Equals(requestName,
                    StringComparison.CurrentCultureIgnoreCase));
            });
            if (command == null)
                command = whisperCommands.SingleOrDefault(c =>
                {
                    return (c.Name.Equals(requestName,
                        StringComparison.CurrentCultureIgnoreCase));
                });

            return command;
        }

        private T FetchFromSet<T>(string requestName, ISet<T> set)
            where T : ICommand
        {
            return set.SingleOrDefault(c =>
            {
                return c.Name.Equals(requestName, StringComparison.CurrentCultureIgnoreCase);
            });
        }

        private bool AddCommand(CustomCommand command)
        {
            if (command == null)
                return false;
            customCommandRepository.Save(command);
            privCommands.Add(command);
            return true;

        }

        private bool DeleteCommand(CustomCommand command)
        {
            if (command == null)
                return false;
            privCommands.Remove(command);
            customCommandRepository.GetAll().Remove(command);
            return true;
        }

        public void Add(Command<IPrivRequest> command)
        {
            privCommands.Add(command);
        }

        public void AddAll(params Command<IPrivRequest>[] commands)
        {
            foreach (var command in commands)
                Add(command);
        }

        public void Add(Command<IWhisperRequest> command)
        {
            whisperCommands.Add(command);
        }

        public void AddAll(params Command<IWhisperRequest>[] commands)
        {
            foreach (var command in commands)
                Add(command);
        }

    }

    #region Commands

    public class UptimeCommand : AbstractCommand<IPrivRequest>
    {
        private Nullable<DateTime> start;

        public override bool IsCustom { get { return false; } }

        public UptimeCommand() : base("uptime", 10)
        {
        }

        public void SetStart(DateTime start)
        {
            this.start = start;
        }

        public void End() { this.start = null; }

        public override string Cancel() { return string.Empty; }

        public override string ExecuteCommand(IPrivRequest request)
        {
            if (!start.HasValue)
                return string.Format("{0} is not currently broadcasting!", request.Channel);

            return string.Format("{0} has been broadcasting for {1}.", request.Channel,
                DateTime.Now.Subtract(start.Value).ToString(@"hh\:mm\:ss"));
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" PRIVMSG only. ");
            sb.Append("Available to users: ");
            if (PermittedUsers.Count == 0)
                sb.Append("ALL ");
            else
                foreach (var pu in PermittedUsers)
                {
                    sb.Append(pu);
                    sb.Append(" ");
                }
            sb.Append("Cooldown: ");
            sb.Append(Cooldown);
            sb.Append(" seconds. ");
            sb.Append("Command for the duration of the current broadcast. Broadcasting ");
            sb.Append("flag needs to be set using '!broadcasting on' to set the start time. ");
            sb.Append("While the stream is not broadcasting this command will simply display ");
            sb.Append("[channel] is not currently broadcasting! However when the stream is ");
            sb.Append("broadcasting then the uptime is displayed hh:mm:ss where 'hh' is ");
            sb.Append("hours, 'mm' is mins, and 'ss' is seconds respectively.");
            return sb.ToString();
        }
    }

    public class TimeCommand : AbstractCommand<IPrivRequest>
    {
        private DateTime last;
        public override bool IsCustom { get { return false; } }

        public TimeCommand() : base("time", 10)
        {
            this.last = DateTime.Now;
        }

        public override string Cancel() { return string.Empty; }

        public override string ExecuteCommand(IPrivRequest request)
        {
            if (DateTime.Now.Subtract(last).TotalSeconds < Cooldown)
                return "";
            last = DateTime.Now;
            return string.Format("The time is now {0} for {1}.",
                DateTime.Now.ToString("HH:mm tt"), request.Channel);
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" PRIVMSG only. ");
            sb.Append("Available to users: ");
            if (PermittedUsers.Count == 0)
                sb.Append("ALL ");
            else
                foreach (var pu in PermittedUsers)
                {
                    sb.Append(pu);
                    sb.Append(" ");
                }
            sb.Append("Cooldown: ");
            sb.Append(Cooldown);
            sb.Append(" seconds. ");
            sb.Append("Command for broadcasters Local time");
            return sb.ToString();
        }
    }

    public class PlaylistCommand : AbstractCommand<IPrivRequest>
    {
        private readonly string url;

        public override bool IsCustom { get { return false; } }

        public PlaylistCommand(string playlistUrl) : base("playlist", 10)
        {
            this.url = playlistUrl;
        }

        public override string Cancel() { return string.Empty; }

        public override string ExecuteCommand(IPrivRequest request)
        {
            return string.Format("{0}'s Spotify Playlist: {1}", request.Channel, url);
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" PRIVMSG only. ");
            sb.Append("Available to users: ");
            if (PermittedUsers.Count == 0)
                sb.Append("ALL ");
            else
                foreach (var pu in PermittedUsers)
                {
                    sb.Append(pu);
                    sb.Append(" ");
                }
            sb.Append("Cooldown: ");
            sb.Append(Cooldown);
            sb.Append(" seconds. ");
            sb.Append("Command for channel Spotify playlist");
            return sb.ToString();
        }
    }

    public class OpinionCommand : AbstractCommand<IPrivRequest>
    {
        public override bool IsCustom { get { return false; } }

        public OpinionCommand() : base("opinion", 10)
        {

        }

        public override string Cancel() { return string.Empty; }

        public override string ExecuteCommand(IPrivRequest request)
        {
            return "Opinions go here: http:////i.imgur.com/3jRQ2fa.jpg";
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" PRIVMSG only. ");
            sb.Append("Available to users: ");
            if (PermittedUsers.Count == 0)
                sb.Append("ALL ");
            else
                foreach (var pu in PermittedUsers)
                {
                    sb.Append(pu);
                    sb.Append(" ");
                }
            sb.Append("Cooldown: ");
            sb.Append(Cooldown);
            sb.Append(" seconds. ");
            sb.Append("Command for opinions! link to an imgur jpeg.");
            return sb.ToString();
        }
    }

    public class PunCommand : AbstractCommand<IPrivRequest>
    {
        public override bool IsCustom { get { return false; } }

        public PunCommand() : base("pun", 10)
        { }

        public override string Cancel() { return string.Empty; }

        public override string ExecuteCommand(IPrivRequest request)
        {
            string path = @"C:\Users\Owner\Dropbox\Stream\puns.txt";
            string myFile = "";
            if (!File.Exists(path))
                return string.Empty;

            myFile = File.ReadAllText(path);
            string[] puns = myFile.Split('\n');
            int numPuns = puns.Length;
            Random random = new Random();
            int randomNumber = random.Next(0, numPuns);
            return puns[randomNumber];
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" PRIVMSG only. ");
            sb.Append("Available to users: ");
            if (PermittedUsers.Count == 0)
                sb.Append("ALL ");
            else
                foreach (var pu in PermittedUsers)
                {
                    sb.Append(pu);
                    sb.Append(" ");
                }
            sb.Append("Cooldown: ");
            sb.Append(Cooldown);
            sb.Append(" seconds. ");
            sb.Append("Command for a randomized pun, They are incredibly punny...");
            return sb.ToString();
        }
    }

    public class QuoteCommand : AbstractCommand<IPrivRequest>
    {
        public override bool IsCustom { get { return false; } }

        public QuoteCommand() : base("quote", 10)
        {

        }

        public override string Cancel() { return string.Empty; }

        public override string ExecuteCommand(IPrivRequest request)
        {
            if (request.RequestValue.Equals(string.Empty))
                return retrieveQuote();
            else
                return addQuote(request);
        }

        private string retrieveQuote()
        {
            string path = @"C:\Users\Owner\Dropbox\Stream\quotes.txt";
            string myFile = "";
            if (!File.Exists(path))
                return string.Empty;

            myFile = File.ReadAllText(path);
            string[] quotes = myFile.Split('\n');
            int numQuotes = quotes.Length;
            Random random = new Random();
            int randomNumber = random.Next(0, numQuotes);
            return quotes[randomNumber];
        }

        private string addQuote(IPrivRequest request)
        {
            if (!RequestUtils.IsModOrBroadcaster(request))
                return "";
            string quote = request.RequestValue;
            string path = @"C:\Users\Owner\Dropbox\Stream\quotes.txt";
            if (!File.Exists(path))
                return string.Empty;

            using (StreamWriter fileWriter = File.AppendText(path))
            {
                try
                {
                    fileWriter.WriteLine(request.RequestValue);
                    fileWriter.Flush();
                    return "Quote has been added!";
                }
                catch (IOException ex)
                {
                    Console.WriteLine(ex);
                    return string.Empty;
                }
            }

        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" PRIVMSG only. ");
            sb.Append("Available to users: ");
            if (PermittedUsers.Count == 0)
                sb.Append("ALL ");
            else
                foreach (var pu in PermittedUsers)
                {
                    sb.Append(pu);
                    sb.Append(" ");
                }
            sb.Append("Cooldown: ");
            sb.Append(Cooldown);
            sb.Append(" seconds. ");
            sb.Append("Command for adding and retrieving quotes. !quote will return a ");
            sb.Append("randomized quote. !quote [quote] will add the quote to list of quotes");
            return sb.ToString();
        }
    }

    public class RaffleCommand : AbstractCommand<IPrivRequest>
    {
        private readonly ITwitchClient client;
        private readonly int defaultDelay;
        private readonly FutureTaskRegistry taskRegistry;
        private SingularScheduledTask raffleTask;
        private string raffleKeyword;
        private readonly ISet<string> entries;

        public override bool IsCustom { get { return false; } }

        public RaffleCommand(ITwitchClient client, int defaultDelay,
            FutureTaskRegistry registry)
            : base("raffle", 10, "broadcaster", "moderator")
        {
            this.client = client;
            this.defaultDelay = defaultDelay;
            this.taskRegistry = registry;
            client.PrivHandler += RaffleHandler;
            this.entries = new HashSet<string>();
        }

        public override string ExecuteCommand(IPrivRequest request)
        {
            if (request.RequestValue.Count() == 0)
                return "";

            if (request.RequestValue.Equals("winner",
                StringComparison.CurrentCultureIgnoreCase))
                return SelectManualWinner(request);

            if (raffleTask != null && !raffleTask.IsDone())
                return string.Format(".w {0} {0} raffle is still accepting requests, "
                        + "please either wait for the timer to run out [{0} seconds] "
                        + "or end it manually by using !raffle winner",
                        request.User, raffleKeyword,
                        raffleTask.GetDelay().Seconds);

            string[] args = request.RequestValue.Split(' ');

            if (args.Count() == 1)
                return SetupRaffle(request, request.RequestValue, defaultDelay);

            string keyword = args[0];
            string wait = args[1];
            if (Regex.IsMatch(wait, "\\d+"))
                return SetupRaffle(request, keyword, int.Parse(wait));
            else
                return "";

        }

        private string SelectManualWinner(IPrivRequest request)
        {
            if (raffleTask == null)
                return string.Format(".w {0} No raffle has been setup! please use "
                    + "!raffle [keyword] to setup a raffle!",
                    request.User);

            if (!raffleTask.Cancel(true))
                return "";

            string result = SelectWinner();
            if (result.Count() == 0)
                result = "No correct entries have been made in the time limit! "
                        + "No winner after the time limit!";

            client.Whisper(request.User, result);
            return result;
        }

        private string SelectWinner()
        {
            if (entries.Count == 0)
                return string.Empty;
            Random rnd = new Random();
            string result = entries.ElementAt(rnd.Next(0, entries.Count));
            return string.Format("The winner of the {0} raffle is...{1}!", raffleKeyword,
                result);
        }

        private string SetupRaffle(IPrivRequest request, string keyword, int mins)
        {
            entries.Clear();
            raffleKeyword = keyword;
            raffleTask = new SingularScheduledTask(taskRegistry, TimeSpan.FromMinutes(mins),
                () =>
                {
                    string result = SelectWinner();
                    if (result.Count() == 0)
                        result = "No correct entries have been made in the time limit! "
                                + "No winner after the time limit!";
                    client.Whisper(request.User, result);
                    client.SendMessage(result);
                });

            return string.Format("A new raffle has been started! To enter type '{0}'. "
                + "The winner will be choosen in {1} mins", keyword, mins);
        }

        private void RaffleHandler(object o, IPrivMessage message)
        {
            if (raffleKeyword == null)
                return;
            if (message.Message.Contains(raffleKeyword) && !entries.Contains(message.User))
                entries.Add(message.User);
        }

        public override string Cancel()
        {
            if (raffleKeyword != null && raffleTask != null && !raffleTask.IsDone())
            {
                raffleTask.Cancel(true);
                entries.Clear();
                string cancelMessage = string.Format("!{0} raffle has been cancelled! "
                    + "No winner will be choosen.", raffleKeyword);
                raffleKeyword = null;
                return cancelMessage;
            }
            return string.Empty;
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" PRIVMSG only. ");
            sb.Append("Available to users: ");
            if (PermittedUsers.Count == 0)
                sb.Append("All ");
            else
                foreach (var pu in PermittedUsers)
                {
                    sb.Append(pu);
                    sb.Append(" ");
                }
            sb.Append("Cooldown: ");
            sb.Append(Cooldown);
            sb.Append(" seconds. ");
            sb.Append("timer based raffle. The syntax is as follows; ");
            sb.Append("[1]!raffle keyword [2]!raffle keyword 10. ");
            sb.Append("[1] will create a new raffle with a default timer. ");
            sb.Append("[2] will create a new raffle with a set 10min timer. ");
            sb.Append("both use 'keyword' as the keyword and chatters are ");
            sb.Append(" instructed to type the keyword to enter the raffle. ");
            sb.Append("raffles can be ended prematurely by using !raffle winner. ");

            if (raffleTask != null && !raffleTask.IsDone())
            {
                sb.Append(raffleKeyword);
                sb.Append(" raffle is currently running and will finish in ");
                sb.Append(raffleTask.GetDelay().Seconds);
                sb.Append(" seconds.");
            }

            return sb.ToString();
        }
    }

    public class CustomCommand : AbstractCommand<IPrivRequest>
    {
        private readonly string echo;

        public override bool IsCustom { get { return true; } }

        public CustomCommand(string name, string echo, int cooldown, params string[] users)
            : base(name, cooldown, users)
        {
            this.echo = echo;
        }

        public CustomCommand(string name, string echo) : base(name)
        {
            this.echo = echo;
        }

        public override string Cancel() { return string.Empty; }

        public override string ExecuteCommand(IPrivRequest request)
        {
            string message = echo.Replace("[user]", request.User);
            message = echo.Replace("[channel]", request.Channel);
            return message;
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" (custom echo command).");
            sb.Append(" PRIVMSG only. ");
            sb.Append("Available to users: ");
            if (PermittedUsers.Count == 0)
                sb.Append("ALL ");
            foreach (var u in PermittedUsers)
            {
                sb.Append(u);
                sb.Append(" ");
            }
            sb.Append("Cooldown: ");
            sb.Append(Cooldown);
            sb.Append(" seconds. ");
            sb.Append("Raw Value: ");
            sb.Append(echo);
            return sb.ToString();
        }
    }

    public class CustomCommandFactory
    {
        public CustomCommand Create(string name, string value, int cooldown,
            string[] permittedUsers)
        {
            return new CustomCommand(name, value, cooldown, permittedUsers);
        }

        //public CustomCommand Create(AddOperationRequestEventArgs e)
        //{
        //    return new CustomCommand(e.RequestName, e.RequestValue, e.GetCooldown(),
        //        e.GetPermittedUsers());
        //}

    }

    public interface ICustomCommandRepository
    {
        ISet<CustomCommand> GetAll();
        void Save(CustomCommand cc);
        void Save();
    }

    public class CustomCommandRepository : ICustomCommandRepository
    {
        private const string filePath = "CustomCommands.json";
        private ISet<CustomCommand> commands;

        public CustomCommandRepository()
        {
            this.commands = new HashSet<CustomCommand>();
        }

        public ISet<CustomCommand> GetAll()
        {
            if (commands.Count != 0)
                return commands;
            try
            {
                if (!File.Exists(filePath))
                    using (File.Create(filePath)) { }

                if (File.Exists(filePath))
                {
                    var customCommands = JsonConvert
                        .DeserializeObject<ISet<CustomCommand>>(File.ReadAllText(filePath));
                    if (customCommands != null)
                        commands.UnionWith(customCommands);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Error fetching Custom Commands. Exception: {0}",
                    ex));
            }

            return commands;
        }

        public void Save(CustomCommand cc)
        {
            commands.Add(cc);
        }

        public void Save()
        {
            try
            {
                lock (commands)
                {
                    var json = JsonConvert.SerializeObject(commands);
                    byte[] data = Encoding.UTF8.GetBytes(json);
                    if (!File.Exists(filePath))
                        using (File.Create(filePath)) { }
                    if (File.Exists(filePath))
                        File.WriteAllBytes(filePath, data);
                    else
                        Console.WriteLine("Unable to find or create a new custom command file!");

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Error trying to save Custom Commands. " +
                    "Exception: {0}", ex));
            }
        }
    }

    #endregion
}