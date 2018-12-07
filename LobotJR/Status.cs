using Client;
using Player;
using System;

namespace Status
{
    /// <summary>
    /// Controls the amount of xp, and despite only having an "AddXP" method these objects should support minus
    /// values of xp to be added allowing for the concept of both leveling up and down. The xp amount should be 
    /// an unsigned int so 0 should be the lowest value available.
    /// </summary>
    public interface ILevel
    {
        int LevelCap { get; }

        /// <summary>
        /// Current xp in this object.
        /// </summary>
        int XP { get; }

        /// <summary>
        /// Calculates the level based on the amount XP.
        /// </summary>
        /// <returns>Level based on current xp</returns>
        int Value();

        /// <summary>
        ///Adds XP to this and will return true if a change of level has occurred.
        ///This method should support both positive and negative values, adding or removing to the current xp total 
        ///respectively.
        /// </summary>
        /// <param name="xp"></param>
        /// <returns>if the level has changed due to the added xp</returns>
        void AddXP(int xp);
        /// <summary>
        /// TNL short for Till Next Level, and is the amount of XP required for the next level.
        /// </summary>
        /// <returns>Amount of xp required for next level</returns>
        int TNL();

        ///<summary>
        ///This function provides insight into how the ILevel class implements XP to Level, returning 
        ///the amount of XP required for a given level. All levels start at 1 so giving 1 as a parameter will
        ///also return 0.
        ///</summary>
        int XPForLevel(int level);

    }

    class PlayerLevel : ILevel
    {
        private readonly int xpCap;
        public int LevelCap { get; }
        public int XP { get { return this.xp; } }
        private int xp;

        public PlayerLevel(int levelCap, int xp)
        {
            this.LevelCap = levelCap;
            this.xpCap = XPForLevel(levelCap);
            this.xp = xp;
        }

        public PlayerLevel(int levelCap)
        {
            this.LevelCap = levelCap;
            this.xpCap = XPForLevel(levelCap);
            this.xp = 0;
        }

        public int Value()
        {
            //as per legacy -> xp = 4*(L^3) +50 :. L^3 = (XP-50)/4 :. L = ((XP-50)/4)^(1/3)
            //if xp is below 82 then this will also return 1
            if (xp < 82)
                return 1;
            return (int)Math.Round(Math.Pow(((xp - 50) / 4d), (1d / 3d)));
        }

        public void AddXP(int xp)
        {
            //int before = Value();
            this.xp += xp;
            this.xp = this.xp > xpCap ? xpCap : this.xp;
            //int after = Value();

            //return (before != after);
        }

        public int TNL()
        {
            return XPForLevel(Value() + 1) - xp;
        }

        public int XPForLevel(int level)
        {
            //as per legacy -> xp = 4*(L^3)+50
            return (int)((4 * Math.Pow(level, 3)) + 50);
        }
    }

    /// <summary>
    /// Factory method for creating of ILevel objects hiding the implementation of this interface.
    /// </summary>
    public class LevelFactory
    {
        /// <summary>
        /// Create method for a new ILevel object, this would return 0 xp accumulated and should also calculate that the 
        /// level would be 1.
        /// </summary>
        /// <returns>A new default ILevel object with no xp added</returns>
        public ILevel create(int levelCap) { return new PlayerLevel(levelCap); }
        /// <summary>
        /// Create method for a new ILevel object, with the accumulated xp as the param. This is a conviencience method
        /// to avoid the need to make a new ILevel object then add xp after.
        /// </summary>
        /// <param name="xp"></param>
        /// <returns>A ILevel object with the respective amount of xp.</returns>
        public ILevel createFromXP(int levelCap, int xp) { return new PlayerLevel(levelCap, xp); }
        /// <summary>
        /// Create method for a new ILevel object, with the minimum xp required to reach the respective level.
        /// This allows for creating a set ILevel object level without knowing how the level is calculated.
        /// </summary>
        /// <param name="level"></param>
        /// <returns>A ILevel object with the minimum xp required for the respective level</returns>
        public ILevel createFromLevel(int levelCap, int level)
        {
            int xp = create(levelCap).XPForLevel(level);
            return createFromXP(levelCap, xp);
        }
    }

    /// <summary>
    /// Simple object for level control, allowing to add xp and see what level the current object is at.
    /// This object also inherits the IStatusable interface, this adds the flexibility of having an object 
    /// that can in theory level up and have stats. 
    /// </summary>
    public interface ILevelable : IStatusable
    {
        /// <summary>
        /// Simple get method returning the level of this object.
        /// </summary>
        int Level { get; }
        /// <summary>
        /// Add xp to this object and will return true if a change of level has occurred as a result. Supporting 
        /// objects should accept both positive and negative values, allowing for the concept of leveling up and down
        /// therefore the boolean should be true regardless of how the level changes.
        /// </summary>
        /// <param name="xp"></param>
        /// <returns>True if the xp added has caused the level to change.</returns>
        void AddXp(int xp);
    }

