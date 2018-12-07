using Classes;
using Companions;
using Equipment;
using PartyGroup;
using System;
using System.Collections.Generic;
using Status;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using System.Text;
using Client;

namespace Player
{

    public class CharClassType
    {
        public static readonly CharClassType DEPRIVED = new CharClassType(0, "Deprived", "DPV");

        public byte ID { get; }
        private string name;
        public string Name { get { return this.name; } }
        private string nameShort;
        public string NameShort { get { return this.nameShort; } }

        public CharClassType(byte id, string name, string nameShort)
        {
            this.ID = id;
            this.name = name;
            this.nameShort = nameShort;
        }

        public void changeName(string name, string nameShort)
        {
            this.name = name;
            this.nameShort = nameShort;
        }

        new public bool Equals(Object o)
        {
            if (o == null || !(o is CharClassType))
                return false;

            CharClassType cct = (CharClassType)o;

            return (this.ID.Equals(cct.ID) &&
                this.name.Equals(cct.name) &&
                this.nameShort.Equals(cct.nameShort));
        }
    }

    public interface ICharClass : ILevelable
    {
        string ClassName { get; }
        ILevel PlayerLevelInfo { get; }
        //char stats
        new float SuccessChance { get; set; }
        new int ItemFind { get; set; }
        new int CoinBonus { get; set; }
        new int XpBonus { get; set; }
        new float PreventDeathBonus { get; set; }
        ////type
        CharClassType CharClassType { get; }
        int Prestige { get; set; }
        //unknown - are pets actually used?
        IList<Pet> MyPets { get; }
        Pet GetPet(int petID);

    }

    public class DeprivedCharClass : ICharClass
    {
        public string ClassName { get { return "DEPRIVED"; } }
        public ILevel PlayerLevelInfo { get; }
        public int Level { get { return PlayerLevelInfo.Value(); } }
        public float SuccessChance { get { return 0; } set { } }
        public int ItemFind { get { return 0; } set { } }
        public int CoinBonus { get { return 0; } set { } }
        public int XpBonus { get { return 0; } set { } }
        public float PreventDeathBonus { get { return 0; } set { } }
        public CharClassType CharClassType { get { return CharClassType.DEPRIVED; } }
        public int Prestige { get; set; }
        public IList<Pet> MyPets { get; }

        public DeprivedCharClass(int levelCap)
        {
            this.PlayerLevelInfo = new PlayerLevel(levelCap);
            this.MyPets = new List<Pet>();
        }


        public Pet GetPet(int petID)
        {
            return null;
        }

        public void AddXp(int xp)
        {
            PlayerLevelInfo.AddXP(xp);
        }
    }

    class CharClassImpl : ICharClass
    {
        public CharClassType CharClassType { get; }
        public string ClassName { get { return this.CharClassType.Name; } }
        public ILevel PlayerLevelInfo { get { return level; } }
        public int CoinBonus { get; set; }
        public int ItemFind { get; set; }
        public int Level { get { return level.Value(); } }
        private readonly ILevel level;
        public IList<Pet> MyPets { get; }
        public int Prestige { get; set; }
        public float PreventDeathBonus { get; set; }
        public float SuccessChance { get; set; }
        public int XpBonus { get; set; }

        public CharClassImpl(CharClassType cct, int coinBonus, int itemFnd, IList<Pet> pets,
            int prestige, float pdb, float sc, int xB, int levelCap, int xE)
        {
            this.CharClassType = cct;
            this.CoinBonus = coinBonus;
            this.ItemFind = itemFnd;
            this.level = new LevelFactory().createFromXP(levelCap, xE);
            this.MyPets = pets;
            this.Prestige = prestige;
            this.PreventDeathBonus = pdb;
            this.SuccessChance = sc;
            this.XpBonus = xB;

        }

        public Pet GetPet(int petID)
        {
            foreach (var pet in MyPets)
                if (pet.ID == petID)
                    return pet;
            return null;
        }

