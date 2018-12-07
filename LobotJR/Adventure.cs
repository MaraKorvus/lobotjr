using Player;
using Status;
using System;
using System.Collections.Generic;
using System.Linq;
using Equipment;
using Client;
using System.Text;
using PartyGroup;
using System.Collections.Concurrent;

namespace Adventure
{
    #region GroupFinder 

    public interface IGroupFinder
    {
        string Queue(IPlayer player);
        string Queue(IPlayer player, params int[] adventures);
        string Queue(IParty party);
        string Queue(IParty party, params int[] adventures);
        string QueueIgnoringRequirements(IParty party);
        string QueueIgnoringRequirements(IParty party, int[] adventures);
        bool IsQueued(IPlayer player);
        string UnQueue(IPlayer player);
        string Waiting(IPlayer player);
    }

    class GroupFinder : IGroupFinder
    {
        private readonly BlockingCollection<KeyValuePair<IParty, IAdventure>> adventureQueue;
        private readonly SortedList<DateTime, AdventureTicket> tickets;
        private readonly IList<TimeSpan> waitTimes;
        private readonly IAdventureRepository repository;
        private readonly IPartyPool pool;
        private readonly int partySize;

        public GroupFinder(BlockingCollection<KeyValuePair<IParty, IAdventure>> adventureQueue,
            IPartyPool pool, int partyCapacity, IAdventureRepository repo)
        {
            this.adventureQueue = adventureQueue;
            this.tickets = new SortedList<DateTime, AdventureTicket>();
            this.waitTimes = new List<TimeSpan>();
            this.repository = repo;
            this.partySize = partyCapacity;
            this.pool = pool;
        }

        private TimeSpan GetAverageWaitTime()
        {
            TimeSpan ts = TimeSpan.Zero;

            foreach (var span in waitTimes)
                ts = span.Add(ts);
            int waitCount = waitTimes.Count == 0 ? 1 : waitTimes.Count;
            return TimeSpan.FromSeconds(ts.Seconds / waitCount);
        }

        private void TryToBuildParty()
        {
            TryToBuildPartyFromNextTicket(tickets.First().Value);
        }

        private void TryToBuildPartyFromNextTicket(AdventureTicket nextTicket)
        {
            if (nextTicket.Players.Count == partySize)
                FormAdventurePartyAndRemoveTickets(nextTicket.Adventures.First(), nextTicket);

            List<AdventureTicket> matches = new List<AdventureTicket>();

            foreach (var adventure in nextTicket.Adventures)
            {
                foreach (var at in tickets.Values)
                {
                    bool result = at.Equals(nextTicket);
                    if (!at.Equals(nextTicket) &&
                        at.Adventures.Contains(adventure) &&
                        !nextTicket.IsDuplicateCharClassType(at.Players))
                        matches.Add(at);
                }

                int match = 0;
                foreach (var m in matches)
                    match += m.Players.Count;

                if (match >= partySize - nextTicket.Players.Count)
                {
                    matches.Add(nextTicket);
                    FormAdventurePartyAndRemoveTickets(adventure, matches.ToArray());
                }
            }
        }

        private void FormAdventurePartyAndRemoveTickets(IAdventure adventure,
            params AdventureTicket[] partyTickets)
        {
            IList<IPlayer> players = new List<IPlayer>();

            foreach (var ticket in partyTickets)
            {
                foreach (var p in ticket.Players)
                    players.Add(p);
                tickets.Remove(ticket.TimeIn);
                waitTimes.Add(TimeSpan.FromSeconds(DateTime.Now.Second / ticket.TimeIn.Second));
            }
            IParty party = pool.Create(players);
            adventureQueue.Add(new KeyValuePair<IParty, IAdventure>(party, adventure));
        }

        public string Queue(IPlayer player)
        {
            if (IsQueued(player))
                return "You are already queued..." + Waiting(player);

            IList<IAdventure> adventures = new List<IAdventure>(repository
                .GetByLevel(player.CharClass.Level)
                .Where(AdventureUtils.GetGeneralRequirementsPredicate(player)));

            if (adventures.Count == 0)
                return "You do not meet the level requirements needed to run a dungeon!";

            return Queue(player, adventures);
        }

