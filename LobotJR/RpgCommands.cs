using Adventure;
using Client;
using Equipment;
using LobotJR;
using PartyGroup;
using Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TwitchMessages;

namespace Command
{

    public class BroadcastingFlagCommand : AbstractCommand<IPrivRequest>
    {
        private readonly string botName;
        private readonly IPlayerRepository playerRepository;
        private readonly IPlayerFactory playerFactory;
        private readonly UptimeCommand uptimeCommand;
        private readonly ITwitchClient client;
        private readonly FutureTaskRegistry registry;
        private ReoccurringScheduledTask broadcastingTask;
        private readonly int xp;
        private readonly int coins;
        private readonly int subMod;
        private readonly TimeSpan rewardDelay;
        private int count;

        public override bool IsCustom { get { return false; } }

        public BroadcastingFlagCommand(string botName, IPlayerRepository pr,
            IPlayerFactory pf, UptimeCommand utc, ITwitchClient client,
            FutureTaskRegistry registry, int xp, int coins, int subMod,
            TimeSpan rewardDelay)
            : base("broadcasting", 10, "broadcaster", "moderator")
        {
            this.botName = botName;
            this.playerRepository = pr;
            this.playerFactory = pf;
            this.uptimeCommand = utc;
            this.client = client;
            this.registry = registry;
            this.xp = xp;
            this.coins = coins;
            this.subMod = subMod;
            this.rewardDelay = rewardDelay;
        }

        public override string Cancel()
        {
            if (broadcastingTask != null)
            {
                broadcastingTask.Cancel(true);
                uptimeCommand.End();
            }

            return "Coins and XP will no longer be available.";
        }

        public override string ExecuteCommand(IPrivRequest request)
        {
            if (request.RequestValue.Equals("on", StringComparison.CurrentCultureIgnoreCase))
                return On(request.Channel);
            else
                return Cancel();
        }

        private string On(string channel)
        {
            if (broadcastingTask == null || broadcastingTask.IsDone())
            {
                uptimeCommand.SetStart(DateTime.Now);
                broadcastingTask = new ReoccurringScheduledTask(registry,
                    rewardDelay, () =>
                    {
                        int rewardmod = 1;
                        IDictionary<TwitchUtils.VIEWER_TYPE, ISet<string>> viewers =
                        TwitchUtils.FetchCurrentViewers(botName, channel);


                        foreach (var vt_users in viewers)
                            foreach (var user in vt_users.Value)
                            {
                                IPlayer player = playerRepository
                                    .GetByToken(PlayerToken.of(user));
                                if (player == null)
                                {
                                    player = playerFactory.Create(user);
                                    playerRepository.Save(player);
                                }
                                if (vt_users.Key == TwitchUtils.VIEWER_TYPE.SUB)
                                    rewardmod = subMod;
                                else
                                    rewardmod = 1;

                                player.AddCoins(coins * rewardmod);
                                player.AddXP(xp * rewardmod);
                            }
                        count++;
                        client.SendMessage(string.Format("Thanks for watching! Viewers " +
                            "awarded {0} XP & {1} Wolfcoins. Subscribers earn double that " +
                            "amount", xp, coins));
                    });
            }
            return string.Format("Wolfcoins & XP will be awarded.");
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
            sb.Append("Broadcasting flag command, this is linked to the rpg starting. ");
            sb.Append("the xp and coin award loop. ");
            sb.Append("Broadcasting command can be used like so, '!broadcasting on' and ");
            sb.Append("'!broadcasting off', setting the broadcast on and off respectively.");
            sb.Append("The stream is ");
            if (broadcastingTask != null)
            {
                if (!broadcastingTask.IsDone())
                {
                    sb.Append(" is currently broadcasting and viewers are rewarded every ");
                    sb.Append(rewardDelay.TotalMinutes);
                    sb.Append(" minutes. Viewers have been rewarded ");
                    sb.Append(count);
                    sb.Append(" this broadcast.");
                }
            }
            else
                sb.Append(" is not broadcasting.");

            return sb.ToString();
        }
    }

    public abstract class AbstractPlayerWhisperCommand : AbstractCommand<IWhisperRequest>
    {
        protected readonly IPlayerFactory playerFactory;
        protected readonly IPlayerRepository playerRepository;
        public override bool IsCustom { get { return false; } }