        public void AddXp(int xp)
        {
            level.AddXP(xp);
        }

    }

    public class CharClassConverter
    {
        private readonly int classChoiceLevel;
        private readonly int levelCap;

        public CharClassConverter(int classChoiceLevel, int levelCap)
        {
            this.classChoiceLevel = classChoiceLevel;
            this.levelCap = levelCap;
        }

        public readonly static IDictionary<int, CharClassType> LEGACY_CLASS_TYPE = new Dictionary<int, CharClassType>()
        {
            {0, CharClassType.DEPRIVED },
            {1, new CharClassType(1, "Warrior", "WAR") },
            {2, new CharClassType(2, "Mage", "MGE") },
            {3, new CharClassType(3, "Rogue", "RGE") },
            {4, new CharClassType(4, "Ranger", "RNG") },
            {5, new CharClassType(5, "Cleric", "CLR") }
        };



        public ICharClass Convert(CharClass cc, int xp)
        {
            CharClassType cct = LEGACY_CLASS_TYPE[cc.classType];
            if (cc.classType == 0)
                return new DeprivedCharClass(classChoiceLevel);
            return new CharClassImpl(cct, cc.coinBonus, cc.itemFind, cc.myPets, cc.prestige,
                cc.preventDeathBonus, cc.successChance, cc.xpBonus, levelCap, xp);
        }

        public KeyValuePair<CharClass, int> Convert(ICharClass icc)
        {
            CharClass cc = new CharClass();
            cc.classType = LEGACY_CLASS_TYPE.Single(kv => kv.Value.Equals(icc.CharClassType)).Key;
            cc.coinBonus = icc.CoinBonus;
            cc.itemFind = icc.ItemFind;
            cc.myPets = new List<Pet>(icc.MyPets);
            cc.prestige = icc.Prestige;
            cc.preventDeathBonus = icc.PreventDeathBonus;
            cc.successChance = icc.SuccessChance;
            cc.xpBonus = icc.XpBonus;

            LevelFactory lf = new LevelFactory();
            return new KeyValuePair<CharClass, int>(cc,
                lf.createFromLevel(levelCap, icc.Level).XP);
        }
    }

    public interface ICharClassRepository
    {
        ICharClass GetById(int id);
    }

    public class LegacyCharClassRepository : ICharClassRepository
    {
        private readonly CharClassConverter charClassConverter;
        private readonly IDictionary<int, CharClass> id_charClass = new Dictionary<int, CharClass>()
        {
            {1, new Warrior() },
            {2, new Mage() },
            {3, new Rogue() },
            {4, new Ranger() },
            {5, new Cleric() }
        };

        public LegacyCharClassRepository(int classChoiceLevel, int levelCap)
        {
            charClassConverter = new CharClassConverter(classChoiceLevel, levelCap);
        }

        public ICharClass GetById(int id)
        {
            return charClassConverter.Convert(id_charClass[id], 0);
        }
    }

    public interface IPlayer : IStatusable, ILevelObservable
    {
        string ID { get; }
        string Name { get; set; }
        ICharClass CharClass { get; }
        int Level { get; }
        DateTime LastDailyGroupFinder { get; set; }
        void AddCoins(int coins);
        int Coins { get; }
        IEquipped Equipped { get; }
        void AddItem(IItem item);
        void RemoveItem(IItem item);
        IList<IItem> Items { get; }
        bool HasItem(IItem item);
        int TotalItemCount { get; }
        bool InParty { get; }
        IParty Party { get; }

        void AddXP(int xp);
        void Register(IParty party);
        void Unregister();
        bool IsToken(IPlayerToken token);
        void Add(IStatusable status);
        void ClearStatusEffects();

    }