        public string Queue(IPlayer player, params int[] adventures)
        {
            if (IsQueued(player))
                return "You are already queued..." + Waiting(player);

            IList<IAdventure> reqAdventures = new List<IAdventure>();
            foreach (var id in adventures)
                reqAdventures.Add(repository.GetById(id));

            return Queue(player, reqAdventures);

        }

        private string Queue(IPlayer player, IList<IAdventure> adventures)
        {
            var ticket = new AdventureTicket(adventures, player);
            tickets.Add(ticket.TimeIn, ticket);
            TryToBuildParty();
            return string.Format("You have joined the group finder! The estimated " +
                "waiting time is {0} mins", GetAverageWaitTime().Minutes);
        }

        public string QueueIgnoringRequirements(IParty party)
        {
            Unqueue(party);
            int minLevel = PartyUtils.GetLowestLevelPlayer(party).Level;

            IList<IAdventure> adventures = repository
                .Get(a => {
                    return (a.MinimumLevel <= minLevel &&
            AdventureUtils.GetCostRequirementPredicate(party).Invoke(a));
                });
            if (adventures.Count == 0)
                return "A member in your party is either too low a level or cannot afford, " +
                    "and dungeons!";

            Random rnd = new Random();
            IAdventure adventure = adventures[rnd.Next(0, adventures.Count - 1)];

            adventureQueue.Add(new KeyValuePair<IParty, IAdventure>(party, adventure));
            return string.Format("Your party has joined the adventure queue for {0}, which " +
                "will start shortly. Maximum level requirements have been ignored, Xp is capped at " +
                "the adventures maximum level ({1}). ",
                adventure.Name, adventure.MaximumLevel);

        }

        public string QueueIgnoringRequirements(IParty party, int[] ids)
        {
            Unqueue(party);
            int minLevel = PartyUtils.GetLowestLevelPlayer(party).Level;

            IList<IAdventure> adventures = repository.Get(a => {
                return (ids.Contains(a.ID) && a.MinimumLevel < minLevel);
            });

            if (adventures.Count == 0)
                return "A member in your party is too low a level to Queue.";

            Random rnd = new Random();
            IAdventure adventure = adventures[rnd.Next(0, adventures.Count - 1)];

            adventureQueue.Add(new KeyValuePair<IParty, IAdventure>(party, adventure));
            return string.Format("Your party has joined the adventure queue for {0}, which " +
                "will start shortly. Maximum level requirements have been ignored.",
                adventure.Name);

        }

        public string Queue(IParty party)
        {
            Unqueue(party);

            StringBuilder sb = new StringBuilder();
            if (!party.IsSyncd)
                sb.Append(AutoLevelSyncParty(party));

            var adventures = new List<IAdventure>((repository
                .GetByLevel(party.Level)
                .Where(AdventureUtils.GetCostRequirementPredicate(party))));

            if (adventures.Count != 0)
                sb.Append(Queue(party, adventures));
            else
            {
                sb.Append("The party does not match the requirements for any dungeons ");
                sb.Append("currently available!");
            }

            return sb.ToString();
        }

        public string Queue(IParty party, params int[] ids)
        {
            Unqueue(party);

            StringBuilder sb = new StringBuilder();

            if (!party.IsSyncd)
                sb.Append(AutoLevelSyncParty(party));

            IList<IAdventure> adventures = repository.Get(a => {
                return (ids.Contains(a.ID) &&
                AdventureUtils.GetAllRequirementsPredicate(party).Invoke(a));
            });

            if (adventures.Count != 0)
                sb.Append(Queue(party, adventures));
            else
            {
                sb.Append("The party does not match the requirements for any dungeons ");
                sb.Append("currently available!");
            }

            return sb.ToString();
        }

        private string Queue(IParty party, IList<IAdventure> adventures)
        {
            if (adventures.Any(AdventureUtils.GetPartySizeRequirementPredicate(party)))
            {
                IEnumerable<IAdventure> matches = adventures
                    .Where(AdventureUtils.GetPartySizeRequirementPredicate(party));
                Random rnd = new Random();
                IAdventure adventure = matches.ElementAt(rnd.Next(0, matches.Count()));
                adventureQueue.Add(new KeyValuePair<IParty, IAdventure>(party, adventure));
                return string.Format("Your party has joined the adventure queue for {0}, which " +
                    "will start shortly.", adventure.Name);
            }

            var ticket = new AdventureTicket(adventures, party.Players.ToArray());
            tickets.Add(ticket.TimeIn, ticket);
            TryToBuildParty();
            return string.Format("You have joined the group finder! The estimated " +
                "waiting time is {0} mins", GetAverageWaitTime().Minutes);


        }

