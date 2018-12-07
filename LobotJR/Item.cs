using Player;
using Status;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Equipment
{

    public interface IItem
    {
        long ID { get; }
        long InventoryID { get; }
        string Name { get; }
        string Description { get; }
        byte Rarirty { get; }

    }

    public class EquipSlot
    {
        public byte ID { get; }
        public string Name { get; }

        public EquipSlot(byte id, string name)
        {
            this.ID = id;
            this.Name = name;
        }

        new public bool Equals(Object o)
        {
            if (o == null || !(o is EquipSlot))
                return false;

            EquipSlot es = (EquipSlot)o;

            return (this.ID == es.ID &&
                this.Name.Equals(es.Name));
        }

    }

    public interface IEquipment : IItem, IStatusable
    {
        EquipSlot EquipSlot { get; }
        bool CanBeEquippedBy(CharClassType cct);
        CharClassType[] ForClasses { get; }
    }

    class Equipment : IEquipment
    {
        public long ID { get; }
        public long InventoryID { get; }
        public string Name { get; }
        public string Description { get; }
        public EquipSlot EquipSlot { get; }
        public byte Rarirty { get; }
        public CharClassType[] ForClasses { get; }

        public int ItemFind { get; }
        public float PreventDeathBonus { get; }
        public float SuccessChance { get; }
        public int XpBonus { get; }
        public int CoinBonus { get; }

        public Equipment(long id, long invId, string name, string desc, EquipSlot es, byte rarity, int itmfnd,
            float pdb, float sc, int xb, int cb, params CharClassType[] ccts)
        {
            this.ID = id;
            this.InventoryID = invId;
            this.Name = name;
            this.Description = desc;
            this.EquipSlot = es;
            this.Rarirty = rarity;
            this.ItemFind = itmfnd;
            this.PreventDeathBonus = pdb;
            this.SuccessChance = sc;
            this.XpBonus = xb;
            this.CoinBonus = cb;
            this.ForClasses = ccts;
        }

        public bool CanBeEquippedBy(CharClassType cct) { return ForClasses.Contains(cct); }

        new public bool Equals(Object o)
        {
            if (o == null || !(o is Equipment))
                return false;

            Equipment e = (Equipment)o;

            return (this.ID == e.ID &&
                this.InventoryID == e.InventoryID &&
                this.Name.Equals(e.Name) &&
                this.Description.Equals(e.Description) &&
                this.EquipSlot.Equals(e.EquipSlot) &&
                this.Rarirty == e.Rarirty &&
                this.ItemFind == e.ItemFind &&
                this.PreventDeathBonus == e.PreventDeathBonus &&
                this.SuccessChance == e.SuccessChance &&
                this.XpBonus == e.XpBonus &&
                this.CoinBonus == e.CoinBonus &&
                this.ForClasses.Equals(e.ForClasses));
        }

    }

    public class LegacyItemEquipmentConverter
    {
        public static IDictionary<int, EquipSlot> LEGACY_EQUIP_TYPE = new Dictionary<int, EquipSlot>()
        {
            {1, new EquipSlot(1, "Weapon") },
            {2, new EquipSlot(2, "Armor") },
            {3, new EquipSlot(3, "Trinket") },
            {4, new EquipSlot(4, "Other") }
        };



        public IEquipment Convert(Item item)
        {
            EquipSlot es = LEGACY_EQUIP_TYPE[item.itemType];
            CharClassType cct = CharClassConverter.LEGACY_CLASS_TYPE[item.forClass];

            return new Equipment(item.itemID, item.inventoryID, item.itemName, item.description, es,
                (byte)item.itemRarity, item.itemFind, item.preventDeathBonus, item.successChance, item.xpBonus,
                item.coinBonus, cct);
        }

        public Item Convert(IEquipment equipment)
        {
            Item item = new Item();
            item.itemID = (int)equipment.ID;
            item.inventoryID = (int)equipment.InventoryID;
            item.itemName = equipment.Name;
            item.description = equipment.Description;
            item.itemType = equipment.EquipSlot.ID;
            item.itemRarity = equipment.Rarirty;
            item.itemFind = equipment.ItemFind;
            item.preventDeathBonus = equipment.PreventDeathBonus;
            item.successChance = equipment.SuccessChance;
            item.xpBonus = equipment.XpBonus;
            item.coinBonus = equipment.CoinBonus;
            item.forClass = equipment.ForClasses[0].ID;
            return item;
        }
    }

    public interface IItemRepository
    {
        IItem getById(long id);

    }

    public interface IEquipmentRepository : IItemRepository
    {
        new IEquipment getById(long id);
    }

    public class LegacyEquipmentRepository : IEquipmentRepository
    {

        public const string LEGACY_ITEM_BRIDGE_FILE_PATH = "content/itemlist.ini";
        public const string LEGACY_ITEM_PREFIX_FILE_PATH = "content/items/";

        private static IDictionary<long, IEquipment> EQUIPMENT;

        private LegacyEquipmentRepository(IDictionary<long, IEquipment> equipment)
        {
            if (EQUIPMENT == null)
                EQUIPMENT = equipment;
        }

        public static LegacyEquipmentRepository getInstance(LegacyItemEquipmentConverter liec,
            string bridgeFile, string itemFilePrefix)
        {
            IDictionary<long, IEquipment> equipment = new Dictionary<long, IEquipment>();

            var itemDatabase = new Dictionary<int, Item>();
            var itemId_File_Bridge = new Dictionary<int, string>();
            int itemIter = 1;
            IEnumerable<string> fileText = System.IO.File.ReadLines(bridgeFile, UTF8Encoding.Default);
            foreach (var line in fileText)
            {
                string[] temp = line.Split(',');
                int id = -1;
                int.TryParse(temp[0], out id);
                if (id != -1)
                    itemId_File_Bridge.Add(id, itemFilePrefix + temp[1]);
                else
                    Console.WriteLine("Invalid item read on line " + itemIter);
                itemIter++;
            }

            itemIter = 0;
            foreach (var item in itemId_File_Bridge)
            {
                Item myItem = new Item();
                int parsedInt = -1;
                int line = 0;
                string[] temp = { "" };
                fileText = System.IO.File.ReadLines(itemId_File_Bridge.ElementAt(itemIter).Value, UTF8Encoding.Default);
                // item ID
                myItem.itemID = itemId_File_Bridge.ElementAt(itemIter).Key;
                // item name
                temp = fileText.ElementAt(line).Split('=');
                myItem.itemName = temp[1];
                line++;
                // item type (1=weapon, 2=armor, 3=other)
                temp = fileText.ElementAt(line).Split('=');
                int.TryParse(temp[1], out parsedInt);
                myItem.itemType = parsedInt;
                line++;
                // Class designation (1=Warrior,2=Mage,3=Rogue,4=Ranger,5=Cleric)
                temp = fileText.ElementAt(line).Split('=');
                int.TryParse(temp[1], out parsedInt);
                myItem.forClass = parsedInt;
                line++;
                // Item rarity (1=Uncommon,2=Rare,3=Epic,4=Artifact)
                temp = fileText.ElementAt(line).Split('=');
                int.TryParse(temp[1], out parsedInt);
                myItem.itemRarity = parsedInt;
                line++;
                // success boost (%)
                temp = fileText.ElementAt(line).Split('=');
                int.TryParse(temp[1], out parsedInt);
                myItem.successChance = parsedInt;
                line++;
                // item find (%)
                temp = fileText.ElementAt(line).Split('=');
                int.TryParse(temp[1], out parsedInt);
                myItem.itemFind = parsedInt;
                line++;
                // coin boost (%)
                temp = fileText.ElementAt(line).Split('=');
                int.TryParse(temp[1], out parsedInt);
                myItem.coinBonus = parsedInt;
                line++;
                // xp boost (%)
                temp = fileText.ElementAt(line).Split('=');
                int.TryParse(temp[1], out parsedInt);
                myItem.xpBonus = parsedInt;
                line++;
                // prevent death boost (%)
                temp = fileText.ElementAt(line).Split('=');
                int.TryParse(temp[1], out parsedInt);
                myItem.preventDeathBonus = parsedInt;
                line++;
                // item description
                temp = fileText.ElementAt(line).Split('=');
                myItem.description = temp[1];

                itemDatabase.Add(itemIter, myItem);

                itemIter++;
            }

            foreach (var item in itemDatabase.Values)
            {
                IEquipment e = liec.Convert(item);
                equipment.Add(e.ID, e);
            }

            return new LegacyEquipmentRepository(equipment);

        }

        public IEquipment getById(long id)
        {
            return EQUIPMENT[id];
        }

        IItem IItemRepository.getById(long id)
        {
            return getById(id);
        }
    }

}