    class Player : IPlayer
    {
        internal string id = string.Empty;
        public string ID { get { return id; } }
        private readonly ICharClass charClass;
        public ICharClass CharClass { get { return this.charClass; } }
        public int Level
        {
            get
            {
                if (InParty && party.IsSyncd)
                    return charClass.Level < party.Level ? charClass.Level : party.Level;
                return charClass.Level;
            }
        }
        public int CoinBonus
        {
            get
            {
                int cb = CharClass.CoinBonus;
                foreach (var se in StatusEffects)
                    cb += se.CoinBonus;
                return cb;
            }
        }
        public int ItemFind
        {
            get
            {
                int itemFind = CharClass.ItemFind;
                foreach (var se in StatusEffects)
                    itemFind += se.ItemFind;
                return itemFind;
            }
        }
        public float PreventDeathBonus
        {
            get
            {
                float pdb = CharClass.PreventDeathBonus;
                foreach (var se in StatusEffects)
                    pdb += se.PreventDeathBonus;
                return pdb;
            }
        }
        public float SuccessChance
        {
            get
            {
                float sc = CharClass.SuccessChance;
                foreach (var se in StatusEffects)
                    sc += se.SuccessChance;
                return sc;
            }
        }
        public int XpBonus
        {
            get
            {
                int xpb = CharClass.XpBonus;
                foreach (var se in StatusEffects)
                    xpb += se.XpBonus;
                return xpb;
            }
        }

        public IEquipped Equipped { get; }
        private int coins;
        public int Coins { get { return this.coins; } }

        public DateTime LastDailyGroupFinder { get; set; }
        private readonly IList<IItem> items;
        public IList<IItem> Items { get { return this.items; } }
        public int TotalItemCount { get { return this.items.Count; } }
        private readonly ISet<IStatusable> StatusEffects;
        public string Name { get; set; }
        private readonly IList<ILevelObserver> levelObservers;
        public bool InParty { get { return party != null; } }
        public IParty Party { get { return party; } }

        private IParty party;

        public Player(string id, string name, ICharClass charClass, int coins,
            DateTime lastDailyGroupFinder, IList<IItem> items,
            IDictionary<EquipSlot, IEquipment> equipped)
        {
            this.id = id;
            this.Name = name;
            this.charClass = charClass;
            this.coins = coins;
            this.LastDailyGroupFinder = lastDailyGroupFinder;
            this.items = items;
            this.StatusEffects = new HashSet<IStatusable>();
            this.Equipped = new Equipped(this, equipped);
            this.levelObservers = new List<ILevelObserver>();
        }

        public Player(string name, ICharClass charClass, int coins,
            DateTime lastDailyGroupFinder, IList<IItem> items,
            IDictionary<EquipSlot, IEquipment> equipped)
        {
            this.Name = name;
            this.charClass = charClass;
            this.coins = coins;
            this.LastDailyGroupFinder = lastDailyGroupFinder;
            this.items = items;
            this.StatusEffects = new HashSet<IStatusable>();
            this.Equipped = new Equipped(this, equipped);
            this.levelObservers = new List<ILevelObserver>();
        }

        public Player(string id, string name, int levelCap)
        {
            this.id = id;
            this.Name = name;
            this.charClass = new DeprivedCharClass(levelCap);
            this.coins = 0;
            this.LastDailyGroupFinder = DateTime.Now;
            this.items = new List<IItem>();
            this.StatusEffects = new HashSet<IStatusable>();
            this.Equipped = new Equipped(this);
            this.levelObservers = new List<ILevelObserver>();
        }

        public Player(string name, int levelCap)
        {
            this.Name = name;
            this.charClass = new DeprivedCharClass(levelCap);
            this.coins = 0;
            this.LastDailyGroupFinder = DateTime.Now;
            this.items = new List<IItem>();
            this.StatusEffects = new HashSet<IStatusable>();
            this.Equipped = new Equipped(this);
            this.levelObservers = new List<ILevelObserver>();
        }

        public void AddXP(int xp)
        {
            int before = charClass.Level;
            charClass.AddXp(xp);
            int after = charClass.Level;

            if (before != after)
                UpdateAll(before, after);
        }