        private string AutoLevelSyncParty(IParty party)
        {
            StringBuilder sb = new StringBuilder();
            IPlayer sync = PartyUtils.GetLowestLevelPlayer(party);
            party.LevelSync(sync.Level);
            sb.AppendFormat("Automatically Level syncing to lowest level player ({0}) ",
                sync.Name);
            sb.Append("and finding dungeons based on this level. ");

            return sb.ToString();
        }

        public KeyValuePair<IParty, IAdventure> Take() { return adventureQueue.Take(); }

        public bool IsQueued(IPlayer player)
        {
            return tickets.Any(t =>
            {
                return t.Value.Players.Contains(player);
            });
        }

        public string UnQueue(IPlayer player)
        {
            if (!IsQueued(player))
                return "You are not currently queued for any Adventures in the Group Finder, " +
                    "Use !Queue command to join Group Finder";

            var ticket = tickets.SingleOrDefault(t => { return t.Value.Players.Contains(player); });

            tickets.Remove(ticket.Key);
            if (ticket.Value.Players.Count > 1 && player.InParty)
                pool.Client.Whisper(player.Party, string.Format("{0} has left the Group Finder " +
                    "queue, your party is no longer queued."));

            return "You have successfully left the Group Finder queue.";
        }

        private void Unqueue(IParty party)
        {
            foreach (var p in party.Players)
                UnQueue(p);
        }

        public string Waiting(IPlayer player)
        {
            if (!IsQueued(player))
                return "You are not currently queued for any Adventures in the Group Finder, " +
                    "Use !Queue command to join Group Finder.";

            var ticket = tickets.Single(t =>
            {
                return t.Value.Players.Contains(player);
            }).Value;

            string message = string.Format("You have been waiting {0} min(s) for the " +
                "following Adventures: ", DateTime.Now.Subtract(ticket.TimeIn).Minutes);
            foreach (var a in ticket.Adventures)
                message += a.Name + " ";
            return message;

        }

        class AdventureTicket
        {
            public DateTime TimeIn { get; }
            public IList<IPlayer> Players { get; }
            public SortedSet<IAdventure> Adventures { get; }

            public AdventureTicket(IList<IAdventure> adventures, params IPlayer[] players)
            {
                this.TimeIn = DateTime.Now;
                this.Players = players;
                this.Adventures = new SortedSet<IAdventure>(adventures);
            }

            public bool IsDuplicateCharClassType(IList<IPlayer> players)
            {
                return Players.Any(p => {
                    return players.Any(pl =>
                    { return pl.CharClass.CharClassType.Equals(p.CharClass.CharClassType); });
                });
            }