        public AbstractPlayerWhisperCommand(IPlayerFactory pf, IPlayerRepository pr, string name,
           int cooldown, int expArgs, params string[] users)
            : base(name, cooldown, users)
        {
            this.playerFactory = pf;
            this.playerRepository = pr;
        }

        public AbstractPlayerWhisperCommand(IPlayerFactory pf, IPlayerRepository pr, string name)
            : this(pf, pr, name, 0, 0)
        {
            this.playerFactory = pf;
            this.playerRepository = pr;
        }

        public override string Cancel() { return string.Empty; }

        public override string ExecuteCommand(IWhisperRequest request)
        {
            IPlayerToken token = PlayerToken.of(request.UserId, request.User);
            IPlayer player = playerRepository.GetByToken(token);
            if (player == null)
            {
                player = playerFactory.Create(token.ID, token.Name);
                playerRepository.Save(player);
            }

            return Execute(player, request);
        }

        public abstract string Execute(IPlayer player, IWhisperRequest request);

    }

    public class StatsCommand : AbstractPlayerWhisperCommand
    {

        public StatsCommand(IPlayerFactory pf, IPlayerRepository pr) :
            base(pf, pr, "stats")
        {
        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {
            if (string.IsNullOrEmpty(request.RequestValue))
                return CurrentPlayerStats(player);
            else
                return OtherPlayerStats(request.RequestValue, player);

        }

        private string CurrentPlayerStats(IPlayer player)
        {
            StringBuilder sb = new StringBuilder("You currently have ");
            sb.AppendFormat("{0} coins. ", player.Coins);
            sb.Append(GetFormattedPlayerLevelStats(player));
            sb.AppendFormat("(Total XP: {0} | XP To Next Level: {1})",
                player.CharClass.PlayerLevelInfo.XP,
                player.CharClass.PlayerLevelInfo.TNL());
            return sb.ToString();
        }

        private string OtherPlayerStats(string otherPlayerName, IPlayer requestee)
        {
            IPlayer player = playerRepository.GetByToken(PlayerToken.of(otherPlayerName));
            if (player == null)
                return "No player with that name exists! No coins have been removed";
            requestee.AddCoins(-1);
            StringBuilder sb = new StringBuilder("1 Coin has been removed for ");
            sb.AppendFormat("{0}'s stats: ", player.Name);
            sb.Append(GetFormattedPlayerLevelStats(player));
            sb.AppendFormat(", and has {0} coins.", player.Coins);
            return sb.ToString();
        }

        private string GetFormattedPlayerLevelStats(IPlayer player)
        {
            return string.Format("{0} is a Level {1} {2} and is Prestige Level {3}",
                player.Name,
                player.Level, player.CharClass.ClassName, player.CharClass.Prestige);
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for retrieving stats for player.");
            return sb.ToString();
        }
    }

    public class InventoryCommand : AbstractPlayerWhisperCommand
    {
        public override bool IsCustom { get { return false; } }

        public InventoryCommand(IPlayerFactory pf, IPlayerRepository pr) :
            base(pf, pr, "inventory")
        {
        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {

            StringBuilder sb = new StringBuilder();

            if (player.Items.Count == 0)
                sb.Append("You have no items in your inventory!");
            else
            {
                sb.Append("You currently have these items in your iventory: ");
                foreach (var item in player.Items)
                {
                    sb.Append(item.ID);
                    sb.Append(" ");
                    sb.Append(item.Name);
                    sb.Append(" ");
                }

            }

            sb.Append("and you have these items equipped: ");
            sb.Append(player.Equipped.ToString());

            return sb.ToString();

        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for retrieving player items.");
            return sb.ToString();
        }
    }

    public class ItemCommand : AbstractPlayerWhisperCommand
    {
        private readonly IEquipmentRepository equipmentRepository;