        public void AddCoins(int coins)
        {
            this.coins += coins;
        }

        public void AddItem(IItem item) { this.items.Add(item); }
        public void RemoveItem(IItem item)
        {
            if (Equipped.IsEquipped(item))
                Equipped.Unequip(item);
            items.Remove(item);
        }

        public bool HasItem(IItem item)
        {
            return (Items.Contains(item) || Equipped.IsEquipped(item));
        }

        public void AddObserver(ILevelObserver o)
        {
            levelObservers.Add(o);
        }

        public void RemoveObserver(ILevelObserver o)
        {
            levelObservers.Remove(o);
        }

        private void UpdateAll(int oldValue, int newValue)
        {
            foreach (var o in levelObservers)
                o.Update(this, oldValue, newValue);
        }

        public void Register(IParty party)
        {
            if (!InParty)
                this.party = party;
        }

        public void Unregister()
        {
            this.party.Remove(this);
            this.party = null;
        }

        public bool IsToken(IPlayerToken token)
        {
            if (token.IsNameValid && token.Name.Equals(Name))
            {
                if (token.IsIDValid)
                {
                    if (string.IsNullOrEmpty(id))
                        this.id = token.ID;

                    return token.ID.Equals(id);
                }
                return true;
            }

            if (token.IsIDValid && !string.IsNullOrEmpty(id))
                return token.ID.Equals(id);
            return false;
        }

        public void Add(IStatusable status)
        {
            StatusEffects.Add(status);
        }

        public void ClearStatusEffects()
        {
            StatusEffects.Clear();
        }
    }

    public class ClassChoiceNotifier : ILevelObserver
    {
        private readonly ITwitchClient client;
        private readonly IPlayerFactory playerFactory;
        private readonly int classChoiceLevel;

        public ClassChoiceNotifier(ITwitchClient client, IPlayerFactory pf,
            int classChoiceLevel)
        {
            this.client = client;
            this.playerFactory = pf;
            this.classChoiceLevel = classChoiceLevel;
        }

        public void Update(IPlayer player, int oldValue, int newValue)
        {
            if (newValue == classChoiceLevel && oldValue < newValue)
            {
                StringBuilder sb = new StringBuilder("ATTENTION! You are high enough level ");
                sb.Append("to pick a class! You can choose your class by using !class ");
                sb.Append("[class name]. The choice of classes are: ");
                foreach (var cct in playerFactory.GetCharClassTypes())
                    sb.AppendFormat("{0}, ", cct.Name);
                sb.Remove(sb.Length - 2, 1);
                client.Whisper(player.Name, sb.ToString());
            }
        }
    }

    public interface IPlayerFactory
    {
        IPlayer Create(string user);
        IPlayer Create(string id, string user);
        IPlayer Create(IPlayer player, CharClassType type);
        ISet<CharClassType> GetCharClassTypes();
        ISet<ILevelObserver> GetCurrentDefaultObservers();
    }

    public class PlayerFactory : IPlayerFactory
    {
        private readonly int classChoiceLevel;
        private readonly int levelCap;

        private readonly CharClassConverter charClassConverter;
        private readonly ICharClassRepository charClassRepository;
        private readonly ISet<ILevelObserver> observers;

        public PlayerFactory(int classChoiceLevel, int levelCap)
        {
            this.classChoiceLevel = classChoiceLevel;
            this.levelCap = levelCap;
            this.charClassConverter = new CharClassConverter(classChoiceLevel, levelCap);
            this.charClassRepository = new LegacyCharClassRepository(classChoiceLevel,
                levelCap);
            this.observers = new HashSet<ILevelObserver>();
        }

        public PlayerFactory(int classChoiceLevel, int levelCap,
            params ILevelObserver[] observers) : this(classChoiceLevel, levelCap)
        {
            foreach (var o in observers)
            {
                this.observers.Add(o);
            }
        }

