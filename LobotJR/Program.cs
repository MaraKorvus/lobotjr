using System;
using System.Threading;
using Equipment;
using Client;
using Adventure;
using PartyGroup;
using Status;
using Player;
using TwitchMessages;
using Command;
using LobotJR;

namespace LobotJr
{

    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string url = "irc.chat.twitch.tv";
            int port = 80;

            string user = "lobotjr";
            string oAuthToken = System.IO.File.ReadAllText(@"token.txt"); // token.txt must be in the same folder as EXE
            string channel = "lobosjr";

            //Set up one IrcClient, only one is required it allows better cooldown managerment and traffic will
            //never cause this code to run slower then any twitch cool down for bots.
            TwitchClientFactory icf = new TwitchClientFactory();
            ITwitchClient client = icf.create(url, port, user, oAuthToken, channel, 600,
                new OperationRequestEventRaiser(), new PrivMessageEventRaiser(),
                new WhisperMessageEventRaiser());
            client.DefaultMessageHandler += (o, e) =>
            {
                Console.WriteLine(string.Format("System: {0}", e.Raw));
            };

            //Set up Legacy Item -> IEquipment converter.
            LegacyItemEquipmentConverter liec = new LegacyItemEquipmentConverter();
            //Set up Equipment repository, if legacy then this will load all items from old files and convert them
            //into IEquipment in memory.
            IEquipmentRepository equipmentRepository = LegacyEquipmentRepository
                .getInstance(liec, LegacyEquipmentRepository.LEGACY_ITEM_BRIDGE_FILE_PATH,
                LegacyEquipmentRepository.LEGACY_ITEM_PREFIX_FILE_PATH);

            //Set up Player Repository, Factory and default ILevelObservers
            ILevelObserver levelUpNotifier = new LevelupNotifier(client);
            PlayerFactory pf = new PlayerFactory(3, 20, levelUpNotifier);
            ILevelObserver classChoiceNotifier = new ClassChoiceNotifier(client, pf, 3);
            pf.GetCurrentDefaultObservers().Add(classChoiceNotifier);
            IPlayerRepository playerRepo = LegacyPlayerRepository.getInstance(3, 20, pf,
                equipmentRepository, LegacyPlayerRepository.LEGACY_USER_COINS_FILE_PATH,
                LegacyPlayerRepository.LEGACY_USER_XP_FILE_PATH,
                LegacyPlayerRepository.LEGACY_USER_CLASS_FILE_PATH, "players.json");


            //Set up Adventure repository.
            IAdventureRepository adventureRepository = LegacyAdventureRepository
                .getInstance(LegacyAdventureRepository.LEGACY_DUNGEON_BRIDGE_FILE_PATH,
                LegacyAdventureRepository.LEGACY_DUNGEON_FILE_PATH_PREFIX, equipmentRepository);

            //Set up Adventure manager who's Run() func should be used to run adventures on a daemon thread
            IAdventureManager adventureManager = new AdventureManager(client, 3);
            new Thread(() =>
            {
                Thread.CurrentThread.Name = "Adventure Manager";
                Thread.CurrentThread.IsBackground = true;
                adventureManager.Run();
            }).Start();
            //Set up Party Pool, this keeps track of current parties. 
            IPartyPool partyPool = new PartyPool(client);
            //Set up Group finder, use the current adventure managers queue. Decide party size capacity for 
            // group finder.
            GroupFinderFactory gff = new GroupFinderFactory();
            IGroupFinder groupFinder = gff.Create(partyPool, 3, adventureRepository,
                adventureManager);

            //Set up FutureTask Registry which will keep track of time based operations
            FutureTaskRegistry futureTaskRegistry = new FutureTaskRegistry();

            //Set up Custom Command Factory and Repository for the Command Manager allowing
            //for saved custom commands to be used aswell as providing capability for new 
            //custom commands to be created from chat(broadcaster/mod only).
            CustomCommandFactory ccf = new CustomCommandFactory();
            CustomCommandRepository ccr = new CustomCommandRepository();
            CommandManager commandManager = new CommandManager(client, ccf, ccr);

            //Initialise all commands to be added to the command manager, seperated by 
            //the source of the request, either PRVMSG or WHISPER.
            #region Initialisation of Commands 

            #region General Commands