        public ItemCommand(IEquipmentRepository er, IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "item")
        {
            this.equipmentRepository = er;
        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {
            long id;
            if (long.TryParse(request.RequestValue.Trim(), out id))
            {
                IEquipment equipment = equipmentRepository.getById(id);
                StringBuilder sb = new StringBuilder(string.Format("{0} ({1}) {2} Bonus: {3} " +
                    "Success Chance {4} Item Find " +
                    "{5} Coin Bonus {6} Xp Bonuse {7} Prevent Death Bonus For: ", equipment.Name,
                    equipment.EquipSlot.Name, equipment.Description, equipment.SuccessChance,
                    equipment.ItemFind, equipment.CoinBonus, equipment.XpBonus,
                    equipment.PreventDeathBonus));
                foreach (var c in equipment.ForClasses)
                {
                    sb.Append(c.NameShort);
                    sb.Append(" ");
                }
                return sb.ToString();
            }

            return "No Item exists with that ID!";


        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for retrieving item information.");
            return sb.ToString();
        }
    }

    public class EquipCommand : AbstractPlayerWhisperCommand
    {
        private readonly IEquipmentRepository equipmentRepository;

        public EquipCommand(IEquipmentRepository er, IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "equip")
        {
            this.equipmentRepository = er;
        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {
            long id;
            if (long.TryParse(request.RequestValue, out id))
            {
                IEquipment equipment = equipmentRepository.getById(id);
                if (player.HasItem(equipment))
                {
                    var removed = player.Equipped.Equip(equipment);
                    StringBuilder sb = new StringBuilder(string
                        .Format("{0} has been equipped in the {1} slot.", equipment.Name,
                        equipment.EquipSlot.Name));
                    if (removed != null)
                        sb.Append(string.Format("{0} has been unequipped and returned to your " +
                            "inventory"));
                }
                return string.Format("You cannot equip {0} as you do not have this item.",
                    equipment.Name);
            }
            return string.Format("No item exists with that ID!");
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for equipping items.");
            return sb.ToString();
        }
    }

    public class UnequipCommand : AbstractPlayerWhisperCommand
    {
        private readonly IEquipmentRepository equipmentRepository;

        public UnequipCommand(IEquipmentRepository er, IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "unequip")
        {
            this.equipmentRepository = er;
        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {
            EquipSlot equipSlot = null;

            if (Regex.IsMatch(request.RequestValue, "\\d+"))
            {
                long id = long.Parse(request.RequestValue);
                IEquipment equipment = equipmentRepository.getById(id);

                if (player.HasItem(equipment))
                    equipSlot = equipment.EquipSlot;
            }
            else
            {

                equipSlot = player.Equipped.GetSlots().SingleOrDefault(slot =>
                {
                    return (slot.Name.Equals(request.RequestValue,
                        StringComparison.CurrentCultureIgnoreCase));
                });
            }

            if (equipSlot != null)
            {
                var unequipped = player.Equipped.Unequip(equipSlot);
                if (unequipped != null)
                    return string.Format("{0} has been unequipped from the {1} slot.",
                        unequipped.Name, unequipped.EquipSlot.Name);
            }
            return string.Empty;
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for unequipping items.");
            return sb.ToString();
        }
    }

    public class ShopCommand : AbstractCommand<IWhisperRequest>
    {
        public override bool IsCustom { get { return false; } }

        public ShopCommand() : base("shop")
        {

        }

        public override string Cancel() { return string.Empty; }

        public override string ExecuteCommand(IWhisperRequest request)
        {
            return "Whisper me '!stats <username>' to check another users stats!" +
                " (Cost: 1 coin) Whisper me '!gloat' to spend 10 coins and show off" +
                " your level! (Cost: 10 coins)";
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for showing shop commands.");
            return sb.ToString();
        }
    }