        public IPlayer Create(string user)
        {
            IPlayer player = new Player(user, classChoiceLevel);
            foreach (var o in observers)
                player.AddObserver(o);
            return player;
        }

        public IPlayer Create(string id, string user)
        {
            IPlayer player = new Player(id, user, classChoiceLevel);
            foreach (var o in observers)
                player.AddObserver(o);
            return player;

        }

        public IPlayer Create(IPlayer player, CharClassType type)
        {
            ICharClass icc = charClassRepository.GetById(type.ID);
            icc.AddXp(player.CharClass.PlayerLevelInfo.XP);

            return new Player(player.Name, icc, player.Coins, player.LastDailyGroupFinder,
                new List<IItem>(), new Dictionary<EquipSlot, IEquipment>());
        }

        public IPlayer Create(string user, CharClass cc, int xp, int coins)
        {
            ICharClass icc = charClassConverter.Convert(cc, xp);
            Dictionary<EquipSlot, IEquipment> dic = new Dictionary<EquipSlot, IEquipment>();
            IList<IItem> items = new List<IItem>();
            LegacyItemEquipmentConverter ef = new LegacyItemEquipmentConverter();
            cc.myItems.ForEach(item =>
            {

                IEquipment e = ef.Convert(item);
                if (e != null)
                    if (item.isActive)
                        dic.Add(e.EquipSlot, e);
                    else
                        items.Add(e);

            });

            IPlayer player = new Player(user, icc, coins, cc.lastDailyGroupFinder, items, dic);
            foreach (var o in observers)
                player.AddObserver(o);
            return player;
        }

        public ISet<ILevelObserver> GetCurrentDefaultObservers() { return observers; }

        public ISet<CharClassType> GetCharClassTypes()
        {
            var set = new HashSet<CharClassType>(CharClassConverter.LEGACY_CLASS_TYPE.Values);
            set.Remove(CharClassConverter.LEGACY_CLASS_TYPE[0]);
            return set;
        }
    }

    /// <summary>
    /// These are identifying tokens for players. 
    /// The token can optionally contain either the player ID or the player name. 
    /// There is no guarantee that the token will contain both but always one will
    /// contain valid data.
    /// It is the responsibility of the accepting object to what it accepts from these tokens 
    /// as some objects may only accept tokens with both ID and Name valid, 
    /// whereas some objects will accept either.
    /// </summary>
    public interface IPlayerToken
    {
        string ID { get; }
        string Name { get; }
        bool IsIDValid { get; }
        bool IsNameValid { get; }
    }

    public class PlayerToken : IPlayerToken
    {
        public string ID { get; }
        public bool IsIDValid { get { return !string.Empty.Equals(ID); } }
        public string Name { get; }
        public bool IsNameValid { get { return !string.Empty.Equals(Name); } }


        private PlayerToken(string id, string name)
        {
            this.ID = id;
            this.Name = name;
        }

        public static IPlayerToken of(string id, string name)
        {
            return new PlayerToken(id, name);
        }

        public static IPlayerToken of(string name)
        {
            return new PlayerToken(string.Empty, name);
        }
    }

    public interface IPlayerRepository
    {
        IList<IPlayer> All();
        /// <summary>
        /// This function is used to return an IPlayer with the matching data as is 
        /// in the token provided. 
        /// The implementation of this object may change what it required from the 
        /// tokens. 
        /// It should be noted that Twitch allows for name changes but ID are kept 
        /// the same, this however does overlook that the Legacy code for players did
        /// not store this ID, therefore it is recommended that regardless of implementation 
        /// that tokens provided should contain as much information as possible on their creation.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        IPlayer GetByToken(IPlayerToken token);
        void Save(IPlayer player);
        bool Save();
    }

    public class LegacyPlayerRepository : IPlayerRepository
    {

        public const string LEGACY_USER_COINS_FILE_PATH = "wolfcoins.json";
        public const string LEGACY_USER_XP_FILE_PATH = "XP.json";
        public const string LEGACY_USER_CLASS_FILE_PATH = "classData.json";

