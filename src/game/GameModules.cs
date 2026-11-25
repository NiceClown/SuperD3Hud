using System;
using System.Collections.Generic;

namespace SuperD3Hud.Game
{
    // ----------------------------------------------------------------------
    // Monsters and loot
    // ----------------------------------------------------------------------

    /// <summary>A monster worth showing on the map.</summary>
    public sealed class MonsterMarker
    {
        public int Id;
        public int Sno;
        public int Rarity;
        public float X;
        public float Y;
        public float Health;
        /// <summary>Champion, rare elite, unique or rift guardian.</summary>
        public bool IsElite;
        /// <summary>Rift guardian or an explicitly marked boss.</summary>
        public bool IsBoss;
        /// <summary>Short human readable label, e.g. "Элита" or "Босс".</summary>
        public string Label = "";
        /// <summary>Monster affixes (e.g. Arcane, Waller, Molten) as MonsterAffix enum uint values.</summary>
        public List<uint> Affixes = new List<uint>();
        /// <summary>Localized names of affixes in Russian.</summary>
        public List<string> AffixNames = new List<string>();
    }

    /// <summary>A legendary or set item lying on the ground.</summary>
    public sealed class LootMarker
    {
        public int Id;
        public int Gbid;
        public float X;
        public float Y;
        /// <summary>0 normal, 1 ancient, 2 primal.</summary>
        public int Rank;
        public bool IsSet;
        public string Name = "";
        /// <summary>Short label for the map, e.g. "Древний" or "Комплект".</summary>
        public string Label = "";
    }

    /// <summary>One of the other players of the group.</summary>
    public sealed class PartyMember
    {
        public int Id;
        public string Name = "";
        public float X;
        public float Y;
    }

    /// <summary>
    /// An interactive object of the area: the exit of a rift, a pylon or a
    /// waypoint. They are not monsters and not loot, but they can be used.
    /// </summary>
    public sealed class InteractiveMarker
    {
        public int Id;
        public int Sno;
        public float X;
        public float Y;
        /// <summary>"exit" for the portal of a rift, "pylon" for the rest.</summary>
        public string Kind = "";
        /// <summary>The name that is shown on the map.</summary>
        public string Name = "";
    }

    /// <summary>Everything read from the actor container in one pass.</summary>
    public sealed class ActorSnapshot
    {
        public int WorldId;
        public bool HasPlayer;
        public float PlayerX;
        public float PlayerY;
        public List<MonsterMarker> Monsters = new List<MonsterMarker>();
        public List<LootMarker> Loot = new List<LootMarker>();
        /// <summary>The other players of the group, if any.</summary>
        public List<PartyMember> PartyMembers = new List<PartyMember>();
        /// <summary>The exit of the rift, the pylons and other usable objects.</summary>
        public List<InteractiveMarker> Interactive = new List<InteractiveMarker>();
        /// <summary>True while the damage meter counts (in a rift).</summary>
        public bool DamageActive;
        /// <summary>The total damage of the group.</summary>
        public double DamageTotal;
        /// <summary>The damage per second of the last seconds.</summary>
        public double DamageDps;
        /// <summary>The players with their damage.</summary>
        public List<DamageEntry> DamageEntries = new List<DamageEntry>();
        public string Status = "";
        public DateTime CapturedUtc;
        /// <summary>0=None, 1=Rift, 2=GreaterRift, 3=ChallengeRift</summary>
        public int SpecialArea;
        public double RiftPercentage;
        public double CurrentQuestProgress;
        public double MaxQuestProgress = 100d;
        public int TimedEventEndTick;
    }

    /// <summary>Reads monsters and loot. Implemented by the actor module.</summary>
    public interface IActorModule
    {
        ActorSnapshot Snapshot { get; }
        ActorSnapshot Poll(GameReader reader, int worldId, bool includeLoot);
        void Reset();
    }

    // ----------------------------------------------------------------------
    // Buffs
    // ----------------------------------------------------------------------