    public class ClassChoice : AbstractPlayerWhisperCommand
    {
        private readonly int classChoiceLevel;
        public ClassChoice(IPlayerFactory pf, IPlayerRepository pr, int classChoiceLevel)
            : base(pf, pr, "class")
        {
            this.classChoiceLevel = classChoiceLevel;
        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {
            if (!(player.Level == classChoiceLevel && player.CharClass.CharClassType.ID == 0))
                return string.Empty;

            if (string.IsNullOrWhiteSpace(request.RequestValue))
                return ChoiceOfClasses();

            CharClassType cct = playerFactory.GetCharClassTypes().SingleOrDefault(c =>
            {
                return (c.Name.Equals(request.RequestValue,
                    StringComparison.CurrentCultureIgnoreCase) ||
                    c.NameShort.Equals(request.RequestValue,
                    StringComparison.CurrentCultureIgnoreCase));
            });

            if (cct == null)
                return string.Empty;

            player = playerFactory.Create(player, cct);
            playerRepository.Save(player);

            return string.Format("You have successfully choosen the {0} class! You are " +
                "now eligiable for dungeons!", cct.Name);

        }

        private string ChoiceOfClasses()
        {
            StringBuilder sb = new StringBuilder("The current class choices available are: ");
            foreach (var cct in playerFactory.GetCharClassTypes())
                sb.AppendFormat("{0}, ", cct.Name);
            sb.Remove(sb.Length - 2, 1);
            sb.Append(".");
            return sb.ToString();
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for choosing character's class. ");
            sb.Append("!class will return a list of the available classes to choose from. ");
            sb.Append("!class [class name] is then used to confirm a choice of class. ");
            sb.Append("Once confirmation has been sent there is no way to stop or cancel. ");
            sb.Append("The only way to change class after this point is to !respec, ");
            sb.Append("which is quite costly.");
            return sb.ToString();
        }
    }

    public class GloatCommand : AbstractPlayerWhisperCommand
    {
        private readonly ITwitchClient client;
        public GloatCommand(ITwitchClient client, IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "gloat")
        {
            this.client = client;
        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {
            int level = player.CharClass.Level;
            string gloat = null;
            #region Switch Gloat Messages
            switch (level)
            {
                case 1:
                    gloat = "Just a baby! lobosMindBlank";
                    break;
                case 2:
                    gloat = "Scrubtastic!";
                    break;
                case 3:
                    gloat = "Pretty weak!";
                    break;
                case 4:
                    gloat = "Not too shabby.";
                    break;
                case 5:
                    gloat = "They can hold their own!";
                    break;
                case 6:
                    gloat = "Getting pretty strong Kreygasm";
                    break;
                case 7:
                    gloat = "A formidable opponent!";
                    break;
                case 8:
                    gloat = "A worthy adversary!";
                    break;
                case 9:
                    gloat = "A most powerful combatant!";
                    break;
                case 10:
                    gloat = "A seasoned war veteran!";
                    break;
                case 11:
                    gloat = "A fearsome champion of the Wolfpack!";
                    break;
                case 12:
                    gloat = "A vicious pack leader!";
                    break;
                case 13:
                    gloat = "A famed Wolfpack Captain!";
                    break;
                case 14:
                    gloat = "A brutal commander of the Wolfpack!";
                    break;
                case 15:
                    gloat = "Decorated Chieftain of the Wolfpack!";
                    break;
                case 16:
                    gloat = "A War Chieftain of the Wolfpack!";
                    break;
                case 17:
                    gloat = "A sacred Wolfpack Justicar!";
                    break;
                case 18:
                    gloat = "Demigod of the Wolfpack!";
                    break;
                case 19:
                    gloat = "A legendary Wolfpack demigod veteran!";
                    break;
                case 20:
                    gloat = "The Ultimate Wolfpack God Rank. A truly dedicated individual.";
                    break;
                default: return "";
            }
            #endregion

            if (player.Coins < 10)
                return "You do not have 10 coins to spend!";
            client.SendMessage(gloat);
            player.AddCoins(-10);
            return "10 coins have been removed.";
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for gloat messages. A gload message is ");
            sb.Append("displayed to public chat at a cost of 10 coins");
            return sb.ToString();
        }
    }

    public class RespecCommand : AbstractPlayerWhisperCommand
    {
        private readonly IDictionary<IPlayer, CharClassType> respecOutstanding;