        private static IDictionary<string, int> LEGACY_USER_COINS;
        private static IDictionary<string, int> LEGACY_USER_XP;
        private static IDictionary<string, CharClass> LEGACY_USER_CHAR_CLASS;

        private static ISet<IPlayer> PLAYER_SET;
        private readonly int classChoiceLevel;
        private readonly int levelCap;
        private readonly string playerFile;

        public static IPlayerRepository getInstance(int classChoiceLevel, int levelCap,
            PlayerFactory pf, IEquipmentRepository er, string coinFile, string xpFile,
            string classFile, string playerFile)
        {
            if (PLAYER_SET != null)
                return new LegacyPlayerRepository(classChoiceLevel, levelCap, PLAYER_SET,
                    playerFile);

            if (File.Exists(playerFile))
            {

                PLAYER_SET = Convert(classChoiceLevel, levelCap, er, pf, JsonConvert
                    .DeserializeObject<ISet<PlayerDTO>>(File.ReadAllText(playerFile)));
                return new LegacyPlayerRepository(classChoiceLevel, levelCap, PLAYER_SET,
                    playerFile);
            }


            //PLAYERS = new Dictionary<string, IPlayer>();
            PLAYER_SET = new HashSet<IPlayer>();

            LEGACY_USER_COINS = JsonConvert
                .DeserializeObject<Dictionary<string, int>>(File.ReadAllText(coinFile));
            LEGACY_USER_XP = JsonConvert
                .DeserializeObject<Dictionary<string, int>>(File.ReadAllText(xpFile));
            LEGACY_USER_CHAR_CLASS = JsonConvert
                .DeserializeObject<Dictionary<string, CharClass>>(File.ReadAllText(classFile));

            foreach (var kv in LEGACY_USER_CHAR_CLASS)
            {
                IPlayer player = pf.Create(kv.Key, kv.Value, LEGACY_USER_XP[kv.Key], LEGACY_USER_COINS[kv.Key]);
                PLAYER_SET.Add(player);
            }

            return new LegacyPlayerRepository(classChoiceLevel, levelCap, PLAYER_SET,
                playerFile);

        }

        private LegacyPlayerRepository(int classChoiceLevel, int levelCap,
            ISet<IPlayer> players, string playerFile)
        {
            if (PLAYER_SET == null)
                PLAYER_SET = players;
            this.playerFile = playerFile;
            this.classChoiceLevel = classChoiceLevel;
            this.levelCap = levelCap;
        }

        public IList<IPlayer> All()
        {
            return new List<IPlayer>(PLAYER_SET);
        }

        public IPlayer GetByToken(IPlayerToken token)
        {
            return PLAYER_SET.SingleOrDefault(p =>
            {
                return p.IsToken(token);
            });

        }

        public void Save(IPlayer player)
        {
            if (!PLAYER_SET.Contains(player))
            {
                IPlayer pl = PLAYER_SET.SingleOrDefault(p => {
                    return p.Name.Equals(player.Name);
                });
                if (pl != null)
                    PLAYER_SET.Remove(pl);
                PLAYER_SET.Add(player);
            }

            CharClassConverter ccc = new CharClassConverter(classChoiceLevel, levelCap);
            KeyValuePair<CharClass, int> char_xp = ccc.Convert(player.CharClass);

            LEGACY_USER_CHAR_CLASS[player.Name] = char_xp.Key;
            LEGACY_USER_COINS[player.Name] = player.Coins;
            LEGACY_USER_XP[player.Name] = char_xp.Value;
        }

        public bool Save()
        {
            try
            {
                if (!File.Exists(playerFile))
                    using (File.Create(playerFile)) { }

                if (File.Exists(playerFile))
                {
                    var dtos = Convert(PLAYER_SET);
                    var players = JsonConvert.SerializeObject(dtos);
                    var data = Encoding.UTF8.GetBytes(players);
                    File.WriteAllBytes(playerFile, data);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Unable to save new version of " +
                    "players to the file {0}. Reverting back Legacy saving.\nError: {1}",
                    playerFile, ex));
            }

