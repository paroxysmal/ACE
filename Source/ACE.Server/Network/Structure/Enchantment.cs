using System.Collections.Generic;
using System.IO;
using ACE.Database;
using ACE.Database.Models.Shard;
using ACE.Database.Models.World;
using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.Entity.Enum;
using ACE.Server.WorldObjects;

namespace ACE.Server.Network.Structure
{
    public class Enchantment
    {
        public WorldObject Target;
        public ACE.Entity.ObjectGuid CasterGuid;
        public SpellBase SpellBase;
        public Spell Spell;
        public ushort Layer;
        public EnchantmentMask EnchantmentMask;
        public double StartTime;
        public double Duration;
        public float? StatMod;

        public Enchantment(WorldObject target, ACE.Entity.ObjectGuid? casterGuid, uint spellId, double duration, ushort layer, uint? enchantmentMask, float? statMod = null)
        {
            Target = target;

            if (casterGuid == null)
                CasterGuid = ACE.Entity.ObjectGuid.Invalid;
            else
                CasterGuid = (ACE.Entity.ObjectGuid)casterGuid;

            SpellBase = DatManager.PortalDat.SpellTable.Spells[spellId];
            Spell = DatabaseManager.World.GetCachedSpell(spellId);
            Layer = layer;
            Duration = duration;
            EnchantmentMask = (EnchantmentMask)(enchantmentMask ?? 0);
            StatMod = statMod ?? Spell.StatModVal;
        }

        public Enchantment(WorldObject target, ACE.Entity.ObjectGuid? casterGuid, SpellBase spellBase, double duration, ushort layer, uint? enchantmentMask, float? statMod = null)
        {
            Target = target;

            if (casterGuid == null)
                CasterGuid = ACE.Entity.ObjectGuid.Invalid;
            else
                CasterGuid = (ACE.Entity.ObjectGuid)casterGuid;

            SpellBase = spellBase;
            Layer = layer;
            Duration = duration;
            EnchantmentMask = (EnchantmentMask)(enchantmentMask ?? 0);
            StatMod = statMod;
        }

        public Enchantment(WorldObject target, BiotaPropertiesEnchantmentRegistry entry)
        {
            Target = target;
            CasterGuid = new ACE.Entity.ObjectGuid(entry.CasterObjectId);
            SpellBase = DatManager.PortalDat.SpellTable.Spells[(uint)entry.SpellId];
            Spell = DatabaseManager.World.GetCachedSpell((uint)entry.SpellId);
            Layer = entry.LayerId;
            StartTime = entry.StartTime;
            Duration = entry.Duration;
            EnchantmentMask = (EnchantmentMask)entry.EnchantmentCategory;
            StatMod = entry.StatModValue;
        }
    }

    public static class EnchantmentExtentions
    {
        public static readonly double LastTimeDegraded = 0;
        public static readonly float DefaultStatMod = 35.0f;

        public static readonly ushort HasSpellSetId = 1;
        public static readonly uint SpellSetId = 0;

        public static void Write(this BinaryWriter writer, List<Enchantment> enchantments)
        {
            writer.Write(enchantments.Count);
            foreach (var enchantment in enchantments)
                writer.Write(enchantment);
        }

        public static void Write(this BinaryWriter writer, Enchantment enchantment)
        {
            var spell = enchantment.Spell;
            var statModType = spell != null ? spell.StatModType ?? 0 : 0;
            var statModKey = spell != null ? spell.StatModKey ?? 0 : 0;

            writer.Write((ushort)enchantment.SpellBase.MetaSpellId);
            writer.Write(enchantment.Layer);
            writer.Write((ushort)enchantment.SpellBase.Category);
            writer.Write(HasSpellSetId);
            writer.Write(enchantment.SpellBase.Power);
            writer.Write(enchantment.StartTime);
            writer.Write(enchantment.Duration);
            writer.Write(enchantment.CasterGuid.Full);
            writer.Write(enchantment.SpellBase.DegradeModifier);
            writer.Write(enchantment.SpellBase.DegradeLimit);
            writer.Write(LastTimeDegraded);     // always 0 / spell economy?
            writer.Write(statModType);
            writer.Write(statModKey);
            writer.Write(enchantment.StatMod ?? DefaultStatMod);
            writer.Write(SpellSetId);
        }
    }
}