        public RespecCommand(IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "respec")
        {
            this.respecOutstanding = new Dictionary<IPlayer, CharClassType>();
        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {
            if (respecOutstanding.ContainsKey(player))
                if (request.RequestValue.Equals("yes", StringComparison.CurrentCultureIgnoreCase))
                {
                    //respec
                    player = playerFactory.Create(player, respecOutstanding[player]);
                    int cor = CostOfRespec(player);
                    player.AddCoins(cor * -1);
                    return string.Format("You have successfully changed classes!" +
                        " You are now a {0}! Your change has cost you {1} wolfcoins " +
                        "and all your items!", player.CharClass.ClassName, cor);
                }
                else if (request.RequestValue.Equals("no",
                    StringComparison.CurrentCultureIgnoreCase))
                {
                    respecOutstanding.Remove(player);
                    return "Respec has been cancelled, no coins have been used.";
                }

            int cost = CostOfRespec(player);
            if (player.Coins < cost)
                return string.Format("You do not have enough coins to respec! The cost to " +
                    "respec is currently {0} for your character. You currently only have " +
                    "{1} coins", cost, player.Coins);

            CharClassType cct = CharClassConverter.LEGACY_CLASS_TYPE.Values
                .SingleOrDefault(type =>
                {
                    return (type.Name.Equals(request.RequestValue,
                        StringComparison.CurrentCultureIgnoreCase) ||
                        type.NameShort.Equals(request.RequestValue,
                        StringComparison.CurrentCultureIgnoreCase));
                });
            if (cct != null)
            {
                respecOutstanding.Add(player, cct);

                return string.Format("To Respec your current class to {0} would cost {1}. Please " +
                        "note you will retain your level and experience but will lose any" +
                        " items you currently have! If you'd like to continue whisper back" +
                        " !respec yes, otherwise whisper back !respec no.", cct.NameShort,
                        cost);
            }
            return string.Empty;
        }

        private int CostOfRespec(IPlayer player)
        {
            return player.CharClass.Level <= 5 ? 250 : (player.CharClass.Level - 4) * 250;
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for respec of characters. ");
            sb.Append("!respec <CharClass> will initiate the respec process where the player is ");
            sb.Append("advised of the cost and result of respec.");
            sb.Append("The player is then prompted to either accept or decline by sending ");
            sb.Append("!respec yes or !respec no, respectively.");
            return sb.ToString();
        }
    }

    public class DailyCommand : AbstractPlayerWhisperCommand
    {

        public DailyCommand(IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "daily")
        {

        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {
            TimeSpan fromLast = DateTime.Now.Subtract(player.LastDailyGroupFinder);
            if (fromLast.CompareTo(TimeSpan.FromDays(1)) == -1)
            {
                TimeSpan remaining = TimeSpan.FromDays(1).Subtract(fromLast);
                return string.Format("{0}:{1} until your next daily bonus!",
                    remaining.Hours, remaining.Minutes);
            }
            return "Your daily bonus is ready!";

        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for checking daily bonus reward timer");
            return sb.ToString();
        }
    }

    public class QueueCommand : AbstractPlayerWhisperCommand
    {
        private readonly IGroupFinder groupFinder;

        public QueueCommand(IGroupFinder gf, IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "queue")
        {
            this.groupFinder = gf;
        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {

            if (player.InParty)
                if (PartyUtils.IsLeader(player))
                    return ExecutePartyQueue(player, request);
                else
                    return "You need to be the party leader to queue the party for dungeons.";

            return ExecutePlayerQueue(player, request);
        }

        private string ExecutePartyQueue(IPlayer partyLeader, IWhisperRequest request)
        {
            if (request.RequestValue.Trim().Equals(string.Empty))
                return groupFinder.Queue(partyLeader.Party);

            int[] ids = ParseIdsForDungeonsFromRequestValue(request.RequestValue);
            return groupFinder.Queue(partyLeader, ids);

        }

        private string ExecutePlayerQueue(IPlayer player, IWhisperRequest request)
        {
            string resultingMessage = "";

            if (request.RequestValue.Trim().Equals(string.Empty))
                resultingMessage = groupFinder.Queue(player);
            else
            {
                int[] ids = ParseIdsForDungeonsFromRequestValue(request.RequestValue);
                resultingMessage = groupFinder.Queue(player, ids);
            }
            return resultingMessage;
        }

        private int[] ParseIdsForDungeonsFromRequestValue(string requestValue)
        {
            string[] string_ids = requestValue.Split(',');
            IList<int> ids = new List<int>();
            foreach (var str in string_ids)
                ids.Add(int.Parse(str));
            return ids.ToArray();
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for joining the Group Finder queue. Queue can ");
            sb.Append("be used by a single member or a party leader with the option of ");
            sb.Append("supplying a list of id's for wanted dungeons. Queue will make sure ");
            sb.Append("that players adhere to the dungeon requirements. Use !start command ");
            sb.Append("in a party to ignore dungeon requirements. The Group Finder will ");
            sb.Append("apply neccessary Level syncs if this allows the party to run a dungeon.");
            return sb.ToString();
        }
    }