    /// <summary>One tracked buff power.</summary>
    public sealed class BuffEntry
    {
        public int PowerSno;
        public string Name = "";
        /// <summary>Highest stack count seen in the icon attributes.</summary>
        public int Stacks;
        /// <summary>True when the power is currently active.</summary>
        public bool Active;
        /// <summary>Element name for Convention of Elements, empty otherwise.</summary>
        public string Element = "";
    }

    /// <summary>Buff state of the local player.</summary>
    public sealed class BuffSnapshot
    {
        public int WorldId;
        public List<BuffEntry> Buffs = new List<BuffEntry>();
        public string Status = "";
        public DateTime CapturedUtc;
    }

    /// <summary>Reads buff presence and stacks. Implemented by the buff module.</summary>
    public interface IBuffModule
    {
        BuffSnapshot Snapshot { get; }
        BuffSnapshot Poll(GameReader reader, int worldId, int localPlayerSlot);
        void Reset();
    }

    // ----------------------------------------------------------------------
    // Skills and resources
    // ----------------------------------------------------------------------

    /// <summary>Cooldown state of one skill slot.</summary>
    public sealed class SkillCooldown
    {
        public int Slot;
        public string Key = "";
        public double RemainingSeconds;
        public double DurationSeconds;
        /// <summary>0 ready, 1 just used.</summary>
        public double Fraction;
        /// <summary>True when the value is an estimate from input, not a read.</summary>
        public bool IsEstimated;
        public int PowerSno;
        public int Rune;
        public int FinishTick;
        public bool IsReady { get { return RemainingSeconds <= 0.001; } }
    }

    /// <summary>Skill and resource state of the local player.</summary>
    public sealed class SkillSnapshot
    {
        public List<SkillCooldown> Skills = new List<SkillCooldown>();
        public int GameTick;
        public int[] EquippedPowerSnos = new int[6];
        public int[] EquippedRunes = new int[6];
        /// <summary>Primary resource, e.g. fury or mana; negative when unknown.</summary>
        public double PrimaryResource = -1;
        public double PrimaryResourceMax = -1;
        /// <summary>Secondary resource, e.g. discipline; negative when unknown.</summary>
        public double SecondaryResource = -1;
        public double SecondaryResourceMax = -1;
        /// <summary>Current health; negative when unknown.</summary>
        public double Health = -1;
        public double HealthMax = -1;
        public string Status = "";
        public DateTime CapturedUtc;
    }

    /// <summary>Tracks skill cooldowns from user input. Implemented by the skill module.</summary>
    public interface ISkillModule
    {
        SkillSnapshot Snapshot { get; }
        /// <summary>Called when the user pressed a skill key.</summary>
        void OnKey(int virtualKey);
        /// <summary>Called on every frame to advance the timers.</summary>
        SkillSnapshot Poll(GameReader reader, int localPlayerSlot);
        void Reset();
    }

    // ----------------------------------------------------------------------
    // Inventory
    // ----------------------------------------------------------------------

    /// <summary>One item in the backpack.</summary>
    public sealed class ItemEntry
    {
        public int Column;
        public int Row;
        public int Gbid;
        /// <summary>0..9, 9 is legendary or set.</summary>
        public int Quality;
        /// <summary>0 normal, 1 ancient, 2 primal.</summary>
        public int Rank;
        public string Name = "";
    }

    /// <summary>Backpack state of the local player.</summary>
    public sealed class InventorySnapshot
    {
        public List<ItemEntry> Items = new List<ItemEntry>();
        public int UsedSlots;
        public int TotalSlots;
        public int FreeSlots { get { return Math.Max(0, TotalSlots - UsedSlots); } }
        public string Status = "";
        public DateTime CapturedUtc;
    }

    /// <summary>Reads the backpack. Implemented by the inventory module.</summary>
    public interface IInventoryModule
    {
        InventorySnapshot Snapshot { get; }
        InventorySnapshot Poll(GameReader reader, int worldId);
        void Reset();
    }
}
