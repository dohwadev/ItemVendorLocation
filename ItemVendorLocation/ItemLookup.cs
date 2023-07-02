﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Logging;
using ItemVendorLocation.Models;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel;
using Lumina.Excel.GeneratedSheets;

namespace ItemVendorLocation
{
#if DEBUG
    public class ItemLookup
#else
    internal class ItemLookup
#endif
    {
        private readonly ExcelSheet<CustomTalk> _customTalks;
        private readonly ExcelSheet<Achievement> _achievements;
        private readonly ExcelSheet<CustomTalkNestHandlers> _customTalkNestHandlers;

        private readonly ExcelSheet<ENpcBase> _eNpcBases;
        private readonly ExcelSheet<ENpcResident> _eNpcResidents;

        private readonly ExcelSheet<FateShopCustom> _fateShops;
        private readonly ExcelSheet<GilShopItem> _gilShopItems;
        private readonly ExcelSheet<GilShop> _gilShops;
        private readonly ExcelSheet<SpecialShopCustom> _specialShops;
        private readonly ExcelSheet<GCShop> _gcShops;
        private readonly ExcelSheet<GCScripShopItem> _gcScripShopItems;
        private readonly ExcelSheet<GCScripShopCategory> _gcScripShopCategories;
        private readonly ExcelSheet<InclusionShop> _inclusionShops;
        private readonly ExcelSheet<InclusionShopSeriesCustom> _inclusionShopSeries;
        private readonly ExcelSheet<FccShop> _fccShops;
        private readonly ExcelSheet<PreHandler> _preHandlers;
        private readonly ExcelSheet<TopicSelect> _topicSelects;

        private readonly ExcelSheet<TerritoryType> _territoryType;

        private readonly Item _gil;
        private readonly ExcelSheet<Item> _items;
        private readonly List<Item> _gcSeal;
        private readonly Addon _fccName;

        private readonly Dictionary<uint, ItemInfo> _itemDataMap = new();
        private readonly Dictionary<uint, NpcLocation> _npcLocations = new();

        private bool _isDataReady;

        private readonly Dictionary<uint, uint> _shbFateShopNpc = new()
        {
            { 1027998, 1769957 },
            { 1027538, 1769958 },
            { 1027385, 1769959 },
            { 1027497, 1769960 },
            { 1027892, 1769961 },
            { 1027665, 1769962 },
            { 1027709, 1769963 },
            { 1027766, 1769964 },
        };

        private readonly uint FirstSpecialShopId;
        private readonly uint LastSpecialShopId;


        public ItemLookup()
        {
            _eNpcBases = Service.DataManager.GetExcelSheet<ENpcBase>();
            _eNpcResidents = Service.DataManager.GetExcelSheet<ENpcResident>();
            _gilShopItems = Service.DataManager.GetExcelSheet<GilShopItem>();
            _gilShops = Service.DataManager.GetExcelSheet<GilShop>();
            _specialShops = Service.DataManager.GetExcelSheet<SpecialShopCustom>();
            _customTalks = Service.DataManager.GetExcelSheet<CustomTalk>();
            _customTalkNestHandlers = Service.DataManager.GetExcelSheet<CustomTalkNestHandlers>();
            _fateShops = Service.DataManager.GetExcelSheet<FateShopCustom>();
            _territoryType = Service.DataManager.GetExcelSheet<TerritoryType>();

            _gcScripShopItems = Service.DataManager.GetExcelSheet<GCScripShopItem>();
            _gcShops = Service.DataManager.GetExcelSheet<GCShop>();
            _gcScripShopCategories = Service.DataManager.GetExcelSheet<GCScripShopCategory>();

            _inclusionShops = Service.DataManager.GetExcelSheet<InclusionShop>();
            _inclusionShopSeries = Service.DataManager.GetExcelSheet<InclusionShopSeriesCustom>();
            _fccShops = Service.DataManager.GetExcelSheet<FccShop>();
            _preHandlers = Service.DataManager.GetExcelSheet<PreHandler>();
            _topicSelects = Service.DataManager.GetExcelSheet<TopicSelect>();

            _achievements = Service.DataManager.GetExcelSheet<Achievement>();

            _fccName = Service.DataManager.GetExcelSheet<Addon>().GetRow(102233);

            _items = Service.DataManager.GetExcelSheet<Item>();
            _gil = _items.GetRow(1);

            _gcSeal = _items.Where(i => i.RowId is >= 20 and <= 22).Select(i => i).ToList();

            FirstSpecialShopId = _specialShops.First().RowId;
            LastSpecialShopId = _specialShops.Last().RowId;

            _ = Task.Run(async () =>
            {
                while (!Service.DataManager.IsDataReady)
                {
                    await Task.Delay(500);
                }

                BuildNpcLocation();
                BuildVendors();
                AddAchievementItem();
                _isDataReady = true;
#if DEBUG
                Dictionary<string, uint> noLocationNpcs = new();
                foreach (KeyValuePair<uint, ItemInfo> items in _itemDataMap)
                {
                    foreach (NpcInfo npc in items.Value.NpcInfos)
                    {
                        if (npc.Location == null)
                        {
                            if (!noLocationNpcs.TryAdd(npc.Name, 1))
                            {
                                noLocationNpcs[npc.Name]++;
                            }
                        }
                    }
                }
                PluginLog.Debug("Data is ready");
                PluginLog.Debug($"Items sold by NPCs with no location: {noLocationNpcs.Values.Aggregate((sum, i) => sum += i)}");
                PluginLog.Debug("Named NPCs:");
                foreach (KeyValuePair<string, uint> npc in noLocationNpcs)
                {
                    if (char.IsUpper(npc.Key.First()))
                    {
                        PluginLog.Debug($"{npc.Key} sells {npc.Value} items");
                    }
                }
                PluginLog.Debug("Unnamed NPCs:");
                foreach (KeyValuePair<string, uint> npc in noLocationNpcs)
                {
                    if (!char.IsUpper(npc.Key.First()))
                    {
                        PluginLog.Debug($"{npc.Key} sells {npc.Value} items");
                    }
                }
#endif
            });
        }


        // https://discord.com/channels/581875019861328007/653504487352303619/860865002721247261
        // https://github.com/SapphireServer/Sapphire/blob/a5c15f321f7e795ed7362ae15edaada99ca7d9be/src/world/Manager/EventMgr.cpp#L14
        private static bool MatchEventHandlerType(uint data, EventHandlerType type)
        {
            return ((data >> 16) & (uint)type) == (uint)type;
        }

#if DEBUG
        public void BuildDebugVendorInfo(uint vendorId)
        {
            NpcLocation npcLocation = _npcLocations[vendorId];

            ENpcBase npcBase = _eNpcBases.GetRow(vendorId);
            if (npcBase == null)
            {
                return;
            }

            BuildVendorInfo(npcBase);
        }
#endif

        private void BuildVendors()
        {
            

            foreach (ENpcBase npcBase in _eNpcBases)
            {
                if (npcBase == null)
                {
                    continue;
                }
                BuildVendorInfo(npcBase);
                
            }
        }

        private void BuildVendorInfo(ENpcBase npcBase)
        {
            ENpcResident resident = _eNpcResidents.GetRow(npcBase.RowId);

            if (HackyFix_Npc(npcBase, resident))
            {
                return;
            }

            FateShopCustom fateShop = _fateShops.GetRow(npcBase.RowId);
            if (fateShop != null)
            {
                foreach (LazyRow<SpecialShop> specialShop in fateShop.SpecialShop)
                {
                    if (specialShop.Value == null)
                    {
                        continue;
                    }

                    SpecialShopCustom specialShopCustom = _specialShops.GetRow(specialShop.Row);
                    AddSpecialItem(specialShopCustom, npcBase, resident);
                }

                return;
            }

            foreach (uint npcData in npcBase.ENpcData)
            {
                if (npcData == 0)
                {
                    continue;
                }

                InclusionShop inclusionShop = _inclusionShops.GetRow(npcData);
                FccShop fccShop = _fccShops.GetRow(npcData);
                PreHandler preHandler = _preHandlers.GetRow(npcData);
                TopicSelect topicSelect = _topicSelects.GetRow(npcData);

                AddInclusionShop(inclusionShop, npcBase, resident);
                AddFccShop(fccShop, npcBase, resident);
                AddItemsInPrehandler(preHandler, npcBase, resident);
                AddItemsInTopicSelect(topicSelect, npcBase, resident);

                if (MatchEventHandlerType(npcData, EventHandlerType.GcShop))
                {
                    GCShop gcShop = _gcShops.GetRow(npcData);
                    AddGcShopItem(gcShop, npcBase, resident);
                    continue;
                }

                if (MatchEventHandlerType(npcData, EventHandlerType.SpecialShop))
                {
                    SpecialShopCustom specialShop = _specialShops.GetRow(npcData);
                    AddSpecialItem(specialShop, npcBase, resident);
                    continue;
                }

                if (MatchEventHandlerType(npcData, EventHandlerType.GilShop))
                {
                    GilShop gilShop = _gilShops.GetRow(npcData);
                    AddGilShopItem(gilShop, npcBase, resident);
                }

                if (MatchEventHandlerType(npcData, EventHandlerType.CustomTalk))
                {
                    CustomTalk customTalk = _customTalks.GetRow(npcData);
                    if (customTalk == null)
                    {
                        continue;
                    }

                    if (customTalk.SpecialLinks != 0)
                    {
                        try
                        {
                            for (uint index = 0; index <= 30; index++)
                            {
                                CustomTalkNestHandlers customTalkNestHandler = _customTalkNestHandlers.GetRow(customTalk.SpecialLinks, index);
                                if (customTalkNestHandler != null)
                                {
                                    SpecialShopCustom specialShop = _specialShops.GetRow(customTalkNestHandler.NestHandler);
                                    if (specialShop != null)
                                    {
                                        AddSpecialItem(specialShop, npcBase, resident);
                                    }
                                    GilShop gilShop = _gilShops.GetRow(customTalkNestHandler.NestHandler);
                                    if (gilShop != null)
                                    {
                                        AddGilShopItem(gilShop, npcBase, resident);
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    foreach (uint arg in customTalk.ScriptArg)
                    {
                        if (MatchEventHandlerType(arg, EventHandlerType.GilShop))
                        {
                            GilShop gilShop = _gilShops.GetRow(arg);
                            AddGilShopItem(gilShop, npcBase, resident);
                            continue;
                        }

                        if (MatchEventHandlerType(arg, EventHandlerType.FcShop))
                        {
                            FccShop shop = _fccShops.GetRow(arg);
                            AddFccShop(shop, npcBase, resident);
                            continue;
                        }

                        if (arg < FirstSpecialShopId || arg > LastSpecialShopId)
                        {
                            continue;
                        }

                        SpecialShopCustom specialShop = _specialShops.GetRow(arg);
                        AddSpecialItem(specialShop, npcBase, resident);
                    }
                }
            }
        }

        private void AddSpecialItem(SpecialShopCustom specialShop, ENpcBase npcBase, ENpcResident resident, ItemType type = ItemType.SpecialShop, string shop = null)
        {
            if (specialShop == null)
            {
                return;
            }

            foreach (SpecialShopCustom.Entry entry in specialShop.Entries)
            {
                if (entry.Result == null || entry.Cost == null)
                {
                    continue;
                }

                foreach (SpecialShopCustom.ResultEntry result in entry.Result)
                {
                    if (result.Item.Value == null)
                    {
                        continue;
                    }

                    if (result.Item.Value.Name == string.Empty)
                    {
                        continue;
                    }

                    List<Tuple<uint, string>> costs = (from e in entry.Cost where e.Item != null && e.Item.Value.Name != string.Empty select new Tuple<uint, string>(e.Count, e.Item.Value.Name)).ToList();

                    string achievementDescription = "";
                    if (type == ItemType.Achievement)
                    {
                        achievementDescription = _achievements.Where(i => i.Item.Value == result.Item.Value).Select(i => i.Description).First();
                    }

                    AddItem_Internal(result.Item.Value.RowId, result.Item.Value.Name, npcBase.RowId, resident.Singular, shop,
                        costs, _npcLocations.TryGetValue(npcBase.RowId, out NpcLocation value) ? value : null, type, achievementDescription);
                }
            }
        }

        private void AddGilShopItem(GilShop gilShop, ENpcBase npcBase, ENpcResident resident, string shop = null)
        {
            if (gilShop == null)
            {
                return;
            }

            for (uint i = 0u; ; i++)
            {
                try
                {
                    GilShopItem item = _gilShopItems.GetRow(gilShop.RowId, i);

                    if (item?.Item.Value == null)
                    {
                        break;
                    }


                    AddItem_Internal(item.Item.Value.RowId, item.Item.Value.Name, npcBase.RowId, resident.Singular,
                        shop != null ? $"{shop}\n{gilShop.Name}" : gilShop.Name,
                        new List<Tuple<uint, string>> { new(item.Item.Value.PriceMid, _gil.Name) },
                        _npcLocations.TryGetValue(npcBase.RowId, out NpcLocation value) ? value : null, ItemType.GilShop);
                }
                catch (Exception)
                {
                    break;
                }
            }
        }

        private void AddGcShopItem(GCShop gcId, ENpcBase npcBase, ENpcResident resident)
        {
            if (gcId == null)
            {
                return;
            }

            List<GCScripShopCategory> categories = _gcScripShopCategories.Where(i => i.GrandCompany.Row == gcId.GrandCompany.Row).ToList();
            if (categories.Count == 0)
            {
                return;
            }

            Item seal = _gcSeal.Find(i => i.Description.RawString.EndsWith($"{gcId.GrandCompany.Value.Name.RawString}."));
            if (seal == null)
            {
                return;
            }

            foreach (GCScripShopCategory category in categories)
            {
                for (uint i = 0u; ; i++)
                {
                    try
                    {
                        GCScripShopItem item = _gcScripShopItems.GetRow(category.RowId, i);
                        if (item == null)
                        {
                            break;
                        }

                        if (item.SortKey == 0)
                        {
                            break;
                        }

                        AddItem_Internal(item.Item.Value.RowId, item.Item.Value.Name, npcBase.RowId, resident.Singular, null, new List<Tuple<uint, string>> { new(item.CostGCSeals, seal.Name) },
                            _npcLocations.TryGetValue(npcBase.RowId, out NpcLocation value) ? value : null, ItemType.GcShop);
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
            }
        }

        private void AddInclusionShop(InclusionShop inclusionShop, ENpcBase npcBase, ENpcResident resident)
        {
            if (inclusionShop == null)
            {
                return;
            }

            foreach (LazyRow<InclusionShopCategory> category in inclusionShop.Category)
            {
                if (category.Value.RowId == 0)
                {
                    continue;
                }

                for (uint i = 0; ; i++)
                {
                    try
                    {
                        InclusionShopSeriesCustom series = _inclusionShopSeries.GetRow(category.Value.InclusionShopSeries.Row, i);
                        if (series == null)
                        {
                            break;
                        }

                        SpecialShopCustom specialShop = series.SpecialShopCustoms.Value;
                        AddSpecialItem(specialShop, npcBase, resident, shop: $"{category.Value.Name}\n{specialShop?.Name}");
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
            }
        }

        private void AddFccShop(FccShop shop, ENpcBase npcBase, ENpcResident resident)
        {
            if (shop == null)
            {
                return;
            }

            for (int i = 0; i < shop.Item.Length; i++)
            {
                Item item = _items.GetRow(shop.Item[i]);
                if (item == null || item.Name == string.Empty)
                {
                    continue;
                }

                uint cost = shop.Cost[i];

                AddItem_Internal(item.RowId, item.Name, npcBase.RowId, resident.Singular, null, new List<Tuple<uint, string>> { new(cost, _fccName.Text) },
                    _npcLocations.TryGetValue(npcBase.RowId, out NpcLocation value) ? value : null, ItemType.FcShop);
            }
        }

        private void AddItemsInPrehandler(PreHandler preHandler, ENpcBase npcBase, ENpcResident resident)
        {
            if (preHandler == null)
            {
                return;
            }

            uint target = preHandler.Target;
            if (target == 0)
            {
                return;
            }

            if (MatchEventHandlerType(target, EventHandlerType.GilShop))
            {
                GilShop gilShop = _gilShops.GetRow(target);
                AddGilShopItem(gilShop, npcBase, resident);
                return;
            }

            if (MatchEventHandlerType(target, EventHandlerType.SpecialShop))
            {
                SpecialShopCustom specialShop = _specialShops.GetRow(target);
                AddSpecialItem(specialShop, npcBase, resident);
                return;
            }

            InclusionShop inclusionShop = _inclusionShops.GetRow(target);
            AddInclusionShop(inclusionShop, npcBase, resident);
        }

        private void AddItemsInTopicSelect(TopicSelect topicSelect, ENpcBase npcBase, ENpcResident resident)
        {
            if (topicSelect == null)
            {
                return;
            }

            foreach (uint data in topicSelect.Shop)
            {
                if (data == 0)
                {
                    continue;
                }

                if (MatchEventHandlerType(data, EventHandlerType.SpecialShop))
                {
                    SpecialShopCustom specialShop = _specialShops.GetRow(data);
                    AddSpecialItem(specialShop, npcBase, resident, shop: topicSelect.Name);

                    continue;
                }

                if (MatchEventHandlerType(data, EventHandlerType.GilShop))
                {
                    GilShop gilShop = _gilShops.GetRow(data);
                    AddGilShopItem(gilShop, npcBase, resident, shop: topicSelect.Name);
                    continue;
                }

                PreHandler preHandler = _preHandlers.GetRow(data);
                AddItemsInPrehandler(preHandler, npcBase, resident);
            }
        }

        private bool HackyFix_Npc(ENpcBase npcBase, ENpcResident resident)
        {
            switch (npcBase.RowId)
            {
                case 1018655: // disreputable priest
                    AddSpecialItem(_specialShops.GetRow(1769743), npcBase, resident);
                    AddSpecialItem(_specialShops.GetRow(1769744), npcBase, resident);
                    AddSpecialItem(_specialShops.GetRow(1770537), npcBase, resident);
                    return true;

                case 1016289: // syndony
                    AddSpecialItem(_specialShops.GetRow(1769635), npcBase, resident);
                    return true;

                case 1025047: // gerolt but in eureka
                    for (uint i = 1769820; i <= 1769834; i++)
                    {
                        SpecialShopCustom specialShop = _specialShops.GetRow(i);
                        AddSpecialItem(specialShop, npcBase, resident);
                    }

                    return true;

                case 1025763: // doman junkmonger
                    GilShop gilShop = _gilShops.GetRow(262919);
                    AddGilShopItem(gilShop, npcBase, resident);
                    return true;

                case 1027123: // eureka expedition artisan
                    AddSpecialItem(_specialShops.GetRow(1769934), npcBase, resident);
                    AddSpecialItem(_specialShops.GetRow(1769935), npcBase, resident);
                    return true;

                case 1027124: // eureka expedition scholar
                    AddSpecialItem(_specialShops.GetRow(1769937), npcBase, resident);
                    return true;


                case 1033921: // faux
                    SpecialShopCustom sShop = _specialShops.GetRow(1770282);
                    AddSpecialItem(sShop, npcBase, resident);
                    return true;

                case 1034007: // bozja
                case 1036895:
                    SpecialShopCustom specShop = _specialShops.GetRow(1770087);
                    AddSpecialItem(specShop, npcBase, resident);
                    return true;

                default:
                    if (_shbFateShopNpc.TryGetValue(npcBase.RowId, out uint value))
                    {
                        SpecialShopCustom specialShop = _specialShops.GetRow(value);
                        AddSpecialItem(specialShop, npcBase, resident);
                        return true;
                    }

                    return false;
            }
        }

        private void AddAchievementItem()
        {
            for (uint i = 1006004u; i <= 1006006; i++)
            {
                ENpcBase npcBase = _eNpcBases.GetRow(i);
                ENpcResident resident = _eNpcResidents.GetRow(i);

                for (uint j = 1769898u; j <= 1769906; j++)
                {
                    AddSpecialItem(_specialShops.GetRow(j), npcBase, resident, ItemType.Achievement);
                }
            }
        }

        private void AddItem_Internal(uint itemId, string itemName, uint npcId, string npcName, string shopName, List<Tuple<uint, string>> cost, NpcLocation npcLocation, ItemType type,
            string achievementDesc = "")
        {
            if (itemId == 0)
            {
                return;
            }

            if (!_itemDataMap.ContainsKey(itemId))
            {
                _itemDataMap.Add(itemId, new ItemInfo
                {
                    Id = itemId,
                    Name = itemName,
                    NpcInfos = new List<NpcInfo> { new() { Id = npcId, Location = npcLocation, Costs = cost, Name = npcName, ShopName = shopName } },
                    Type = type,
                    AchievementDescription = achievementDesc,
                });
                return;
            }

            if (!_itemDataMap.TryGetValue(itemId, out ItemInfo itemInfo))
            {
                _ = _itemDataMap.TryAdd(itemId, itemInfo = new ItemInfo
                {
                    Id = itemId,
                    Name = itemName,
                    NpcInfos = new List<NpcInfo> { new() { Id = npcId, Location = npcLocation, Costs = cost, Name = npcName } },
                    Type = type,
                    AchievementDescription = achievementDesc,
                });
            }

            if (type == ItemType.Achievement && itemInfo.Type != ItemType.Achievement)
            {
                itemInfo.Type = ItemType.Achievement;
                itemInfo.AchievementDescription = achievementDesc;
            }

            List<NpcInfo> npcs = itemInfo.NpcInfos;
            if (npcs.Find(j => j.Id == npcId) == null)
            {
                npcs.Add(new NpcInfo { Id = npcId, Location = npcLocation, Name = npcName, Costs = cost, ShopName = shopName });
            }

            itemInfo.NpcInfos = npcs;
        }

        // https://github.com/ufx/GarlandTools/blob/3b3475bca6f95c800d2454f2c09a3f1eea0a8e4e/Garland.Data/Modules/Territories.cs
        private void BuildNpcLocation()
        {
            foreach (TerritoryType sTerritoryType in _territoryType)
            {
                string bg = sTerritoryType.Bg.ToString();
                if (string.IsNullOrEmpty(bg))
                {
                    continue;
                }

                string lgbFileName = "bg/" + bg[..(bg.IndexOf("/level/", StringComparison.Ordinal) + 1)] + "level/planevent.lgb";
                LgbFile sLgbFile = Service.DataManager.GetFile<LgbFile>(lgbFileName);
                if (sLgbFile == null)
                {
                    continue;
                }

                foreach (LayerCommon.Layer sLgbGroup in sLgbFile.Layers)
                {
                    foreach (LayerCommon.InstanceObject instanceObject in sLgbGroup.InstanceObjects)
                    {
                        if (instanceObject.AssetType != LayerEntryType.EventNPC)
                        {
                            continue;
                        }

                        LayerCommon.ENPCInstanceObject eventNpc = (LayerCommon.ENPCInstanceObject)instanceObject.Object;
                        uint npcRowId = eventNpc.ParentData.ParentData.BaseId;
                        if (npcRowId == 0)
                        {
                            continue;
                        }

                        if (_npcLocations.ContainsKey(npcRowId))
                        {
                            continue;
                        }

                        _npcLocations.Add(npcRowId, new NpcLocation(instanceObject.Transform.Translation.X, instanceObject.Transform.Translation.Z, sTerritoryType));
                    }
                }
            }

            ExcelSheet<Level> levels = Service.DataManager.GetExcelSheet<Level>();
            foreach (Level level in levels)
            {
                // NPC
                if (level.Type != 8)
                {
                    continue;
                }

                // NPC Id
                if (_npcLocations.ContainsKey(level.Object))
                {
                    continue;
                }

                if (level.Territory.Value == null)
                {
                    continue;
                }

                _npcLocations.Add(level.Object, new NpcLocation(level.X, level.Z, level.Territory.Value));
            }

            // https://github.com/ufx/GarlandTools/blob/7b38def8cf0ab553a2c3679aec86480c0e4e9481/Garland.Data/Modules/NPCs.cs#L59-L66
            TerritoryType corrected = _territoryType.GetRow(698);
            _npcLocations[1004418].TerritoryExcel = corrected;
            _npcLocations[1006747].TerritoryExcel = corrected;
            _npcLocations[1002299].TerritoryExcel = corrected;
            _npcLocations[1002281].TerritoryExcel = corrected;
            _npcLocations[1001766].TerritoryExcel = corrected;
            _npcLocations[1001945].TerritoryExcel = corrected;
            _npcLocations[1001821].TerritoryExcel = corrected;

#pragma warning disable format
            // Fix Kugane npcs location
            TerritoryType kugane = _territoryType.GetRow(641);
            _npcLocations[1019100] = new NpcLocation(-85.03851f,    117.05188f, kugane);
            _npcLocations[1022846] = new NpcLocation(-83.93994f,    115.31238f, kugane);
            _npcLocations[1019106] = new NpcLocation(-99.22949f,    105.6687f,  kugane);
            _npcLocations[1019107] = new NpcLocation(-100.26703f,   107.43872f, kugane);
            _npcLocations[1019104] = new NpcLocation(-67.582275f,   59.739014f, kugane);
            _npcLocations[1019102] = new NpcLocation(-59.617065f,   33.524048f, kugane);
            _npcLocations[1019103] = new NpcLocation(-52.35376f,    76.58496f,  kugane);
            _npcLocations[1019101] = new NpcLocation(-36.484375f,   49.240845f, kugane);

            // random NPC fixes
            _ = _npcLocations[1004418] = new NpcLocation(-114.0307f, 118.30322f, _territoryType.GetRow(131), 73);

            // some are missing from my test, so we gotta hardcode them
            _ = _npcLocations.TryAdd(1006004, new NpcLocation(5.355835f,    155.22998f,     _territoryType.GetRow(128)));
            _ = _npcLocations.TryAdd(1017613, new NpcLocation(2.822865f,    153.521f,       _territoryType.GetRow(128)));
            _ = _npcLocations.TryAdd(1003077, new NpcLocation(-259.32715f,  37.491333f,     _territoryType.GetRow(129)));

            _ = _npcLocations.TryAdd(1008145, new NpcLocation(-31.265808f,  -245.38031f,    _territoryType.GetRow(133)));
            _ = _npcLocations.TryAdd(1006005, new NpcLocation(-61.234497f,  -141.31384f,    _territoryType.GetRow(133)));
            _ = _npcLocations.TryAdd(1017614, new NpcLocation(-58.79309f,   -142.1073f,     _territoryType.GetRow(133)));
            _ = _npcLocations.TryAdd(1003633, new NpcLocation(145.83044f,   -106.767456f,   _territoryType.GetRow(133)));

            // more locations missing
            _ = _npcLocations.TryAdd(1000215, new NpcLocation(155.35205f,   -70.26782f,     _territoryType.GetRow(133)));
            _ = _npcLocations.TryAdd(1000996, new NpcLocation(-28.152893f,  196.70398f,     _territoryType.GetRow(128)));
            _ = _npcLocations.TryAdd(1000999, new NpcLocation(-29.465149f,  197.92468f,     _territoryType.GetRow(128)));
            _ = _npcLocations.TryAdd(1000217, new NpcLocation(170.30591f,   -73.16705f,     _territoryType.GetRow(133)));
            _ = _npcLocations.TryAdd(1000597, new NpcLocation(-163.07324f,  -78.62976f,     _territoryType.GetRow(153)));
            _ = _npcLocations.TryAdd(1000185, new NpcLocation(-8.590881f,   -2.2125854f,    _territoryType.GetRow(132)));
            _ = _npcLocations.TryAdd(1000392, new NpcLocation(-17.746277f,  43.35083f,      _territoryType.GetRow(132)));
            _ = _npcLocations.TryAdd(1000391, new NpcLocation(66.819214f,   -143.45007f,    _territoryType.GetRow(133)));
            _ = _npcLocations.TryAdd(1000232, new NpcLocation(164.72107f,   -133.68433f,    _territoryType.GetRow(133)));
            _ = _npcLocations.TryAdd(1000301, new NpcLocation(-87.174866f,  -173.51044f,    _territoryType.GetRow(133)));
            _ = _npcLocations.TryAdd(1000267, new NpcLocation(103.89868f,   -213.03125f,    _territoryType.GetRow(133)));
            _ = _npcLocations.TryAdd(1003252, new NpcLocation(-139.57434f,  31.967651f,     _territoryType.GetRow(129)));
            _ = _npcLocations.TryAdd(1001016, new NpcLocation(-42.679565f,  119.920654f,    _territoryType.GetRow(128)));
            _ = _npcLocations.TryAdd(1005422, new NpcLocation(-397.6349f,   80.979614f,     _territoryType.GetRow(129)));
            _ = _npcLocations.TryAdd(1000244, new NpcLocation(423.17834f,   -119.95117f,    _territoryType.GetRow(154)));

            // merchant & mender
            _ = _npcLocations.TryAdd(1000718, new NpcLocation(332.23462f,   332.47876f,     _territoryType.GetRow(154)));
            _ = _npcLocations.TryAdd(1000396, new NpcLocation(82.597046f,   -103.349365f,   _territoryType.GetRow(148)));

            // housing vendors
            _ = _npcLocations.TryAdd(1008837, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008838, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008839, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008840, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008841, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008842, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008843, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008844, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008845, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008846, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1013117, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1013118, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018662, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018663, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018664, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018665, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018666, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018667, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018668, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018669, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018670, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018671, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018672, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018673, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024548, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024549, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024550, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024551, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024552, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024553, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024554, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024555, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024556, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024557, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024558, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024559, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025039, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025043, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025717, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1026169, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1027015, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1045242, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1045256, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));

            _ = _npcLocations.TryAdd(1008847, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008848, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008849, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008850, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008851, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008852, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008853, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008854, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008855, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008856, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1008856, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1013119, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1013120, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018674, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018675, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018676, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018677, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018678, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018679, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018680, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018681, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018682, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018683, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018684, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1018685, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024560, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024561, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024562, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024563, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024564, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024565, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024566, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024567, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024568, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024569, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024570, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1024571, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025040, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025044, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025718, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1026170, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1027016, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1045243, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1045257, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));

            _ = _npcLocations.TryAdd(1016176, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1016177, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1016178, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1016179, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1016180, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1016181, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1016182, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1016183, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1016184, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1016185, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1016186, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1016187, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025027, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025028, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025029, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025030, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025031, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025032, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025033, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025034, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025035, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025036, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025037, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025038, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025042, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025046, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1025720, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1026172, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1027018, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1045245, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));
            _ = _npcLocations.TryAdd(1045259, new NpcLocation(0f, 0f, _territoryType.GetRow(282)));

            // OIC Quartermaster hax, only Maelstrom missing
            _ = _npcLocations.TryAdd(1002389, new NpcLocation(95.8114f, 67.61267f, _territoryType.GetRow(128)));
#pragma warning restore format
        }

        public ItemInfo GetItemInfo(uint itemId)
        {
            return !_isDataReady ? null : _itemDataMap.TryGetValue(itemId, out ItemInfo itemInfo) ? itemInfo : null;
        }

        // https://github.com/SapphireServer/Sapphire/blob/a5c15f321f7e795ed7362ae15edaada99ca7d9be/src/world/Event/EventHandler.h#L48-L83
        internal enum EventHandlerType : uint
        {
            GilShop = 0x0004,
            CustomTalk = 0x000B,
            GcShop = 0x0016,
            SpecialShop = 0x001B,
            FcShop = 0x002A,    // not sure how these numbers were obtained by the folks at sapphire. This works for my isolated use case though I guess.
        }
    }
}