    public class LeaveQueueCommand : AbstractPlayerWhisperCommand
    {
        private readonly IGroupFinder groupFinder;

        public LeaveQueueCommand(IGroupFinder groupFinder, IPlayerFactory pf,
            IPlayerRepository pr)
            : base(pf, pr, "leavequeue")
        {
            this.groupFinder = groupFinder;
        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {
            return groupFinder.UnQueue(player);
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for leaving the Group Finder queue.");
            return sb.ToString();
        }
    }

    public class QueueTimeCommand : AbstractPlayerWhisperCommand
    {

        private readonly IGroupFinder groupFinder;

        public QueueTimeCommand(IGroupFinder gf, IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "queuetime")
        {
            this.groupFinder = gf;
        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {
            return groupFinder.Waiting(player);
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for Group Finder waiting time.");
            return sb.ToString();
        }
    }

    public class CreatePartyCommand : AbstractPlayerWhisperCommand
    {
        private readonly IPartyPool partyPool;

        public CreatePartyCommand(IPartyPool pp, IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "createparty")
        {
            this.partyPool = pp;
        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {
            if (player.InParty)
                return "You are already in a party! Use !Leave command to leave then " +
                    "create a new party.";

            partyPool.Create(player, 3);

            return string.Format("Party created you can invite {0} other members using !add <user>",
                2);
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for creating a new party.");
            return sb.ToString();
        }
    }

    public class PendingInvite : AbstractPlayerWhisperCommand
    {

        private readonly IPartyPool partyPool;

        public PendingInvite(IPartyPool pp, IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "pendinginvite")
        {
            this.partyPool = pp;
        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {
            if (player.InParty)
                return string.Empty;

            if (partyPool.HasInvite(player))
                return "You do not have an outstanding invite to a party!";

            if (request.RequestValue.Equals("accept", StringComparison.CurrentCultureIgnoreCase))
                return AcceptInvite(player);
            else if (request.RequestValue.Equals("decline",
                StringComparison.CurrentCultureIgnoreCase))
                return DeclineInvite(player);
            else
                return string.Empty;
        }

        private string AcceptInvite(IPlayer player)
        {
            if (partyPool.AcceptInvite(player))
                partyPool.Client.Whisper(player.Party, string.Format("{0} has joined the party",
                    player.Name));
            return "You were unable to join the party! The party may have reached max capacity!";
        }

        private string DeclineInvite(IPlayer player)
        {
            partyPool.DeclineInvite(player);
            return "You have declined the party invite!";
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for accepting or declining party invites.");
            return sb.ToString();
        }
    }

    public class LeavePartyCommand : AbstractPlayerWhisperCommand
    {

        public LeavePartyCommand(IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "leaveparty")
        {

        }

        public override string Execute(IPlayer player, IWhisperRequest request)
        {
            if (player.InParty)
            {
                player.Unregister();
                return "You have left the party!";
            }
            return "You are not in a party!";
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for leaving a party.");
            return sb.ToString();
        }
    }

    public abstract class AbstractPartyLeaderCommand : AbstractCommand<IWhisperRequest>
    {
        protected readonly IPlayerFactory playerFactory;
        protected readonly IPlayerRepository playerRepository;

        public override bool IsCustom { get { return false; } }

        public AbstractPartyLeaderCommand(IPlayerFactory pf, IPlayerRepository pr, string name)
            : base(name)
        {
            this.playerFactory = pf;
            this.playerRepository = pr;
        }

        public override string Cancel() { return string.Empty; }

        public override string ExecuteCommand(IWhisperRequest request)
        {
            IPlayerToken token = PlayerToken.of(request.UserId, request.User);
            IPlayer player = playerRepository.GetByToken(token);

            if (player == null)
            {
                player = playerFactory.Create(token.ID, token.Name);
                playerRepository.Save(player);
            }

            if (!(player.InParty && player.Party.PartyLeader.Equals(player)))
                return string.Format("You have to be in a party and be the party leader to use " +
                    "{0} command!", request.RequestName);

            return Execute(player, request);
        }

        protected abstract string Execute(IPlayer partyLeader, IWhisperRequest request);

    }