            public override bool Equals(object o)
            {
                if (o == null || !(o is AdventureTicket))
                    return false;

                AdventureTicket at = (AdventureTicket)o;

                return (this.TimeIn.Equals(at.TimeIn) &&
                    this.Players.Equals(at.Players) &&
                    this.Adventures.Equals(at.Adventures));
            }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }

        }
    }

    public class GroupFinderFactory
    {

        public IGroupFinder Create(IPartyPool pool, int partyCapacity, IAdventureRepository repo,
            IAdventureManager am)
        {
            return new GroupFinder(am.Queue(), pool, partyCapacity, repo);
        }
    }


    #endregion

    #region AdventureManager
    public interface IAdventureManager
    {
        BlockingCollection<KeyValuePair<IParty, IAdventure>> Queue();
        void Add(IAdventureProgressObserver observer);
        void Remove(IAdventureProgressObserver observer);
        void Run();
    }

    public class AdventureManager : IAdventureManager
    {
        private readonly ITwitchClient client;
        public int PartySizeLimit { get; }
        public bool IsAlive { get; set; }
        private readonly Random random;
        private readonly IList<IAdventureProgressObserver> observers;

        private readonly BlockingCollection<KeyValuePair<IParty, IAdventure>> queue;

        public AdventureManager(ITwitchClient client, int partySizeLimit)
        {
            this.client = client;
            this.PartySizeLimit = partySizeLimit;
            this.queue = new BlockingCollection<KeyValuePair<IParty, IAdventure>>();
            this.random = new Random();
            this.observers = new List<IAdventureProgressObserver>();
        }

        public BlockingCollection<KeyValuePair<IParty, IAdventure>> Queue() { return queue; }

        public void Run()
        {
            IsAlive = true;
            while (IsAlive)
            {

                var kv = queue.Take();
                var party = kv.Key;
                var adventure = kv.Value;

                foreach (var p in party.Players)
                    p.AddCoins(-adventure.Cost);
                client.Whisper(party, string.Format("Loading information for... {0}. "
                    + "{1} coins have been removed from each member.", adventure.Name,
                    adventure.Cost));

                ADVENTURE_PROGRESS progress = ADVENTURE_PROGRESS.IN_PROGRESS;
                UpdateAllOnStart(party, adventure);

                int encounterNo = 0;
                for (encounterNo = 0; encounterNo < adventure.NumOfEncounters; encounterNo++)
                {
                    string encounter = adventure.Encounter(encounterNo);
                    client.Whisper(party, encounter);
                    if (isEncounterSuccessful(party, adventure, encounterNo))
                    {
                        string encounterSuccess = adventure.EncounterSuccess(encounterNo);
                        client.Whisper(party, encounterSuccess);
                    }
                    else
                    {
                        progress = ADVENTURE_PROGRESS.UNSUCCESSFULLY_COMPLETED;
                        break;
                    }
                }


                if (progress == ADVENTURE_PROGRESS.IN_PROGRESS)
                {
                    progress = ADVENTURE_PROGRESS.SUCCESSFULLY_COMPLETED;
                    string reward = Reward(party, adventure);
                    IDictionary<IPlayer, IList<IItem>> items = RewardItems(party, adventure);

                    string completion = string.Format("{0} {1}", adventure.Success, reward);
                    foreach (var playerItems in items)
                    {
                        completion += string.Format("{0} has found the following items: ",
                            playerItems.Key.Name);
                        foreach (var item in playerItems.Value)
                            completion += string.Format("{0} ", item.Name);
                    }

                    UpdateAllOnSuccess(party, encounterNo, items, completion);
                    client.Whisper(party, completion);
                }
                else
                {
                    string penalty = Penalise(party, adventure);
                    IList<IPlayer> deaths = CalcDeaths(party);
                    var itemsLost = RollPossibleLostItemFromDeath(deaths);
                    string failureMessage = string.Format("{0} {1}", adventure.Failure,
                        penalty);
                    UpdateAllOnFailure(party, encounterNo, deaths, itemsLost, failureMessage);
                    client.Whisper(party, failureMessage);
                }

            }

        }

        private bool isEncounterSuccessful(IParty party, IAdventure adventure, int encounter)
        {
            float chance = party.SuccessChance * (party.Level / adventure.MaximumLevel);
            chance = chance < (party.SuccessChance * .75) ? party.SuccessChance * .75f : chance;
            chance += adventure.BaseSuccessRate * (party.Players.Count / PartySizeLimit);
            chance -= adventure.EncounterDifficulty(encounter);

            return chance > random.Next(0, 100);
        }

        private string Reward(IParty party, IAdventure adventure)
        {
            int xp = CalcXP(adventure, party, adventure.RewardModifier);
            int coins = CalcCoins(adventure, party, adventure.RewardModifier);

            string rewardMessage = adventure.Name + " has been completed! Each player has been awarded: " +
                xp + " xp and " + coins + " coins! ";

            party.AddXp(xp);
            party.AddCoins(coins);
            return rewardMessage;

        }

        /// <summary>
        /// This function will roll chances against the party's Item Find stat, if successful
        /// then the item will be randomly given to a player in the party. The player 
        /// does not need to meet any requirements to use the item in order for it to drop
        /// for them. 
        /// The item is then placed in the players inventory and the resulting dictionary is
        /// returned to show what items have been rewarded and who to.
        /// </summary>
        /// <param name="party"></param>
        /// <param name="adventure"></param>
        /// <returns></returns>
        private IDictionary<IPlayer, IList<IItem>> RewardItems(IParty party, IAdventure adventure)
        {
            IDictionary<IPlayer, IList<IItem>> items = new Dictionary<IPlayer, IList<IItem>>();
            foreach (var item in adventure.Loot)
                if (party.ItemFind >= random.Next(100))
                {
                    IPlayer player = party.Players[random.Next(0, party.Players.Count)];
                    player.AddItem(item);
                    if (items.ContainsKey(player))
                        items[player].Add(item);
                    else
                        items.Add(player, new List<IItem>(new IItem[] { item }));
                }
            return items;
        }

        private int CalcXP(IAdventure adventure, IParty party, float rewardMod)
        {
            int level = party.Level < adventure.MaximumLevel ? party.Level : adventure.MaximumLevel;
            int totalLevelTNL = new LevelFactory().createFromLevel(100, level).TNL();
            int xp = totalLevelTNL / 50;
            return ScaleReward(level, xp, rewardMod);
        }

        private int CalcCoins(IAdventure adventure, IParty party, float rewardMod)
        {
            int coins = 50; //base default from legacy
            coins += (party.CoinBonus * coins) / 100;

            int level = party.Level < adventure.MaximumLevel ? party.Level : adventure.MaximumLevel;
            return ScaleReward(level, coins, rewardMod) / party.Players.Count;
        }

        private int ScaleReward(int partyLevel, int reward, float rewardMod)
        {
            reward = (int)((reward * rewardMod) * partyLevel);
            reward += random.Next(-2 * partyLevel, 2 * partyLevel);
            reward = reward < 5 ? 5 : reward;
            return reward;
        }

        private string Penalise(IParty party, IAdventure adventure)
        {
            int xp = CalcXP(adventure, party, adventure.RewardModifier);
            int coins = CalcXP(adventure, party, adventure.RewardModifier);

            string message = "Failed to complete " + adventure.Name + "! ";

            return message;
        }

        private IList<IPlayer> CalcDeaths(IParty party)
        {
            var deadPlayers = new List<IPlayer>();
            foreach (var p in party.Players)
                if (IsKilled(party.PreventDeathBonus))
                    deadPlayers.Add(p);

            return deadPlayers;
        }

        private IDictionary<IPlayer, IEquipment> RollPossibleLostItemFromDeath(
            IList<IPlayer> died)
        {
            IDictionary<IPlayer, IEquipment> items = new Dictionary<IPlayer, IEquipment>();
            foreach (var p in died)

                if (p.Equipped.GetEquipped().Count != 0 && random.Next(1, 100) < 15)
                {
                    int equipNo = p.Equipped.GetEquipped().Count;
                    IEquipment item = p.Equipped.GetEquipped()
                        .ElementAt(random.Next(0, equipNo)).Value;
                    if (item != null)
                    {
                        p.RemoveItem(item);
                        items.Add(p, item);
                    }
                }

            return items;
        }

        private bool IsKilled(float chance)
        {
            return random.Next(0, 99) <= chance;
        }

        public void Add(IAdventureProgressObserver observer)
        {
            observers.Add(observer);
        }

        public void Remove(IAdventureProgressObserver observer)
        {
            observers.Remove(observer);
        }

        private void UpdateAllOnStart(IParty party, IAdventure adventure)
        {
            foreach (var o in observers)
                o.UpdateOnStart(party, adventure);
        }

        private void UpdateAllOnFailure(IParty party, int encounter, IList<IPlayer> deaths,
            IDictionary<IPlayer, IEquipment> itemsLost, string message)
        {
            foreach (var o in observers)
                o.UpdateOnFailure(party, encounter, deaths, itemsLost, message);
        }

        private void UpdateAllOnSuccess(IParty party, int encounter,
            IDictionary<IPlayer, IList<IItem>> loot, string message)
        {
            foreach (var o in observers)
                o.UpdateOnSuccess(party, encounter, loot, message);
        }
    }

    public enum ADVENTURE_PROGRESS
    {
        NOT_STARTED,
        IN_PROGRESS,
        SUCCESSFULLY_COMPLETED,
        UNSUCCESSFULLY_COMPLETED
    }

    public interface IAdventureProgressObserver
    {
        void UpdateOnStart(IParty party, IAdventure adventure);
        void UpdateOnSuccess(IParty party, int encounter,
            IDictionary<IPlayer, IList<IItem>> loot, string resultingMessage);
        void UpdateOnFailure(IParty party, int encounter, IList<IPlayer> deaths,
            IDictionary<IPlayer, IEquipment> itemsLost, string message);

    }

    #endregion

    #region Adventure

    public interface IEncounter : IComparable<IEncounter>
    {
        int EncounterIndex { get; }
        int EncounterDifficulty { get; }
        string EncounterMessage { get; }
        string SuccessMessage { get; }
    }

    public class Encounter : IEncounter
    {
        public int EncounterIndex { get; }
        public string EncounterMessage { get; }
        public int EncounterDifficulty { get; }
        public string SuccessMessage { get; }

        public Encounter(int i, string encounter, int encounterDifficulty,
            string successMessage)
        {
            this.EncounterIndex = i;
            this.EncounterMessage = encounter;
            this.EncounterDifficulty = encounterDifficulty;
            this.SuccessMessage = successMessage;
        }

        public int CompareTo(IEncounter other)
        {
            return EncounterIndex.CompareTo(other.EncounterIndex);
        }
    }

    public interface IAdventure : IComparable<IAdventure>
    {
        int ID { get; }
        string Name { get; }
        string Description { get; }
        int MinimumLevel { get; }
        int MaximumLevel { get; }
        int BaseSuccessRate { get; }
        int Cost { get; }
        int PartySize { get; }
        int NumOfEncounters { get; }
        int EncounterDifficulty(int encounterNo);
        string Encounter(int encounterNo);
        string EncounterSuccess(int encounterNo);
        string Failure { get; }
        string Success { get; }
        IReadOnlyList<IItem> Loot { get; }
        float RewardModifier { get; }

    }

    class Adventure : IAdventure
    {
        public int ID { get; }
        public string Name { get; }
        public string Description { get; }
        public int MaximumLevel { get; }
        public int MinimumLevel { get; }
        public int BaseSuccessRate { get; }
        public int Cost { get { return 50 + ((MinimumLevel - 3) * 10); } }
        public int PartySize { get; }
        public int NumOfEncounters { get { return this.Encounters.Count; } }
        public SortedSet<IEncounter> Encounters { get; }
        public string Failure { get; }
        public string Success { get; }
        public IReadOnlyList<IItem> Loot { get; }
        public float RewardModifier { get; }

        public Adventure(int id, string name, string desc, int max, int min,
            int baseSuccessRate, int cost, int partySize, string failure, string success,
            SortedSet<IEncounter> encounters, IList<IEquipment> loot, float rewardMod)
        {
            this.ID = id;
            this.Name = name;
            this.Description = desc;
            this.MaximumLevel = max;
            this.MinimumLevel = min;
            this.BaseSuccessRate = baseSuccessRate;
            this.PartySize = partySize;
            this.Failure = failure;
            this.Success = success;
            this.Encounters = encounters;
            this.Loot = new List<IItem>(loot);
            this.RewardModifier = rewardMod;
        }

        public Adventure(int id, string name, string desc, int max, int min,
            int baseSuccessRate, int cost, string failure, string success,
            SortedSet<IEncounter> encounters, IList<IEquipment> loot, float rewardMod)
        {
            this.ID = id;
            this.Name = name;
            this.Description = desc;
            this.MaximumLevel = max;
            this.MinimumLevel = min;
            this.BaseSuccessRate = baseSuccessRate;
            this.PartySize = 3;
            this.Failure = failure;
            this.Success = success;
            this.Encounters = encounters;
            this.Loot = new List<IItem>(loot);
            this.RewardModifier = rewardMod;
        }

        public int EncounterDifficulty(int encounterNo)
        {
            return Encounters.ElementAt(encounterNo).EncounterDifficulty;
        }

        public string Encounter(int encounterNo)
        {
            return Encounters.ElementAt(encounterNo).EncounterMessage;
        }

        public string EncounterSuccess(int encounterNo)
        {
            return Encounters.ElementAt(encounterNo).SuccessMessage;
        }

        public int CompareTo(IAdventure other)
        {
            return this.MinimumLevel.CompareTo(other.MinimumLevel);
        }
    }

    public static class AdventureUtils
    {
        public static Func<IAdventure, bool> GetCostRequirementPredicate(IPlayer player)
        {
            return new Func<IAdventure, bool>(a => { return a.Cost <= player.Coins; });
        }

        public static Func<IAdventure, bool> GetLevelRequirementsPredicate(IPlayer player)
        {
            return new Func<IAdventure, bool>(a => {
                return (a.MinimumLevel <= player.Level &&
a.MaximumLevel >= player.Level);
            });
        }

        public static Func<IAdventure, bool> GetGeneralRequirementsPredicate(IPlayer player)
        {
            return new Func<IAdventure, bool>(a =>
            {
                return (GetCostRequirementPredicate(player).Invoke(a) &&
                GetLevelRequirementsPredicate(player).Invoke(a));
            });
        }

        public static Func<IAdventure, bool> GetCostRequirementPredicate(IParty party)
        {
            return new Func<IAdventure, bool>(a =>
            {
                IAdventure adventure = a;
                return party.Players.All(p => { return adventure.Cost <= p.Coins; });
            });
        }

        public static Func<IAdventure, bool> GetPartySizeRequirementPredicate(IParty party)
        {
            return new Func<IAdventure, bool>(a => { return a.PartySize == party.Size; });
        }

        public static Func<IAdventure, bool> GetGeneralRequirementsPredicate(IParty party)
        {
            return new Func<IAdventure, bool>(a =>
            {
                IAdventure adventure = a;
                return (party.Size == adventure.PartySize &&
                party.Players.All(p => { return adventure.Cost <= p.Coins; }));
            });
        }

        public static Func<IAdventure, bool> GetLevelRequirementsPredicate(IParty party)
        {
            return new Func<IAdventure, bool>(a =>
            {
                IAdventure adventure = a;
                return party.Players.All(p =>
                {
                    return adventure.MinimumLevel <= p.Level &&
                    adventure.MaximumLevel >= p.Level;
                });
            });
        }

        public static Func<IAdventure, bool> GetAllRequirementsPredicate(IParty party)
        {
            return new Func<IAdventure, bool>(a =>
            {
                IAdventure adventure = a;
                return party.Size == adventure.PartySize && party.Players.All(p =>
                {
                    return (GetCostRequirementPredicate(p).Invoke(adventure) &&
                    GetLevelRequirementsPredicate(p).Invoke(adventure));
                });
            });
        }

        public static IEnumerable<IAdventure> FilterMatching(
            ICollection<IAdventure> collection, params Func<IAdventure, bool>[] funcs)
        {
            Func<IAdventure, bool> func = new Func<IAdventure, bool>(a =>
            {
                return funcs.All(f => { return f.Invoke(a); });
            });
            return collection.Where(func);
        }
    }

    public interface IAdventureRepository
    {
        IAdventure GetById(int id);
        IList<IAdventure> GetAll();
        IList<IAdventure> GetByLevel(int level);
        IList<IAdventure> Get(Predicate<IAdventure> prdct);
    }

    public class LegacyAdventureRepository : IAdventureRepository
    {
        private const int LEGACY_COST = 50;
        private const float LEGACY_REWARD_MOD = 1.05f;

        public const string LEGACY_DUNGEON_BRIDGE_FILE_PATH = "content/dungeonlist.ini";
        public const string LEGACY_DUNGEON_FILE_PATH_PREFIX = "content/dungeons/";

        private static IDictionary<int, IAdventure> ADVENTURES;

        //Gets a new AdventureRepository, this repository will look up the dungeonlist.ini file and use
        // it as a bridge to find the correct file for each type of dungeon. 
        // If this method is called more than once during the application life, it will check if the 
        // dungeons have already been loaded before doing so again.
        // Params: 
        // dbfp - dungeonBridgeFilePath [Legacy - "content/dungeonlist.ini"]
        // dfpp - dungeonFilePathPrefix [Legacy - "content/dungeons/"]
        // throws: 
        // IOException - if a file cannot be accessed this exception will be thrown
        // NullPointException - Is thrown if any params are null or any resulting calls to files return null.
        public static LegacyAdventureRepository getInstance(string bridgeFile, string filePrefix,
            IEquipmentRepository items)
        {
            if (LegacyAdventureRepository.ADVENTURES != null)
                return new LegacyAdventureRepository(LegacyAdventureRepository.ADVENTURES);
            IDictionary<int, string> dungeonDic = createDungeonID_File_Dic(bridgeFile, filePrefix);
            IDictionary<int, IAdventure> adventures = new Dictionary<int, IAdventure>();

            foreach (KeyValuePair<int, string> dungeonID_FilePath in dungeonDic)
            {
                IAdventure adv = create(dungeonID_FilePath, items);
                adventures.Add(adv.ID, adv);
            }

            return new LegacyAdventureRepository(adventures);
        }

        private static IDictionary<int, string> createDungeonID_File_Dic(string bridgeFile,
            string filePrefix)
        {
            IDictionary<int, string> dungeonList = new Dictionary<int, string>();

            IEnumerable<string> fileText = System.IO.File.ReadLines(bridgeFile, UTF8Encoding.Default);
            int dungeonIter = 1;
            foreach (var line in fileText)
            {
                string[] temp = line.Split(',');
                int id = -1;
                int.TryParse(temp[0], out id);
                if (id != -1)
                    dungeonList.Add(id, filePrefix + temp[1] + ".ini");
                else
                    Console.WriteLine("Invalid dungeon read on line " + dungeonIter);
                dungeonIter++;
            }
            return dungeonList;
        }

        private static IAdventure create(KeyValuePair<int, string> dungeonID_FilePath,
            IEquipmentRepository items)
        {

            IList<string> dungeonText = new List<string>();
            IList<IEquipment> loot = new List<IEquipment>();

            IEnumerable<string> lines = System.IO.File.ReadLines(dungeonID_FilePath.Value, UTF8Encoding.Default);
            int textIter = 1;
            string[] header = lines.ElementAt(textIter).Split(',');
            string name = header[0];
            int numEncounters;
            int.TryParse(header[1], out numEncounters);
            int baseSuccessRate;
            int.TryParse(header[2], out baseSuccessRate);
            int minLevel;
            int.TryParse(header[3], out minLevel);
            int maxLevel;
            int.TryParse(header[4], out maxLevel);

            textIter++;
            string[] enemies = lines.ElementAt(textIter).Split(',');
            if ((enemies.Count() / 2) != numEncounters)
                throw new InvalidOperationException("Dungeon at " + dungeonID_FilePath.Value +
                    " has a mismatch for # of encounters & encounter data.");

            IDictionary<string, int> encounters = new Dictionary<string, int>();

            for (int i = 0; i < enemies.Count(); i += 2)
            {
                int difficulty = 0;
                int.TryParse(enemies[i + 1], out difficulty);
                encounters.Add(enemies[i], difficulty);
            }

            textIter++;
            string desc = lines.ElementAt(textIter);
            textIter++;
            string victory = lines.ElementAt(textIter);
            textIter++;
            string defeat = lines.ElementAt(textIter);
            textIter++;
            if (lines.ElementAt(textIter).StartsWith("Loot="))
            {
                string[] temp = lines.ElementAt(textIter).Split('=');
                string[] ids = temp[1].Split(',');
                for (int i = 0; i < ids.Length; i++)
                {
                    int toAdd = -1;
                    int.TryParse(ids[i], out toAdd);
                    IEquipment equipment = items.getById(toAdd);
                    if (equipment != null)
                        loot.Add(equipment);
                }
                textIter++;
            }

            int iter = 0;
            foreach (var line in lines.Skip(textIter))
            {
                dungeonText.Add(line);
                iter++;
            }

            SortedSet<IEncounter> encounterSet = new SortedSet<IEncounter>();
            IDictionary<string, int> encounter_difficulty = new Dictionary<string, int>();
            int j = 0;
            for (int i = 0; i < dungeonText.Count; i += 2, j++)
            {
                IEncounter e = new Encounter(j, dungeonText[i], encounters.ElementAt(j).Value,
                    dungeonText[i + 1]);
                encounterSet.Add(e);
            }

            

            return new Adventure(dungeonID_FilePath.Key, name, desc, maxLevel, minLevel,
                baseSuccessRate, LEGACY_COST, defeat, victory, encounterSet,
                loot, LEGACY_REWARD_MOD);

        }


        private LegacyAdventureRepository(IDictionary<int, IAdventure> adventures)
        {
            if (ADVENTURES == null)
                LegacyAdventureRepository.ADVENTURES = adventures;
        }

        public IAdventure GetById(int id) { return ADVENTURES[id]; }

        public IList<IAdventure> GetByLevel(int level)
        {
            IList<IAdventure> list = new List<IAdventure>();
            foreach (var adv in ADVENTURES.Values)
                if (adv.MinimumLevel <= level && adv.MaximumLevel >= level)
                    list.Add(adv);
            return list;
        }

        public IList<IAdventure> GetAll()
        {
            return new List<IAdventure>(ADVENTURES.Values);
        }

        public IList<IAdventure> Get(Predicate<IAdventure> prdct)
        {
            return new List<IAdventure>(GetAll().Where(prdct.Invoke));
        }
    }

    #endregion


}