            try
            {
                var user_coins = JsonConvert.SerializeObject(LEGACY_USER_COINS);
                var user_xp = JsonConvert.SerializeObject(LEGACY_USER_COINS);
                var user_char = JsonConvert.SerializeObject(LEGACY_USER_CHAR_CLASS);

                var user_coins_bytes = Encoding.UTF8.GetBytes(user_coins);
                var user_xp_bytes = Encoding.UTF8.GetBytes(user_xp);
                var user_char_bytes = Encoding.UTF8.GetBytes(user_char);

                File.WriteAllBytes(LEGACY_USER_COINS_FILE_PATH, user_coins_bytes);
                File.WriteAllBytes(LEGACY_USER_XP_FILE_PATH, user_xp_bytes);
                File.WriteAllBytes(LEGACY_USER_CLASS_FILE_PATH, user_char_bytes);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            return false;
        }

        private static ISet<IPlayer> Convert(int classChoiceLevel, int levelCap,
            IEquipmentRepository equipmentRepository,
            IPlayerFactory playerFactory, ISet<PlayerDTO> dtos)
        {
            ISet<IPlayer> players = new HashSet<IPlayer>();
            foreach (var dto in dtos)
                players.Add(Convert(classChoiceLevel, levelCap, equipmentRepository,
                    playerFactory, dto));
            return players;
        }

        private static IPlayer Convert(int classChoiceLevel, int levelCap,
            IEquipmentRepository equipmentRepository,
            IPlayerFactory playerFactory, PlayerDTO dto)
        {
            PlayerDTO.CharClassDTO cc = dto.CharClass;
            ICharClass cci;
            if (cc.Type.ID == 0)
                cci = new DeprivedCharClass(classChoiceLevel);
            else
                cci = new CharClassImpl(cc.Type, cc.CoinBonus,
                cc.ItemFind, cc.Pets, cc.Prestige,
                cc.PreventDeathBonus, cc.SuccessChance,
                cc.XpBonus, levelCap, cc.Xp);

            IList<IItem> items = new List<IItem>();
            foreach (var item in dto.Items)
                items.Add(equipmentRepository.getById(item));

            var equipment = new Dictionary<EquipSlot, IEquipment>();
            foreach (var kvp in dto.Equipment)
                equipment.Add(kvp.Key, equipmentRepository.getById(kvp.Value));

            IPlayer player = new Player(dto.ID, dto.Name, cci, dto.Coins,
                dto.LastDailyGroupFinder, items, equipment);
            foreach (var o in playerFactory.GetCurrentDefaultObservers())
                player.AddObserver(o);

            return player;
        }

        private ISet<PlayerDTO> Convert(ISet<IPlayer> players)
        {
            ISet<PlayerDTO> dtos = new HashSet<PlayerDTO>();
            foreach (var player in players)
                dtos.Add(Convert(player));
            return dtos;
        }

        private PlayerDTO Convert(IPlayer player)
        {
            PlayerDTO.CharClassDTO ccdto = new PlayerDTO.CharClassDTO();
            ICharClass icc = player.CharClass;
            ccdto.Type = icc.CharClassType;
            ccdto.CoinBonus = icc.CoinBonus;
            ccdto.ItemFind = icc.ItemFind;
            ccdto.Pets = icc.MyPets;
            ccdto.Prestige = icc.Prestige;
            ccdto.PreventDeathBonus = icc.PreventDeathBonus;
            ccdto.SuccessChance = icc.SuccessChance;
            ccdto.XpBonus = icc.XpBonus;
            ccdto.Xp = icc.PlayerLevelInfo.XP;

            IList<long> items = new List<long>();
            foreach (var item in player.Items)
                items.Add(item.ID);

            var equipment = new Dictionary<EquipSlot, long>();

            foreach (var slot in player.Equipped.GetEquipped())
                equipment.Add(slot.Key, slot.Value.ID);


            PlayerDTO dto = new PlayerDTO();
            dto.ID = player.ID;
            dto.Name = player.Name;
            dto.CharClass = ccdto;
            dto.Coins = player.Coins;
            dto.LastDailyGroupFinder = player.LastDailyGroupFinder;
            dto.Items = items;
            dto.Equipment = equipment;

            return dto;

        }


