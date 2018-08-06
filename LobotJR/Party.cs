using Client;
using Player;
using Status;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PartyGroup
{
    public interface IParty : ILevelable
    {
        int GroupID { get; set; }
        IPlayer PartyLeader { get; set; }
        IList<IPlayer> Players { get; }
        int Size { get; }
        int PartyLimit { get; }
        IList<IPlayer> PendingInvites { get; }
        void AddCoins(int coins);

        int NumInvitesSent { get; }
        bool PendingInvite { get; }
        bool UsedGroupFinder { get; }
        bool IsReady { get; }

        /// <summary>
        /// Party level, which is the lowest level in the party.
        /// </summary>
        new int Level { get; }
        bool IsSyncd { get; }

        //Adds a new IPlayer to this instance of IParty. 
        //Returns bool value which is true when successful and false if unsuccessful.
        //Returning false may be if the amount of Players have reached this instance limit.
        bool Add(IPlayer player);
        //Remove player from Party, this will call Unregister func on the IPlayer obj 
        // Returns true on successfully removing a IPlayer obj from this instance, however 
        //will return false if this IPlayer does not exist in this instance.
        bool Remove(IPlayer player);
        void Disband();

        void Invited(IPlayer player);
        void ClearInvites();
        void UnsyncLevel();
        bool LevelSync(int level);



    }

    class Party : IParty
    {
        private readonly IPartyPool pool;
        public int GroupID { get; set; }
        public IPlayer PartyLeader { get; set; }
        public int PartyLimit { get; }

        public int XpBonus { get { return this.xpBonus; } }
        private int xpBonus;
        public int CoinBonus { get { return this.coinBonus; } }
        private int coinBonus;
        public int ItemFind { get { return this.itemFind; } }
        private int itemFind;
        public float SuccessChance { get { return this.successChance; } }
        private float successChance;
        public float PreventDeathBonus { get { return this.preventDeathBonus; } }
        private float preventDeathBonus;

        public int NumInvitesSent { get { return PendingInvites.Count; } }
        public bool PendingInvite { get { return NumInvitesSent != 0; } }
        public bool UsedGroupFinder { get; }
        public bool IsReady { get { return Players.Count == PartyLimit; } }

        public int Level
        {
            get
            {
                if (IsSyncd)
                    return syncdLevel;
                int level = 0;
                foreach (var p in Players)
                    level = p.CharClass.Level > level ? p.CharClass.Level : level;
                return level;
            }
        }
        private int syncdLevel = -1;
        public bool IsSyncd { get { return syncdLevel != -1; } }

        public IList<IPlayer> PendingInvites { get; }
        public IList<IPlayer> Players { get; }
        public int Size { get { return Players.Count; } }

        private Party(IPartyPool pool, int partyLimit)
        {
            this.pool = pool;
            this.PartyLimit = partyLimit;
            this.Players = new List<IPlayer>();
            this.PendingInvites = new List<IPlayer>();
        }

        public Party(IPartyPool pool, int partyLimit, IPlayer leader) : this(pool, partyLimit)
        {
            this.PartyLeader = leader;
            leader.Register(this);
            Players.Add(leader);
            UpdateStatus();
            UsedGroupFinder = false;
        }

        public Party(IPartyPool pool, IList<IPlayer> players) : this(pool, players.Count())
        {
            if (PartyLimit > 0)
                this.PartyLeader = players[0];

            foreach (var p in players)
            {
                Players.Add(p);
                p.Register(this);
            }

            UpdateStatus();
            UsedGroupFinder = true;
        }

        private void UpdateStatus()
        {
            this.xpBonus = 0;
            this.coinBonus = 0;
            this.itemFind = 0;
            this.successChance = 0;
            this.preventDeathBonus = 0;

            IList<int> charclass = new List<int>();
            int duplicateMod = 1;

            foreach (var p in Players)
            {
                duplicateMod = 1;
                if (charclass.Contains(p.CharClass.CharClassType.ID))
                    duplicateMod = 2;
                xpBonus += p.CharClass.XpBonus / duplicateMod;
                coinBonus += p.CharClass.CoinBonus / duplicateMod;
                itemFind += p.CharClass.ItemFind / duplicateMod;
                successChance += p.CharClass.SuccessChance / duplicateMod;
                preventDeathBonus += p.CharClass.PreventDeathBonus / duplicateMod;
                charclass.Add(p.CharClass.CharClassType.ID);
            }
        }

        public void AddCoins(int coins)
        {
            foreach (var p in Players)
                p.AddCoins(coins);
        }

        public void AddXp(int xp)
        {
            foreach (var p in Players)
                p.AddXP(xp);
        }

        public bool Add(IPlayer player)
        {
            if (Players.Count == PartyLimit)
                return false;

            PendingInvites.Remove(player);
            Players.Add(player);
            player.Register(this);
            UpdateStatus();
            return true;
        }

        public bool Remove(IPlayer player)
        {
            if (!Players.Contains(player))
                return false;

            Players.Remove(player);
            player.Unregister();
            UpdateStatus();
            pool.Client.Whisper(this, string.Format("{0} has left the party!", player.Name));
            return true;
        }

        public void Disband()
        {
            for (int i = Players.Count; i >= 0; i++)
            {
                Players[i].Unregister();
                Players.RemoveAt(i);
            }
            pool.Unregister(this);
        }

        public void Invited(IPlayer player)
        {
            PendingInvites.Add(player);
        }

        public void ClearInvites()
        {
            PendingInvites.Clear();
        }

        public bool LevelSync(int level)
        {
            foreach (var p in Players)
                p.Add(LevelSyncFactory.Create(p, level));
            this.syncdLevel = level;
            return true;
        }

        public void UnsyncLevel()
        {
            foreach (var p in Players)
                p.ClearStatusEffects();
            this.syncdLevel = -1;
        }

    }

    public static class PartyUtils
    {
        public static bool IsLeader(IPlayer player)
        {
            return (player.InParty && player.Party.PartyLeader.Equals(player));
        }

        public static void ApplyMultiActionsToPlayers(IParty party,
            params Action<IPlayer>[] actions)
        {
            foreach (var p in party.Players)
                foreach (var a in actions)
                    a?.Invoke(p);
        }

        public static IPlayer GetLowestLevelPlayer(IParty party)
        {
            IPlayer player = null;
            foreach (var p in party.Players)
            {
                if (player != null)
                    player = player.Level < p.Level ? player : p;
                else
                    player = p;
            }
            return player;
        }

        public static IPlayer GetHighestLevelPlayer(IParty party)
        {
            IPlayer player = null;
            foreach (var p in party.Players)
            {
                if (player != null)
                    player = player.Level > p.Level ? player : p;
                else
                    player = p;
            }
            return player;
        }
    }

    public interface IPartyPool
    {
        ITwitchClient Client { get; }
        IParty Create(IPlayer player, int capacity);
        IParty Create(IList<IPlayer> players);
        void Unregister(IParty party);
        IList<IParty> All();
        bool HasInvite(IPlayer player);
        bool AcceptInvite(IPlayer player);
        bool DeclineInvite(IPlayer player);
    }

    public class PartyPool : IPartyPool
    {
        public ITwitchClient Client { get; }
        private static IList<IParty> ALL = new List<IParty>();

        public PartyPool(ITwitchClient client)
        {
            this.Client = client;
        }

        public IParty Create(IPlayer player, int capacity)
        {
            IParty party = new Party(this, capacity, player);
            ALL.Add(party);
            return party;
        }

        public IParty Create(IList<IPlayer> players)
        {
            IParty party = new Party(this, players);
            ALL.Add(party);
            return party;
        }

        public void Unregister(IParty party)
        {
            ALL.Remove(party);
        }

        public IList<IParty> All() { return ALL; }

        public bool HasInvite(IPlayer player)
        {
            return ALL.Any(p => { return p.PendingInvites.Contains(player); });
        }

        public bool AcceptInvite(IPlayer player)
        {
            return ALL.Any(p =>
            {
                if (p.PendingInvites.Contains(player))
                    return p.Add(player);
                return false;

            });
        }

        public bool DeclineInvite(IPlayer player)
        {
            return ALL.Any(p =>
            {
                if (p.PendingInvites.Remove(player))
                {
                    Client.Whisper(p.PartyLeader.Name,
                        string.Format("{0} has declined the invite", player.Name));
                    return true;
                }
                return false;
            });

        }

    }


}