            UptimeCommand uptime = new UptimeCommand();
            Command<IPrivRequest> broadcasting = new BroadcastingFlagCommand(user, playerRepo,
                pf, uptime, client, futureTaskRegistry, 200, 50, 2, TimeSpan.FromMinutes(1));//5,3,2,30
            Command<IPrivRequest> time = new TimeCommand();
            Command<IPrivRequest> playlist = new PlaylistCommand("http://open.spotify.com/user/1251282601/playlist/2j1FVSjJ4zdJiqGQgXgW3t");
            Command<IPrivRequest> opinion = new OpinionCommand();
            Command<IPrivRequest> pun = new PunCommand();
            Command<IPrivRequest> quote = new QuoteCommand();
            Command<IPrivRequest> raffle = new RaffleCommand(client, 5, futureTaskRegistry);

            #endregion

            #region RPG Commands

            #region General

            Command<IWhisperRequest> stats = new StatsCommand(pf, playerRepo);
            Command<IWhisperRequest> inventory = new InventoryCommand(pf, playerRepo);
            Command<IWhisperRequest> item = new ItemCommand(equipmentRepository, pf, playerRepo);
            Command<IWhisperRequest> equip = new EquipCommand(equipmentRepository, pf, playerRepo);
            Command<IWhisperRequest> unequip = new UnequipCommand(equipmentRepository, pf,
                playerRepo);
            Command<IWhisperRequest> shop = new ShopCommand();
            Command<IWhisperRequest> classChoice = new ClassChoice(pf, playerRepo, 3);
            Command<IWhisperRequest> gloat = new GloatCommand(client, pf, playerRepo);
            Command<IWhisperRequest> respec = new RespecCommand(pf, playerRepo);
            Command<IWhisperRequest> daily = new DailyCommand(pf, playerRepo);
            Command<IWhisperRequest> queue = new QueueCommand(groupFinder, pf, playerRepo);
            Command<IWhisperRequest> leaveQueue = new LeaveQueueCommand(groupFinder, pf, playerRepo);
            Command<IWhisperRequest> queueTime = new QueueTimeCommand(groupFinder, pf, playerRepo);

            #endregion

            #region Party Commands

            Command<IWhisperRequest> createParty = new CreatePartyCommand(partyPool, pf,
                playerRepo);
            Command<IWhisperRequest> pendingInvite = new PendingInvite(partyPool, pf, playerRepo);
            Command<IWhisperRequest> leaveParty = new LeavePartyCommand(pf, playerRepo);

            #region Party Leader Commands

            Command<IWhisperRequest> partyAdd = new AddPartyCommand(client, pf, playerRepo);
            Command<IWhisperRequest> partyKick = new KickPartyCommand(client, pf, playerRepo);
            Command<IWhisperRequest> partyStart = new StartPartyCommand(groupFinder, pf,
                playerRepo);
            Command<IWhisperRequest> partyPromote = new PromotePartyCommand(client, pf,
                playerRepo);

            #endregion

            #endregion

            #region Broadcaster only

            Command<IWhisperRequest> addPlayerXp = new AddPlayerXP(pf, playerRepo);
            Command<IWhisperRequest> addPlayerCoin = new AddPlayerCoin(pf, playerRepo);
            Command<IWhisperRequest> setPlayerLevel = new SetPlayerLevel(pf, playerRepo);

            #endregion

            #endregion

            #endregion

            commandManager.AddAll(uptime, broadcasting, time, playlist, opinion, pun, quote,
                raffle);
            commandManager.AddAll(stats, inventory, item, equip, unequip, shop, classChoice,
                gloat, respec, daily, queue, leaveQueue, queueTime, createParty, pendingInvite,
                leaveParty, partyAdd, partyKick, partyStart, partyPromote,
                addPlayerXp, addPlayerCoin, setPlayerLevel);

            //Provide Handles for events raised by client, multiple handles can be added
            //allow for parsing of PRVMSG chat for mirroring certain messages.
            #region Client Event Handling

            client.AddOperationHandler += commandManager.Handle;
            client.CancelOperationHandler += commandManager.Handle;
            client.DeleteOperationHandler += commandManager.Handle;
            client.EditOperationHandler += commandManager.Handle;
            client.InfoOperationHandler += commandManager.Handle;

            client.PrivHandler += (o, e) =>
            {
                Console.WriteLine(string.Format("{0}: {1}", e.User, e.Message));
            };
            client.PrivRequestHandler += commandManager.Handle;

            client.WhisperHandler += (o, e) =>
            {
                Console.WriteLine(string.Format("Whisper {0}: {1}", e.User, e.Message));
            };
            client.WhisperRequestHandler += commandManager.Handle;



            #endregion

            //new thread for sending messages back to twitch server.
            new Thread(() =>
            {
                Thread.CurrentThread.Name = "Twitch Client";
                Thread.CurrentThread.IsBackground = true;
                client.Run();
            }).Start();



            futureTaskRegistry.Run();


        }
    }

}