        class PlayerDTO
        {
            public string ID { get; set; }
            public string Name { get; set; }
            public CharClassDTO CharClass { get; set; }
            public int Coins { get; set; }
            public DateTime LastDailyGroupFinder { get; set; }
            public IList<long> Items { get; set; }
            public IDictionary<EquipSlot, long> Equipment { get; set; }


            public class CharClassDTO
            {
                public CharClassType Type { get; set; }
                public int CoinBonus { get; set; }
                public int ItemFind { get; set; }
                public IList<Pet> Pets { get; set; }
                public int Prestige { get; set; }
                public float PreventDeathBonus { get; set; }
                public float SuccessChance { get; set; }
                public int XpBonus { get; set; }
                public int Xp { get; set; }
            }

        }
    }

    public interface IEquipped
    {
        IEquipment Equip(IEquipment e);
        IEquipment Peek(EquipSlot slot);
        IEquipment Unequip(EquipSlot slot);
        IEquipment Unequip(IItem item);
        bool IsEquipped(IItem item);
        IReadOnlyDictionary<EquipSlot, IEquipment> GetEquipped();
        ISet<EquipSlot> GetSlots();
    }

    class Equipped : IEquipped
    {
        private readonly IPlayer player;
        private readonly IDictionary<EquipSlot, IEquipment> slots;

        public Equipped(IPlayer player)
        {
            this.player = player;
            this.slots = new Dictionary<EquipSlot, IEquipment>();
        }

        public Equipped(IPlayer player, params IEquipment[] equipments) : this(player)
        {
            foreach (var e in equipments)
                slots.Add(e.EquipSlot, e);
        }

        public Equipped(IPlayer player, IDictionary<EquipSlot, IEquipment> slots)
        {
            this.player = player;
            this.slots = slots;
        }

        public IEquipment Peek(EquipSlot slot) { return slots[slot]; }

        public IEquipment Unequip(EquipSlot slot)
        {
            IEquipment e = slots[slot];
            slots[slot] = null;
            player.AddItem(e);
            return e;
        }

        public IEquipment Unequip(IItem item)
        {
            var kv = slots.SingleOrDefault(es => { return es.Value.Equals(item); });
            if (kv.Value == null)
                return null;

            slots[kv.Key] = null;
            player.AddItem(kv.Value);
            return kv.Value;
        }

        public IEquipment Equip(IEquipment e)
        {
            if (!player.HasItem(e))
                return null;
            player.RemoveItem(e);

            IEquipment ie = null;
            if (slots.ContainsKey(e.EquipSlot))
                ie = slots[e.EquipSlot];
            slots[e.EquipSlot] = e;
            if (ie != null)
                player.AddItem(ie);
            return ie;
        }

        public bool IsEquipped(IItem item)
        {
            return slots.Values.Any(e => (e != null && e.Equals(item)));
        }

        override public string ToString()
        {
            string s = "";
            foreach (var item in slots.Values)
                s += string.Format("{0} {1} [{2}] ", item.ID, item.Name, item.EquipSlot.Name);
            return s;
        }

        public IReadOnlyDictionary<EquipSlot, IEquipment> GetEquipped()
        {
            var equipped = (IReadOnlyDictionary<EquipSlot, IEquipment>)slots;
            return equipped;
        }

        public ISet<EquipSlot> GetSlots()
        {
            return new HashSet<EquipSlot>(slots.Keys);
        }
    }


}