    public class AddPartyCommand : AbstractPartyLeaderCommand
    {

        private readonly ITwitchClient client;

        public AddPartyCommand(ITwitchClient client, IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "add")
        {
            this.client = client;
        }

        protected override string Execute(IPlayer player, IWhisperRequest request)
        {

            if (player.Party.IsReady)
                return "You are unable to send an invite with a full party!";

            IPlayer toAdd = playerRepository.GetByToken(PlayerToken.of(request.RequestValue));
            if (toAdd == null)
                return string.Format("{0} user could not be found!", request.RequestValue);

            if (toAdd.InParty)
                return string.Format("{0} is already in a party!", toAdd.Name);

            player.Party.Invited(toAdd);
            client.Whisper(toAdd.Name, string.Format("You have been invited to a party by {0}. " +
                "Use '!party accept' or '!party decline' to accept or decline the invite " +
                "respectively"));
            return string.Format("You have invited {0} to your party!", toAdd.Name);


        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for party leaders to invite other players to their ");
            sb.Append("party. This command is available to the users above, however any player ");
            sb.Append("that is not a party leader will recieve an error message.");
            return sb.ToString();
        }
    }

    public class KickPartyCommand : AbstractPartyLeaderCommand
    {
        private readonly ITwitchClient client;

        public KickPartyCommand(ITwitchClient client, IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "kick")
        {
            this.client = client;
        }

        protected override string Execute(IPlayer partyLeader, IWhisperRequest request)
        {
            IPlayerToken token = PlayerToken.of(request.RequestValue);
            IPlayer toKick = playerRepository.GetByToken(token);

            if (toKick == null)
                return string.Format("{0} user could not be found!", request.RequestValue);

            if (!(toKick.InParty && toKick.Party.Equals(partyLeader.Party)))
                return string.Format("You cannot kick {0} as they are not in your party!",
                    toKick.Name);

            partyLeader.Party.Remove(toKick);
            client.Whisper(toKick.Name, "You have been kicked from the party!");
            return string.Format("You have kicked {0} from the party!", toKick.Name);
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for party leaders to kick other players from their ");
            sb.Append("parties. This command is available to the users above, however any player ");
            sb.Append("that is not a party leader will recieve an error message.");
            return sb.ToString();
        }


    }

    public class StartPartyCommand : AbstractPartyLeaderCommand
    {
        private readonly IGroupFinder groupFinder;

        public StartPartyCommand(IGroupFinder gf, IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "start")
        {
            this.groupFinder = gf;
        }

        protected override string Execute(IPlayer partyLeader, IWhisperRequest request)
        {
            if (request.RequestValue.Equals(string.Empty))
                return groupFinder.QueueIgnoringRequirements(partyLeader.Party);

            int[] ids = ParseIdsForDungeonsFromRequestValue(request.RequestValue);
            return groupFinder.QueueIgnoringRequirements(partyLeader.Party, ids);
        }

        private int[] ParseIdsForDungeonsFromRequestValue(string requestValue)
        {
            string[] string_ids = requestValue.Split(',');
            IList<int> ids = new List<int>();
            foreach (var str in string_ids)
                ids.Add(int.Parse(str));
            return ids.ToArray();
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for joining the Group Finder with a pre-made");
            sb.Append("party. This will ignore the requirements of dungeons, apart from ");
            sb.Append("the minimum level requirement. This allows higher levels to solo/duo ");
            sb.Append("dungeons with the full benefit of their gear. Use !Queue command ");
            sb.Append("for normal leveling.");
            return sb.ToString();
        }
    }

    public class PromotePartyCommand : AbstractPartyLeaderCommand
    {
        private readonly ITwitchClient client;

        public PromotePartyCommand(ITwitchClient client, IPlayerFactory pf,
            IPlayerRepository pr)
            : base(pf, pr, "promote")
        {
            this.client = client;
        }

        protected override string Execute(IPlayer partyLeader, IWhisperRequest request)
        {
            IPlayerToken token = PlayerToken.of(request.RequestValue);
            IPlayer toPromote = playerRepository.GetByToken(token);

            if (toPromote == null)
                return string.Format("{0} user could not be found!", request.RequestValue);

            if (!(toPromote.InParty && toPromote.Party.Equals(partyLeader.Party)))
                return string.Format("You cannot promote {0} as they are not in your party!",
                    toPromote.Name);

            partyLeader.Party.PartyLeader = toPromote;
            client.Whisper(toPromote.Name, "You have been promoted to leader of the party!");
            return string.Format("You have promoted {0} to leader of the party!",
                toPromote.Name);
        }

