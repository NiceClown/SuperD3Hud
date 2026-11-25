using System;
using System.Collections.Generic;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>
    /// One game attribute of the public API. The host knows the attribute code
    /// and index; the numeric id is derived the way the game derives it.
    /// </summary>
    public class HostAttribute : IAttribute
    {
        private readonly string _code;
        private readonly uint _index;

        public HostAttribute(string code, uint index)
        {
            _code = code ?? "";
            _index = index;
        }

        public string Code { get { return _code; } }
        public uint Index { get { return _index; } }
        public AttributeValueType ValueType { get { return AttributeValueType._int; } }
        public IEnumerable<IAttributeProcessor> Processors { get { return new IAttributeProcessor[0]; } }
        public bool OrderIndexPrimary { get { return true; } }

        public uint GetId(uint modifier)
        {
            // Attribute ids are keyed by the catalogue index; modifiers are
            // selectors for the value and must not alter the base id.
            return _index;
        }

        public string GetDescription(uint modifier)
        {
            return _code;
        }

        public int GetModLowestOrderIndex(int mod)
        {
            return mod < 0 ? 0 : mod;
        }
    }

    /// <summary>A processor attached to an attribute; the host applies none.</summary>
    public class HostAttributeProcessor : IAttributeProcessor
    {
        private readonly IAttribute _attribute;
        private readonly string _code;

        public HostAttributeProcessor(IAttribute attribute, string code)
        {
            _attribute = attribute;
            _code = code ?? "";
        }

        public IAttribute Attribute { get { return _attribute; } }
        public string Code { get { return _code; } }
        public byte CompactId { get { return 0; } }
        public uint Modifier { get { return 0; } }
        public double? Multiplier { get { return null; } }
        public int RoundDecimals { get { return 0; } }

        public double ProcessDouble(double dv)
        {
            if (double.IsNaN(dv) || double.IsInfinity(dv)) return 0d;
            return dv;
        }
        public int ProcessInt(int iv) { return iv; }
    }

    /// <summary>A game item definition. Names come from the live snapshots.</summary>
    public class HostSnoItem : ISnoItem
    {
        private readonly uint _sno;
        private readonly string _code;
        private readonly string _name;
        private readonly HostSnoController _owner;
        private List<ISnoSocketedEffect> _socketedEffects;

        public HostSnoItem(HostSnoController owner, uint sno, string code, string name)
        {
            _owner = owner;
            _sno = sno;
            _code = code ?? "";
            _name = name ?? "";
            _socketedEffects = new List<ISnoSocketedEffect>();
        }

        public uint Sno { get { return _sno; } }
        public ISnoActor SnoActor { get { return null; } }
        public ItemKind Kind { get { return ItemKind.loot; } }
        public uint PrefixStringSno { get { return 0; } }
        public uint SuffixStringSno { get { return 0; } }
        public int Level { get { return 0; } }
        public int ItemWidth { get { return 1; } }
        public int ItemHeight { get { return 1; } }
        public string Code { get { return _code; } }
        private string CurrentName
        {
            get
            {
                if (_owner != null)
                {
                    string live = "";
                    try { live = _owner.GetItemName(_sno); }
                    catch { live = ""; }
                    if (!string.IsNullOrEmpty(live)) return live;
                }
                return _name;
            }
        }

        public string NameLocalized { get { return CurrentName; } }
        public string NameEnglish { get { return CurrentName; } }
        public string MainGroupCode { get { return ""; } }
        public string[] GroupCodes { get { return new string[0]; } }
        public ItemLocation UsedLocation1 { get { return ItemLocation.Inventory; } }
        public ItemLocation UsedLocation2 { get { return ItemLocation.Inventory; } }
        public ISnoItemType SnoItemType { get { return null; } }
        public int StackSize { get { return 1; } }
        public int GoldPrice { get { return 0; } }
        public int UnsocketPrice { get { return 0; } }
        public int RequiredLevel { get { return 0; } }
        public uint BaseItemSno { get { return 0; } }
        public uint SetItemBonusesSno { get { return 0; } }
        public ItemCraftQuality CraftQuality { get { return ItemCraftQuality.None; } }
        public ISnoItemMod[] Mods { get { return new ISnoItemMod[0]; } }
        public ISnoItemAffixGroupLink[] AffixGroups { get { return new ISnoItemAffixGroupLink[0]; } }

        public List<ISnoSocketedEffect> SocketedEffects
        {
            get { return _socketedEffects ?? new List<ISnoSocketedEffect>(); }
            set { _socketedEffects = value ?? new List<ISnoSocketedEffect>(); }
        }

        public bool CanKanaiCube { get { return false; } }
        public int DropWeight { get { return 0; } }
        public int DropSmartDemonHunter { get { return 0; } }
        public int DropSmartBarbarian { get { return 0; } }
        public int DropSmartWizard { get { return 0; } }
        public int DropSmartWitchDoctor { get { return 0; } }
        public int DropSmartMonk { get { return 0; } }
        public int DropSmartCrusader { get { return 0; } }

        public bool HasGroupCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            if (string.Equals(code, MainGroupCode, StringComparison.OrdinalIgnoreCase)) return true;
            string[] groups = GroupCodes;
            for (int i = 0; i < groups.Length; i++)
                if (string.Equals(groups[i], code, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public ISnoPower LegendaryPower { get { return null; } }
        public bool IsEthereal { get { return false; } }
    }

    /// <summary>A power definition. Names come from the live buff snapshot.</summary>
    public class HostSnoPower : ISnoPower
    {
        /// <summary>
        /// Number of icon slots of a power. TurboHUD powers have 32 buff icon
        /// slots, and plugins index IBuff.IconCounts, TimeLeftSeconds,
        /// TimeElapsedSeconds and ISnoPower.Icons with the same index (the
        /// Default BuffRuleCalculator loops over TimeLeftSeconds.Length and
        /// then reads Icons[i]). Every one of those arrays must therefore have
        /// exactly this length, or a plugin throws IndexOutOfRange each frame.
        /// The highest literal index used by the bundled plugins is 9.
        /// </summary>
        public const int IconSlots = 32;

        /// <summary>
        /// High bit that marks a texture id as synthetic. The host has no game
        /// texture atlas; HostTextureController returns a placeholder for any
        /// id. A non-zero id is still required because BuffRuleCalculator and
        /// SkillPainter drop an icon whose texture id is 0.
        /// </summary>
        private const uint SyntheticTextureFlag = 0x80000000u;

        private readonly uint _sno;
        private readonly string _code;
        private readonly string _name;
        private readonly HostSnoController _owner;
        private int[] _iconIndexes;
        private SnoPowerIcon[] _icons;
        private string _iconsName;

        public HostSnoPower(uint sno, string code, string name)
            : this(null, sno, code, name)
        {
        }

        public HostSnoPower(HostSnoController owner, uint sno, string code, string name)
        {
            _owner = owner;
            _sno = sno;
            _code = code ?? "";
            _name = name ?? "";
            _iconIndexes = new int[0];
        }

        public uint Sno { get { return _sno; } }
        public string Code { get { return _code; } }
        private string CurrentName
        {
            get
            {
                if (_owner != null)
                {
                    string live = "";
                    try { live = _owner.GetPowerName(_sno); }
                    catch { live = ""; }
                    if (!string.IsNullOrEmpty(live)) return live;
                }
                return _name;
            }
        }

        public string NameLocalized { get { return CurrentName; } }
        public string NameEnglish { get { return CurrentName; } }
        public string DescriptionLocalized { get { return ""; } }
        public string DescriptionEnglish { get { return ""; } }
        public string[] RuneNamesLocalized { get { return new string[0]; } }
        public string[] RuneNamesEnglish { get { return new string[0]; } }

        public int[] IconIndexes
        {
            get { return _iconIndexes ?? new int[0]; }
            set { _iconIndexes = value ?? new int[0]; }
        }

        public bool HasKnownRunesValues { get { return false; } }
        public int[] ElementalDamageTypesByRune { get { return new int[0]; } }
        public int[] WeaponDamageMultipliersByRune { get { return new int[0]; } }
        public float[] DotSecondsByRune { get { return new float[0]; } }
        public int[] ResourceCostsByRune { get { return new int[0]; } }
        public PowerResourceCostType[] ResourceCostTypeByRune { get { return new PowerResourceCostType[0]; } }
        public double[] BaseCoolDownByRune { get { return new double[0]; } }
        /// <summary>
        /// IconSlots icons; never empty and never shorter than the IBuff
        /// arrays. Slot 0 is the power's main icon. The other slots are
        /// default (Exists=false), so rules that target them fall back to
        /// NormalIconTextureId. The array is cached per power and rebuilt only
        /// when the live name changes, because plugins read it per buff per
        /// frame.
        /// </summary>
        public SnoPowerIcon[] Icons
        {
            get
            {
                string name = CurrentName ?? "";
                SnoPowerIcon[] icons = _icons;
                if (icons != null && string.Equals(_iconsName, name, StringComparison.Ordinal))
                    return icons;

                icons = new SnoPowerIcon[IconSlots];
                for (int i = 0; i < icons.Length; i++)
                {
                    // Strings are "" rather than null: BuffPainter concatenates
                    // TitleLocalized/DescriptionLocalized into the tooltip.
                    icons[i].TitleLocalized = "";
                    icons[i].DescriptionLocalized = "";
                }
                icons[0].Exists = true;
                icons[0].TitleLocalized = name;
                icons[0].TextureId = NormalIconTextureId;
                icons[0].ShowDuration = true;
                icons[0].ShowInBuffHolder = true;
                // Publish the name before the array: a reader that sees the
                // new array with the old name only rebuilds once more.
                _iconsName = name;
                _icons = icons;
                return icons;
            }
        }

        /// <summary>
        /// Synthetic non-zero id derived from the SNO, so GetIconTexture in the
        /// Default buff lists returns a (placeholder) texture instead of null
        /// and the buff is laid out with its timer and stack text.
        /// </summary>
        public uint NormalIconTextureId { get { return SyntheticTextureFlag | _sno; } }
        public ISnoPower SynergyPower { get { return null; } }
    }

    /// <summary>An actor definition. The host keeps the SNO and a synthetic code.</summary>
    public class HostSnoActor : ISnoActor
    {
        private readonly uint _sno;
        private readonly string _code;

        public HostSnoActor(uint sno, string code)
        {
            _sno = sno;
            _code = code ?? "";
        }

        public ActorSnoEnum Sno { get { return (ActorSnoEnum)(int)_sno; } }
        public ActorKind Kind { get { return ActorKind.None; } }
        public ActorType Type { get { return ActorType.Invalid; } }
        public string Code { get { return _code; } }
        public string NameLocalized { get { return ""; } }
        public string NameEnglish { get { return ""; } }
        public bool IsStatic { get { return false; } }
        public uint[] TextureMap { get { return new uint[0]; } }
    }

    /// <summary>A monster definition.</summary>
    public class HostSnoMonster : ISnoMonster
    {
        private readonly uint _sno;
        private readonly string _code;
        private readonly HostSnoActor _actor;
        private readonly MonsterPriority _priority;
        private readonly float _riftProgression;
        private readonly string _nameLocalized;

        public HostSnoMonster(uint sno, string code, HostSnoActor actor)
            : this(sno, code, actor, MonsterPriority.normal, 1.15f, "")
        {
        }

        public HostSnoMonster(uint sno, string code, HostSnoActor actor,
                              MonsterPriority priority, float riftProgression, string nameLocalized)
        {
            _sno = sno;
            _code = code ?? "";
            _actor = actor;
            _priority = priority;
            _riftProgression = riftProgression;
            _nameLocalized = nameLocalized ?? "";
        }

        public ISnoActor SnoActor { get { return _actor; } }
        public uint Sno { get { return _sno; } }
        public string Code { get { return _code; } }
        public string NameLocalized { get { return _nameLocalized; } }
        public string NameEnglish { get { return _code; } }
        public bool IsUnique { get { return _priority == MonsterPriority.boss || _priority == MonsterPriority.keywarden; } }
        public MonsterPriority Priority { get { return _priority; } }
        public float RiftProgression { get { return _riftProgression; } }
    }

    /// <summary>An area definition. The host knows only the id.</summary>
    public class HostSnoAreaInfo : ISnoArea
    {
        private readonly uint _sno;
        private readonly string _code;

        public HostSnoAreaInfo(uint sno, string code)
        {
            _sno = sno;
            _code = code ?? "";
        }

        public uint Sno { get { return _sno; } }
        public string Code { get { return _code; } }
        public string NameLocalized { get { return ""; } }
        public string NameEnglish { get { return ""; } }
        public AreaType Type { get { return AreaType.Normal; } }
        public bool IsRandom { get { return false; } }
        public bool IsTown { get { return false; } }
        public int Act { get { return 0; } }
        public string AreaGroupInWorld { get { return ""; } }
        public ISnoWorld SnoWorld { get { return null; } }
        public uint HostAreaSno { get { return 0; } }
        public ISnoArea HostSnoArea { get { return null; } }
    }

    /// <summary>A quest definition. The host has no quest catalogue.</summary>
    public class HostSnoQuest : ISnoQuest
    {
        private readonly uint _sno;
        private readonly string _code;

        public HostSnoQuest(uint sno, string code)
        {
            _sno = sno;
            _code = code ?? "";
        }

        public string Code { get { return _code; } }
        public uint Sno { get { return _sno; } }
        public string NameLocalized { get { return ""; } }
        public string NameEnglish { get { return ""; } }
        public string DescriptionLocalized { get { return ""; } }
        public string DescriptionEnglish { get { return ""; } }
        public ISnoAct SnoAct { get { return null; } }
        public QuestType Type { get { return QuestType.MainQuest; } }
        public QuestEventType EventType { get { return QuestEventType.None; } }
        public int UnassignedStepId { get { return 0; } }
        public string UnassignedStepBnetTitleLocalized { get { return ""; } }
        public string UnassignedStepBnetTitleEnglish { get { return ""; } }
        public BountyAct BountyAct { get { return BountyAct.None; } }
        public BountyType BountyType { get { return BountyType.None; } }
        public ISnoArea BountySnoArea { get { return null; } }

        public ISnoQuestStep GetStep(uint stepId)
        {
            return null;
        }

        public IEnumerable<ISnoQuestStep> Steps { get { return new ISnoQuestStep[0]; } }
    }

    /// <summary>
    /// The SNO controller. Catalogue entries resolve to lightweight objects
    /// built from the current snapshots, so a plugin that asks for a named
    /// item or power gets a real name whenever the game reported it.
    /// </summary>
    public class HostSnoController : ISnoController, ICatalogueResolver
    {
        private readonly HostContext _context;
        private readonly HostSnoItemList _items;
        private readonly HostSnoPowerList _powers;
        private readonly HostSnoQuestList _quests;
        private readonly HostAttributeList _attributes;
        private readonly Dictionary<uint, ISnoPower> _powerCache = new Dictionary<uint, ISnoPower>();
        private readonly Dictionary<uint, ISnoItem> _itemCache = new Dictionary<uint, ISnoItem>();
        private readonly Dictionary<uint, ISnoActor> _actorCache = new Dictionary<uint, ISnoActor>();
        private readonly Dictionary<string, IAttribute> _attributeCache = new Dictionary<string, IAttribute>(StringComparer.Ordinal);
        private readonly Dictionary<uint, ISnoQuest> _questCache = new Dictionary<uint, ISnoQuest>();
        private readonly HostBuffCache _buffCache = new HostBuffCache();

        public HostSnoController(HostContext context)
        {
            _context = context;
            _items = new HostSnoItemList(this);
            _powers = new HostSnoPowerList(this);
            _quests = new HostSnoQuestList(this);
            _attributes = new HostAttributeList(this);
        }

        public IAttributeList Attributes { get { return _attributes; } }

        public ISnoPowerList SnoPowers { get { return _powers; } }

        /// <summary>
        /// Buff objects of the local player. They live here, not in HostPlayer,
        /// because HostGame builds a new HostPlayer for every snapshot revision
        /// and the buff watches must survive that.
        /// </summary>
        internal HostBuffCache BuffCache { get { return _buffCache; } }

        public ISnoPower GetSnoPower(uint sno)
        {
            if (sno == 0) return null;
            ISnoPower cached;
            if (_powerCache.TryGetValue(sno, out cached)) return cached;

            HostSnoPower power = new HostSnoPower(this, sno, "power:" + sno.ToString(),
                                                  _context == null ? "" : _context.PowerName(sno));
            _powerCache[sno] = power;
            return power;
        }

        public IEnumerable<ISnoPower> AllSnoPower { get { return new List<ISnoPower>(); } }

        public ISnoQuestList SnoQuests { get { return _quests; } }

        public ISnoQuest GetSnoQuest(uint sno)
        {
            if (sno == 0) return null;
            ISnoQuest cached;
            if (_questCache.TryGetValue(sno, out cached)) return cached;

            HostSnoQuest quest = new HostSnoQuest(sno, "quest:" + sno.ToString());
            _questCache[sno] = quest;
            return quest;
        }

        public IEnumerable<ISnoQuest> AllSnoQuest { get { return new List<ISnoQuest>(); } }

        public ISnoItemList SnoItems { get { return _items; } }

        public long TotalParagonExperienceRequired(uint paragonLevel)
        {
            // The public formula of the game: the experience needed to reach a
            // paragon level. It is a pure function, so it needs no memory read.
            double level = paragonLevel;
            double total = 0d;
            // Do not allow malformed plugin input to turn this pure helper
            // into a multi-billion-iteration loop.
            if (level > 100000d) return long.MaxValue;
            for (long i = 1; i < (long)level; i++)
            {
                double amount = Math.Floor(1400.0 * Math.Pow(i, 1.1) + 100.0 * i);
                if (amount < 0d || double.IsNaN(amount) || double.IsInfinity(amount)
                    || total > long.MaxValue - amount)
                    return long.MaxValue;
                total += amount;
            }
            return total >= long.MaxValue ? long.MaxValue : (long)total;
        }

        // ---- ICatalogueResolver ------------------------------------------

        public ISnoItem SnoItem(string code, uint sno)
        {
            if (sno == 0) return null;
            ISnoItem cached;
            if (_itemCache.TryGetValue(sno, out cached)) return cached;
            string name = _context == null ? "" : _context.ItemName(sno);
            cached = new HostSnoItem(this, sno, code, name);
            _itemCache[sno] = cached;
            return cached;
        }

        public ISnoPower SnoPower(string code, uint sno)
        {
            return GetSnoPower(sno);
        }

        public ISnoQuest SnoQuest(string code, uint sno)
        {
            return GetSnoQuest(sno);
        }

        public IAttribute Attribute(string code, uint index)
        {
            string key = (code ?? "") + "\u001f" + index.ToString();
            IAttribute cached;
            if (_attributeCache.TryGetValue(key, out cached)) return cached;
            cached = new HostAttribute(code, index);
            _attributeCache[key] = cached;
            return cached;
        }

        private ISnoActor _defaultActor;

        public ISnoActor SnoActor(string code, uint sno)
        {
            if (sno == 0) return _defaultActor ?? (_defaultActor = new HostSnoActor(0u, code ?? ""));
            ISnoActor cached;
            if (_actorCache.TryGetValue(sno, out cached)) return cached;
            cached = new HostSnoActor(sno, code);
            _actorCache[sno] = cached;
            return cached;
        }

        internal string GetItemName(uint sno)
        {
            return _context == null ? "" : _context.ItemName(sno);
        }

        internal string GetPowerName(uint sno)
        {
            return _context == null ? "" : _context.PowerName(sno);
        }

        public IEnumerable<ISnoItem> AllSnoItems { get { return new List<ISnoItem>(); } }
        public IEnumerable<ISnoPower> AllSnoPowers { get { return new List<ISnoPower>(); } }
        public IEnumerable<ISnoQuest> AllSnoQuests { get { return new List<ISnoQuest>(); } }
    }

    /// <summary>
    /// Keeps one HostBuff per power SNO across snapshot revisions.
    /// Rebuilding the buffs on every call reset FirstActive (so
    /// TimeElapsedSeconds was always about 0) and allocated a new list for
    /// every GetBuff/BuffIsActive call a plugin made in a frame.
    /// </summary>
    internal sealed class HostBuffCache
    {
        private readonly object _sync = new object();
        private readonly Dictionary<int, HostBuff> _bySno = new Dictionary<int, HostBuff>();
        private SuperD3Hud.Game.BuffSnapshot _snapshot;
        private int _revision = int.MinValue;
        private IPlayer _player;
        private IBuff[] _list = new IBuff[0];

        /// <summary>
        /// The buffs of the given snapshot. The result is rebuilt only when the
        /// context revision, the snapshot object or the player object changes;
        /// otherwise the same array is returned, so callers must not modify it.
        /// </summary>
        public IBuff[] GetBuffs(IPlayer player, HostSnoController sno, HostContext context)
        {
            if (context == null || sno == null) return new IBuff[0];
            SuperD3Hud.Game.BuffSnapshot snapshot = context.Buffs;
            int revision = context.Revision;
            lock (_sync)
            {
                if (revision == _revision && ReferenceEquals(snapshot, _snapshot)
                    && ReferenceEquals(player, _player))
                    return _list;

                List<SuperD3Hud.Game.BuffEntry> entries = snapshot == null ? null : snapshot.Buffs;
                List<IBuff> result = new List<IBuff>();
                HashSet<int> seen = new HashSet<int>();
                if (entries != null)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        SuperD3Hud.Game.BuffEntry entry = entries[i];
                        // Entries without a power are skipped: IBuff.SnoPower
                        // would be null and plugins dereference it unchecked
                        // (buff.SnoPower.Sno, buff.SnoPower.Icons).
                        if (entry == null || entry.PowerSno <= 0) continue;
                        // A duplicate SNO would be invisible to GetBuff anyway
                        // (it returns the first match) and would share the
                        // cached object, so keep only the first entry.
                        if (!seen.Add(entry.PowerSno)) continue;
                        ISnoPower power = sno.GetSnoPower((uint)entry.PowerSno);
                        if (power == null) continue;

                        HostBuff buff;
                        if (_bySno.TryGetValue(entry.PowerSno, out buff))
                            buff.Update(player, power, entry);
                        else
                        {
                            buff = new HostBuff(player, power, entry);
                            _bySno[entry.PowerSno] = buff;
                        }
                        result.Add(buff);
                    }
                }

                // A buff that left the snapshot starts from scratch if it
                // comes back, which matches a freshly applied buff.
                if (_bySno.Count > seen.Count)
                {
                    List<int> stale = new List<int>();
                    foreach (int key in _bySno.Keys)
                        if (!seen.Contains(key)) stale.Add(key);
                    for (int i = 0; i < stale.Count; i++) _bySno.Remove(stale[i]);
                }

                _list = result.ToArray();
                _snapshot = snapshot;
                _revision = revision;
                _player = player;
                return _list;
            }
        }
    }
}