    /// <summary>
    /// This observable class supports the ILevelObserver interface and will update all those observers when a change
    /// to this objects level. The update will contain both the old and new value for the level object. This could be in
    /// the event of either the level going up or down.
    /// </summary>
    public interface ILevelObservable
    {
        /// <summary>
        /// Adds the given ILevelObserver to the list of observers to update in the event of a level change in this object.
        /// </summary>
        /// <param name="o"></param>
        void AddObserver(ILevelObserver o);
        /// <summary>
        /// Removes the given ILevelObserver from the list of observers, if no such observer exists this method does
        /// nothing.
        /// </summary>
        /// <param name="o"></param>
        void RemoveObserver(ILevelObserver o);
        //void UpdateAll(int oldValue, int newValue);
    }

    /// <summary>
    /// This observer class is used in conjunction with the ILevelObservable class allowing for this object to observe
    /// changes in levels. This object will recieve a "name" and the old and new value from the observable object.
    /// </summary>
    public interface ILevelObserver
    {
        /// <summary>
        /// This should only be called by the ILevelObservable class when a level has changed in that class.
        /// </summary>
        /// <param name="observableName"></param>
        /// <param name="oldValue"></param>
        /// <param name="newValue"></param>
        void Update(IPlayer player, int oldValue, int newValue);
    }

    public class LevelupNotifier : ILevelObserver
    {

        private readonly ITwitchClient client;

        public LevelupNotifier(ITwitchClient client)
        {
            this.client = client;
        }

        public void Update(IPlayer player, int oldValue, int newValue)
        {
            int tnl = player.CharClass.PlayerLevelInfo.TNL();

            if (oldValue < newValue)
                client.Whisper(player.Name, string.Format("DING! you just reached level {0}! " +
                    "XP to next level: {1}", newValue, tnl));
            else
                client.Whisper(player.Name, string.Format("Uh oh! You have just deleveled! " +
                    "you are now level {0}! XP to retain old level: {1}", newValue, tnl));

        }
    }

    /// <summary>
    /// This contains the properties for status' that help with encounters, the simplicity of this interface allows
    /// for ease of use with many different kind of objects for example: players, equipment, items, status effects etc.
    /// </summary>
    public interface IStatusable
    {

        float SuccessChance { get; }
        int ItemFind { get; }
        int CoinBonus { get; }
        int XpBonus { get; }
        float PreventDeathBonus { get; }
    }

    public class LevelSync : IStatusable
    {
        public int Level { get; }
        public int CoinBonus { get; }
        public int ItemFind { get; }
        public float PreventDeathBonus { get; }
        public float SuccessChance { get; }
        public int XpBonus { get; }

        public LevelSync(int level, int coinBonus, int itemFind, float preventDeathBonus,
            float successChance, int xpBonus)
        {
            this.Level = level;
            this.CoinBonus = coinBonus;
            this.ItemFind = itemFind;
            this.PreventDeathBonus = preventDeathBonus;
            this.SuccessChance = successChance;
            this.XpBonus = xpBonus;
        }

        public LevelSync(int level)
        {
            this.Level = level;
        }



    }

    public class LevelSyncFactory
    {
        public static LevelSync Create(IPlayer player, int sync)
        {
            if (sync < 12)
                return CounterScale(player, sync, 2);
            else if (sync < 9)
                return CounterScale(player, sync, 1);
            else if (sync < 5)
                return CounterScale(player, sync, 0);
            return new LevelSync(sync);
        }

        private static LevelSync CounterScale(IPlayer player, int level,
            int rarity)
        {
            int coinBonus = 0, itemFind = 0, xpBonus = 0;
            float successChance = 0, preventDeathBonus = 0;

            foreach (var equip in player.Equipped.GetEquipped().Values)
                if (equip.Rarirty > rarity)
                {
                    coinBonus += equip.CoinBonus * (rarity / equip.Rarirty);
                    itemFind += equip.ItemFind * (rarity / equip.Rarirty);
                    xpBonus += equip.XpBonus * (rarity / equip.Rarirty);
                    successChance += equip.SuccessChance * (rarity / equip.Rarirty);
                    preventDeathBonus += equip.PreventDeathBonus * (rarity / equip.Rarirty);
                }

            return new LevelSync(level, coinBonus, itemFind, preventDeathBonus,
                successChance, xpBonus);

        }
    }

}