        public override string Info()
        {
            StringBuilder sb = new StringBuilder("Command: ");
            sb.Append(Name);
            sb.Append(" Not custom and cannot be edited in chat.");
            sb.Append(" WHISPER only. ");
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
            sb.Append("Wolfpack Rpg command for party leaders to promote other another player ");
            sb.Append("to the leader of the party. This command is available to the users ");
            sb.Append("above, however any player that is not a party leader will recieve ");
            sb.Append("an error message.");
            return sb.ToString();
        }


    }

    public abstract class BroadcasterSetPlayerCommand : AbstractCommand<IWhisperRequest>
    {

        protected readonly IPlayerFactory playerFactory;
        protected readonly IPlayerRepository playerRepository;
        public override bool IsCustom { get { return false; } }

        public BroadcasterSetPlayerCommand(IPlayerFactory pf, IPlayerRepository pr,
            string name)
            : base(name, 1, "broadcaster")
        {
            this.playerFactory = pf;
            this.playerRepository = pr;
        }

        public override string Cancel()
        { return string.Empty; }

        public override string ExecuteCommand(IWhisperRequest request)
        {
            Regex regex = new Regex("(\\w*) (\\w*)");
            MatchCollection mc = regex.Matches(request.RequestValue);

            if (mc.Count == 1)
            {
                GroupCollection gc = mc[0].Groups;
                string name = gc[1].Value;
                string value = gc[2].Value;

                IPlayerToken token = PlayerToken.of(name);
                IPlayer player = playerRepository.GetByToken(token);
                if (player != null && !string.IsNullOrEmpty(value))
                {
                    return ExecuteCommand(request, player, value);
                }
            }

            return "Set player command either cannot find the player based on the name, or " +
                "the syntax for the command is incorrect. All set player commands follow the " +
                "syntax of ![commandName] [playerName] [value].";
        }

        public abstract string ExecuteCommand(IWhisperRequest request, IPlayer player,
            string value);
    }

    public class AddPlayerXP : BroadcasterSetPlayerCommand
    {
        public AddPlayerXP(IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "addplayerxp")
        {
        }

        public override string ExecuteCommand(IWhisperRequest request, IPlayer player,
            string value)
        {
            int xp = -1;
            if (int.TryParse(value, out xp))
            {
                player.AddXP(xp);

                return string.Format("{0} XP has been added to the player {1}.",
                    xp, player.Name);
            }
            return string.Format("Only integer numerics can be used for changing player xp");
        }

        public override string Info()
        {
            return string.Empty;
        }
    }

    public class AddPlayerCoin : BroadcasterSetPlayerCommand
    {
        public AddPlayerCoin(IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "addplayercoin")
        {
        }

        public override string ExecuteCommand(IWhisperRequest request, IPlayer player, string value)
        {
            int coins = -1;
            if (int.TryParse(value, out coins))
            {
                player.AddCoins(coins);

                return string.Format("{0} coins has been added to the player {1}. ",
                    coins, player.Name);
            }
            return string.Format("Only integer numerics can be used for adding coins");
        }

        public override string Info()
        { return string.Empty; }
    }

    public class SetPlayerLevel : BroadcasterSetPlayerCommand
    {
        public SetPlayerLevel(IPlayerFactory pf, IPlayerRepository pr)
            : base(pf, pr, "setplayerlevel")
        {
        }

        public override string ExecuteCommand(IWhisperRequest request, IPlayer player,
            string value)
        {
            int level = -1;
            if (int.TryParse(value, out level) && level > 0)
            {
                int xp = player.CharClass.PlayerLevelInfo.XPForLevel(level);
                xp -= player.CharClass.PlayerLevelInfo.XP;
                player.AddXP(xp);

                return string.Format("{0} XP has been added to the player {1}. ",
                    xp, player.Name);
            }
            return string.Format("Only integer numerics can be used for changing player level");
        }

        public override string Info()
        {
            throw new NotImplementedException();
        }
    }


}