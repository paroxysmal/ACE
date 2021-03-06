using System;
using System.Collections.Generic;
using System.Numerics;

using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACE.DatLoader.Entity;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Structure;
using ACE.Server.Managers;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Physics;
using Position = ACE.Entity.Position;

namespace ACE.Server.WorldObjects
{
    partial class WorldObject
    {
        public struct EnchantmentStatus
        {
            public StackType stackType;
            public GameMessageSystemChat message;
        }

        public enum SpellLevel
        {
            One = 1,
            Two = 50,
            Three = 100,
            Four = 150,
            Five = 200,
            Six = 250,
            Seven = 300,
            Eight = 350
        }

        protected static SpellLevel CalculateSpellLevel(SpellBase spell)
        {
            SpellLevel spellLevel;
            var scarab = spell.Formula[0];

            switch (scarab)
            {
                case 1:
                    spellLevel = SpellLevel.One;
                    break;
                case 2:
                    spellLevel = SpellLevel.Two;
                    break;
                case 3:
                    spellLevel = SpellLevel.Three;
                    break;
                case 4:
                    spellLevel = SpellLevel.Four;
                    break;
                case 5:
                    spellLevel = SpellLevel.Five;
                    break;
                case 6:
                    spellLevel = SpellLevel.Six;
                    break;
                case 7:
                    spellLevel = SpellLevel.Seven;
                    break;
                default:
                    spellLevel = SpellLevel.Eight;
                    break;
            }

            return spellLevel;
        }

        /// <summary>
        /// Method used for the scaling, windup motion, and spell gestures for spell casts
        /// </summary>
        protected static float SpellAttributes(string account, uint spellId, out float castingDelay, out MotionCommand windUpMotion, out MotionCommand spellGesture)
        {
            float scale;

            SpellComponentsTable comps = DatManager.PortalDat.SpellComponentsTable;

            SpellTable spellTable = DatManager.PortalDat.SpellTable;
            if (!spellTable.Spells.ContainsKey(spellId))
            {
                windUpMotion = MotionCommand.Invalid;
                spellGesture = MotionCommand.Invalid;
                castingDelay = 0.0f;
                return -1.0f;
            }

            SpellBase spell = spellTable.Spells[spellId];

            ////Determine scale of the spell effects and windup animation
            var spellLevel = CalculateSpellLevel(spell);
            if (account == null)
            {
                switch (spellLevel)
                {
                    case SpellLevel.One:
                        scale = 0.1f;
                        break;
                    case SpellLevel.Two:
                        scale = 0.2f;
                        break;
                    case SpellLevel.Three:
                        scale = 0.4f;
                        break;
                    case SpellLevel.Four:
                        scale = 0.5f;
                        break;
                    case SpellLevel.Five:
                        scale = 0.6f;
                        break;
                    case SpellLevel.Six:
                        scale = 1.0f;
                        break;
                    default:
                        scale = 1.0f;
                        break;
                }

                spellGesture = MotionCommand.Magic;
                windUpMotion = 0;
                castingDelay = 0.0f;
                return scale;
            }

            switch (spellLevel)
            {
                case SpellLevel.One:
                    scale = 0.1f;
                    castingDelay = 1.3f;
                    windUpMotion = MotionCommand.MagicPowerUp01;
                    break;
                case SpellLevel.Two:
                    scale = 0.2f;
                    castingDelay = 1.4f;
                    windUpMotion = MotionCommand.MagicPowerUp02;
                    break;
                case SpellLevel.Three:
                    scale = 0.4f;
                    castingDelay = 1.5f;
                    windUpMotion = MotionCommand.MagicPowerUp03;
                    break;
                case SpellLevel.Four:
                    scale = 0.5f;
                    castingDelay = 1.7f;
                    windUpMotion = MotionCommand.MagicPowerUp04;
                    break;
                case SpellLevel.Five:
                    scale = 0.6f;
                    castingDelay = 1.9f;
                    windUpMotion = MotionCommand.MagicPowerUp05;
                    break;
                case SpellLevel.Six:
                    scale = 1.0f;
                    castingDelay = 2.0f;
                    windUpMotion = MotionCommand.MagicPowerUp06;
                    break;
                default:
                    scale = 1.0f;
                    castingDelay = 2.0f;
                    windUpMotion = MotionCommand.MagicPowerUp07Purple;
                    break;
            }

            var formula = SpellTable.GetSpellFormula(spellTable, spellId, account);
            spellGesture = (MotionCommand)comps.SpellComponents[formula[formula.Count - 1]].Gesture;

            return scale;
        }

        /// <summary>
        /// Determine is the spell being case is harmful or beneficial
        /// </summary>
        /// <param name="spell"></param>
        /// <returns></returns>
        protected bool IsSpellHarmful(SpellBase spell)
        {
            // All War and Void Magic spells are harmful
            if (spell.School == MagicSchool.WarMagic || spell.School == MagicSchool.VoidMagic)
                return true;

            // Life Magic spells that don't have bit three of their bitfield property set are harmful
            if (spell.School == MagicSchool.LifeMagic && (spell.Bitfield & (uint)SpellBitfield.Beneficial) == 0)
                return true;

            // Creature Magic spells that don't have bit three of their bitfield property set are harmful
            if (spell.School == MagicSchool.CreatureEnchantment && (spell.Bitfield & (uint)SpellBitfield.Beneficial) == 0)
                return true;

            return false;
        }

        /// <summary>
        /// Determine Player's PK status and whether it matches the target Player
        /// </summary>
        /// <param name="player"></param>
        /// <param name="target"></param>
        /// <param name="spell"></param>
        /// <returns>
        /// A null return signifies either player or target are not Player World objects, so check does not apply
        /// A true return value indicates that the Player passed the PK status check
        /// A false return value indicates that the Player failed the PK status check
        /// </returns>
        protected bool? CheckPKStatusVsTarget(Player player, Player target, SpellBase spell)
        {
            if (player == null || target == null)
                return null;

            bool isSpellHarmful = IsSpellHarmful(spell);
            if (isSpellHarmful)
            {
                // Ensure that a non-PK cannot cast harmful spells on another player
                if (player.PlayerKillerStatus == PlayerKillerStatus.NPK)
                    return false;

                // Ensure that a harmful spell isn't being cast on another player that doesn't have the same PK status
                if (player.PlayerKillerStatus != PlayerKillerStatus.NPK)
                {
                    if (player.PlayerKillerStatus != target.PlayerKillerStatus)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether the target for the spell being cast is invalid
        /// </summary>
        /// <param name="spell"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        protected bool IsInvalidTarget(SpellBase spell, WorldObject target)
        {
            var targetPlayer = target as Player;
            var targetCreature = target as Creature;

            // Self targeted spells should have a target of self
            if ((int)Math.Floor(spell.BaseRangeConstant) == 0 && targetPlayer == null)
                return true;

            // Invalidate non Item Enchantment spells cast against non Creatures or Players
            if (spell.School != MagicSchool.ItemEnchantment && targetCreature == null)
                return true;

            // Invalidate beneficial spells against Creature/Non-player targets
            if (targetCreature != null && targetPlayer == null && IsSpellHarmful(spell) == false)
                return true;

            // Cannot cast Weapon Aura spells on targets that are not players or creatures
            if ((spell.Name.Contains("Aura of")) && (spell.School == MagicSchool.ItemEnchantment))
            {
                if (targetCreature == null)
                    return true;
            }

            // Cannot cast Weapon Aura spells on targets that are not players or creatures
            if ((spell.MetaSpellType == SpellType.Enchantment) && (spell.School == MagicSchool.ItemEnchantment))
            {
                if (targetPlayer != null
                    || (target.WeenieType == WeenieType.Creature)
                    || (target.WeenieType == WeenieType.Clothing)
                    || (target.WeenieType == WeenieType.Caster)
                    || (target.WeenieType == WeenieType.MeleeWeapon)
                    || (target.WeenieType == WeenieType.MissileLauncher)
                    || (target.WeenieType == WeenieType.Missile)
                    || (target.WeenieType == WeenieType.Door)
                    || (target.WeenieType == WeenieType.Chest)
                    || (target.CombatUse != null && target.CombatUse == ACE.Entity.Enum.CombatUse.Shield))
                    return false;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether a spell will be evaded, based upon the caster's magic skill vs target's magic defense skill
        /// </summary>
        /// <param name="casterMagicSkill"></param>
        /// <param name="targetMagicDefenseSkill"></param>
        /// <returns></returns>
        private static bool MagicDefenseCheck(uint casterMagicSkill, uint targetMagicDefenseSkill)
        {
            return Physics.Common.Random.RollDice(0.0f, 1.0f) < (1.0f - SkillCheck.GetSkillChance((int)casterMagicSkill, (int)targetMagicDefenseSkill));
        }

        /// <summary>
        /// Test if target resists the spell cast
        /// </summary>
        /// <param name="target"></param>
        /// <param name="spell"></param>
        /// <returns></returns>
        public bool? ResistSpell(WorldObject target, SpellBase spell)
        {
            if (this is Creature caster)
            {
                var player = caster as Player;
                var targetPlayer = target as Player;

                // Retrieve creature's skill level in the Magic School
                var creatureMagicSkill = caster.GetCreatureSkill(spell.School).Current;

                // Retrieve target's Magic Defense Skill
                Creature creature = target as Creature;
                var targetMagicDefenseSkill = creature.GetCreatureSkill(Skill.MagicDefense).Current;

                bool resisted = MagicDefenseCheck(creatureMagicSkill, targetMagicDefenseSkill);

                if (targetPlayer != null)
                    resisted |= targetPlayer.Invincible == true;

                if (resisted)
                {
                    if (player != null)
                    {
                        player.Session.Network.EnqueueSend(new GameMessageSystemChat($"{creature.Name} resists {spell.Name}", ChatMessageType.Magic));
                        player.Session.Network.EnqueueSend(new GameMessageSound(player.Guid, Sound.ResistSpell, 1.0f));
                    }
                    else
                    {
                        if (targetPlayer != null)
                        {
                            targetPlayer.Session.Network.EnqueueSend(new GameMessageSystemChat($"You resist the spell cast by {caster.Name}", ChatMessageType.Magic));
                            targetPlayer.Session.Network.EnqueueSend(new GameMessageSound(targetPlayer.Guid, Sound.ResistSpell, 1.0f));
                        }
                    }

                    return resisted;
                }
                return resisted;
            }

            return null;
        }

        /// <summary>
        /// Creates a Life Magic spell
        /// </summary>
        /// <param name="target"></param>
        /// <param name="spell"></param>
        /// <param name="spellStatMod"></param>
        /// <param name="damage"></param>
        /// <param name="critical"></param>
        /// <param name="enchantmentStatus"></param>
        /// <param name="itemCaster"></param>
        /// <returns></returns>
        protected bool LifeMagic(WorldObject target, SpellBase spell, Database.Models.World.Spell spellStatMod, out uint damage, out bool critical, out EnchantmentStatus enchantmentStatus, WorldObject itemCaster = null)
        {
            critical = false;
            string srcVital, destVital;
            enchantmentStatus = default(EnchantmentStatus);
            enchantmentStatus.stackType = StackType.None;
            GameMessageSystemChat targetMsg = null;

            Player player = null;
            Creature creature = null;
            if (this is Player)
                player = this as Player;
            else if (this is Creature)
                creature = this as Creature;

            Creature spellTarget;
            if (spell.BaseRangeConstant > 0)
                spellTarget = target as Creature;
            else
                spellTarget = this as Creature;

            // Target already dead
            if (spellTarget.Health.Current <= 0)
            {
                enchantmentStatus.message = null;
                damage = 0;
                return false;
            }

            int newSpellTargetVital;
            switch (spell.MetaSpellType)
            {
                case SpellType.Boost:
                    int minBoostValue, maxBoostValue;
                    if ((spellStatMod.BoostVariance + spellStatMod.Boost) < spellStatMod.Boost)
                    {
                        minBoostValue = (int)(spellStatMod.BoostVariance + spellStatMod.Boost);
                        maxBoostValue = (int)spellStatMod.Boost;
                    }
                    else
                    {
                        minBoostValue = (int)spellStatMod.Boost;
                        maxBoostValue = (int)(spellStatMod.BoostVariance + spellStatMod.Boost);
                    }
                    int boost = Physics.Common.Random.RollDice(minBoostValue, maxBoostValue);
                    if (boost <= 0)
                        damage = (uint)Math.Abs(boost);
                    else
                        damage = 0;

                    switch (spellStatMod.DamageType)
                    {
                        case 512:   // Mana
                            newSpellTargetVital = (int)Math.Min(spellTarget.Mana.Current + boost, spellTarget.Mana.MaxValue);
                            srcVital = "mana";
                            spellTarget.UpdateVital(spellTarget.Mana, (uint)newSpellTargetVital);
                            break;
                        case 256:   // Stamina
                            newSpellTargetVital = (int)Math.Min(spellTarget.Stamina.Current + boost, spellTarget.Stamina.MaxValue);
                            srcVital = "stamina";
                            spellTarget.UpdateVital(spellTarget.Stamina, (uint)newSpellTargetVital);
                            break;
                        default:   // Health
                            srcVital = "health";
                            if ((spellTarget.Health.Current <= 0) && (boost < 0))
                            {
                                boost = 0;
                                break;
                            }
                            newSpellTargetVital = (int)Math.Min(spellTarget.Health.Current + boost, spellTarget.Health.MaxValue);
                            spellTarget.UpdateVital(spellTarget.Health, (uint)newSpellTargetVital);

                            if (boost >= 0)
                                spellTarget.DamageHistory.OnHeal((uint)boost);
                            else
                                spellTarget.DamageHistory.Add(this, damage);
                            break;
                    }

                    if (this is Player)
                    {
                        if (spell.BaseRangeConstant > 0)
                        {
                            string msg;
                            if (boost <= 0)
                            {
                                msg = $"You drain {Math.Abs(boost).ToString()} points of {srcVital} from {spellTarget.Name}";
                                enchantmentStatus.message = new GameMessageSystemChat(msg, ChatMessageType.Combat);
                            }
                            else
                            {
                                msg = $"You restore {Math.Abs(boost).ToString()} points of {srcVital} to {spellTarget.Name}";
                                enchantmentStatus.message = new GameMessageSystemChat(msg, ChatMessageType.Magic);
                            }
                        }
                        else
                            enchantmentStatus.message = new GameMessageSystemChat($"You restore {Math.Abs(boost).ToString()} {srcVital}", ChatMessageType.Magic);
                    }
                    else
                        enchantmentStatus.message = null;

                    if (target is Player && spell.BaseRangeConstant > 0)
                    {
                        string msg;
                        if (boost <= 0)
                        {
                            msg = $"{Name} casts {spell.Name} and drains {Math.Abs(boost).ToString()} points of your {srcVital}";
                            targetMsg = new GameMessageSystemChat(msg, ChatMessageType.Combat);
                        }
                        else
                        {
                            msg = $"{Name} casts {spell.Name} and restores {Math.Abs(boost).ToString()} points of your {srcVital}";
                            targetMsg = new GameMessageSystemChat(msg, ChatMessageType.Magic);
                        }
                    }

                    if (player != null && srcVital != null && srcVital.Equals("health"))
                        player.Session.Network.EnqueueSend(new GameEventUpdateHealth(player.Session, target.Guid.Full, (float)spellTarget.Health.Current / spellTarget.Health.MaxValue));

                    break;

                case SpellType.Transfer:

                    // Calculate the change in vitals of the target
                    Creature caster;
                    if (spell.BaseRangeConstant == 0 && spell.BaseRangeMod == 1)
                        caster = spellTarget;
                    else
                        caster = (Creature)this;
                    uint vitalChange, casterVitalChange;
                    ResistanceType resistanceDrain, resistanceBoost;
                    if (spellStatMod.Source == (int)PropertyAttribute2nd.Mana)
                        resistanceDrain = ResistanceType.ManaDrain;
                    else if (spellStatMod.Source == (int)PropertyAttribute2nd.Stamina)
                        resistanceDrain = ResistanceType.StaminaDrain;
                    else
                        resistanceDrain = ResistanceType.HealthDrain;
                    vitalChange = (uint)((spellTarget.GetCurrentCreatureVital((PropertyAttribute2nd)spellStatMod.Source) * spellStatMod.Proportion) * spellTarget.GetNaturalResistance(resistanceDrain));
                    if (spellStatMod.TransferCap != 0)
                    {
                        if (vitalChange > spellStatMod.TransferCap)
                            vitalChange = (uint)spellStatMod.TransferCap;
                    }
                    if (spellStatMod.Destination == (int)PropertyAttribute2nd.Mana)
                        resistanceBoost = ResistanceType.ManaDrain;
                    else if (spellStatMod.Source == (int)PropertyAttribute2nd.Stamina)
                        resistanceBoost = ResistanceType.StaminaDrain;
                    else
                        resistanceBoost = ResistanceType.HealthDrain;
                    casterVitalChange = (uint)((vitalChange * (1.0f - spellStatMod.LossPercent)) * spellTarget.GetNaturalResistance(resistanceBoost));
                    vitalChange = (uint)(casterVitalChange / (1.0f - spellStatMod.LossPercent));

                    // Apply the change in vitals to the target
                    switch (spellStatMod.Source)
                    {
                        case (int)PropertyAttribute2nd.Mana:
                            srcVital = "mana";
                            vitalChange = (uint)-spellTarget.UpdateVitalDelta(spellTarget.Mana, -(int)vitalChange);
                            break;
                        case (int)PropertyAttribute2nd.Stamina:
                            srcVital = "stamina";
                            vitalChange = (uint)-spellTarget.UpdateVitalDelta(spellTarget.Stamina, -(int)vitalChange);
                            break;
                        default:   // Health
                            srcVital = "health";
                            vitalChange = (uint)-spellTarget.UpdateVitalDelta(spellTarget.Health, -(int)vitalChange);
                            spellTarget.DamageHistory.Add(this, vitalChange);
                            break;
                    }
                    damage = vitalChange;

                    // Apply the scaled change in vitals to the caster
                    switch (spellStatMod.Destination)
                    {
                        case (int)PropertyAttribute2nd.Mana:
                            destVital = "mana";
                            casterVitalChange = (uint)caster.UpdateVitalDelta(caster.Mana, casterVitalChange);
                            break;
                        case (int)PropertyAttribute2nd.Stamina:
                            destVital = "stamina";
                            casterVitalChange = (uint)caster.UpdateVitalDelta(caster.Stamina, casterVitalChange);
                            break;
                        default:   // Health
                            destVital = "health";
                            casterVitalChange = (uint)caster.UpdateVitalDelta(caster.Health, casterVitalChange);
                            caster.DamageHistory.OnHeal(casterVitalChange);
                            break;
                    }

                    if (this is Player)
                    {
                        if (target.Guid == player.Guid)
                        {
                            enchantmentStatus.message = new GameMessageSystemChat($"You drain {vitalChange} points of {srcVital} and apply {casterVitalChange} points of {destVital} to yourself", ChatMessageType.Magic);
                        }
                        else
                            enchantmentStatus.message = new GameMessageSystemChat($"You drain {vitalChange} points of {srcVital} from {spellTarget.Name} and apply {casterVitalChange} to yourself", ChatMessageType.Combat);
                    }
                    else
                        enchantmentStatus.message = null;

                    if (target is Player && target != this)
                        targetMsg = new GameMessageSystemChat($"You lose {vitalChange} points of {srcVital} due to {Name} casting {spell.Name} on you", ChatMessageType.Combat);

                    if (player != null && srcVital != null && srcVital.Equals("health"))
                        player.Session.Network.EnqueueSend(new GameEventUpdateHealth(player.Session, target.Guid.Full, (float)spellTarget.Health.Current / spellTarget.Health.MaxValue));

                    break;

                case SpellType.LifeProjectile:

                    caster = (Creature)this;

                    if (spell.Name.Contains("Blight"))
                    {
                        var tryDamage = (int)Math.Round(caster.GetCurrentCreatureVital(PropertyAttribute2nd.Mana) * caster.GetNaturalResistance(ResistanceType.ManaDrain));
                        damage = (uint)-caster.UpdateVitalDelta(caster.Mana, -tryDamage);
                    }
                    else if (spell.Name.Contains("Tenacity"))
                    {
                        var tryDamage = (int)Math.Round(spellTarget.GetCurrentCreatureVital(PropertyAttribute2nd.Stamina) * spellTarget.GetNaturalResistance(ResistanceType.StaminaDrain));
                        damage = (uint)-caster.UpdateVitalDelta(caster.Stamina, -tryDamage);
                    }
                    else
                    {
                        var tryDamage = (int)Math.Round(spellTarget.GetCurrentCreatureVital(PropertyAttribute2nd.Stamina) * spellTarget.GetNaturalResistance(ResistanceType.HealthDrain));
                        damage = (uint)-caster.UpdateVitalDelta(caster.Health, -tryDamage);
                        caster.DamageHistory.Add(this, damage);
                    }

                    var sp = CreateSpellProjectile(spell.MetaSpellId, (uint)spellStatMod.Wcid, target, damage);
                    LaunchSpellProjectile(sp);

                    if (caster.Health.Current <= 0)
                        caster.Die();

                    enchantmentStatus.message = null;
                    break;

                case SpellType.Dispel:
                    damage = 0;
                    enchantmentStatus.message = new GameMessageSystemChat("Spell not implemented, yet!", ChatMessageType.Magic);
                    break;

                case SpellType.Enchantment:
                    damage = 0;
                    if (itemCaster != null)
                        enchantmentStatus = CreateEnchantment(target, itemCaster, spell, spellStatMod);
                    else
                        enchantmentStatus = CreateEnchantment(target, this, spell, spellStatMod);
                    break;

                default:
                    damage = 0;
                    enchantmentStatus.message = new GameMessageSystemChat("Spell not implemented, yet!", ChatMessageType.Magic);
                    break;
            }

            if (targetMsg != null)
                (target as Player).Session.Network.EnqueueSend(targetMsg);

            if (spellTarget.Health.Current == 0)
                return true;

            return false;
        }

        /// <summary>
        /// Wrapper around CreateEnchantment for Creature Magic
        /// </summary>
        /// <param name="target"></param>
        /// <param name="spell"></param>
        /// <param name="spellStatMod"></param>
        /// <param name="itemCaster"></param>
        /// <returns></returns>
        protected EnchantmentStatus CreatureMagic(WorldObject target, SpellBase spell, Database.Models.World.Spell spellStatMod, WorldObject itemCaster = null)
        {
            if (itemCaster != null)
                return CreateEnchantment(target, itemCaster, spell, spellStatMod);

            return CreateEnchantment(target, this, spell, spellStatMod);
        }

        /// <summary>
        /// Item Magic
        /// </summary>
        /// <param name="target"></param>
        /// <param name="spell"></param>
        /// <param name="spellStatMod"></param>
        /// <param name="itemCaster"></param>
        protected EnchantmentStatus ItemMagic(WorldObject target, SpellBase spell, Database.Models.World.Spell spellStatMod, WorldObject itemCaster = null)
        {
            EnchantmentStatus enchantmentStatus = default(EnchantmentStatus);
            enchantmentStatus.message = null;
            enchantmentStatus.stackType = StackType.None;

            Player player = CurrentLandblock?.GetObject(Guid) as Player;
            if (player == null && ((this as Player) != null)) player = this as Player;

            if ((spell.MetaSpellType == SpellType.PortalLink)
                || (spell.MetaSpellType == SpellType.PortalRecall)
                || (spell.MetaSpellType == SpellType.PortalSending)
                || (spell.MetaSpellType == SpellType.PortalSummon))
            {
                var targetPlayer = target as Player;

                switch (spell.MetaSpellType)
                {
                    case SpellType.PortalRecall:
                        PositionType recall = PositionType.Undef;
                        switch (spell.MetaSpellId)
                        {
                            case 2645: // Portal Recall
                                if (player.GetPosition(PositionType.LastPortal) == null)
                                {
                                    // You must link to a portal to recall it!
                                    player.Session.Network.EnqueueSend(new GameEventWeenieError(player.Session, WeenieError.YouMustLinkToPortalToRecall));
                                }
                                else
                                    recall = PositionType.LastPortal;
                                break;
                            case 1635: // Lifestone Recall
                                if (player.GetPosition(PositionType.LinkedLifestone) == null)
                                {
                                    // You must link to a lifestone to recall it!
                                    player.Session.Network.EnqueueSend(new GameEventWeenieError(player.Session, WeenieError.YouMustLinkToLifestoneToRecall));
                                }
                                else
                                    recall = PositionType.LinkedLifestone;
                                break;
                            case 48: // Primary Portal Recall
                                if (player.GetPosition(PositionType.LinkedPortalOne) == null)
                                {
                                    // You must link to a portal to recall it!
                                    player.Session.Network.EnqueueSend(new GameEventWeenieError(player.Session, WeenieError.YouMustLinkToPortalToRecall));
                                }
                                else
                                    recall = PositionType.LinkedPortalOne;
                                break;
                            case 2647: // Secondary Portal Recall
                                if (player.GetPosition(PositionType.LinkedPortalTwo) == null)
                                {
                                    // You must link to a portal to recall it!
                                    player.Session.Network.EnqueueSend(new GameEventWeenieError(player.Session, WeenieError.YouMustLinkToPortalToRecall));
                                }
                                else
                                    recall = PositionType.LinkedPortalTwo;
                                break;
                        }

                        if (recall != PositionType.Undef)
                        {
                            ActionChain portalRecallChain = new ActionChain();
                            portalRecallChain.AddDelaySeconds(2.0f);  // 2 second delay
                            portalRecallChain.AddAction(targetPlayer, () => player.TeleToPosition(recall));
                            portalRecallChain.EnqueueChain();
                        }
                        break;
                    case SpellType.PortalSending:
                        if (targetPlayer != null)
                        {
                            var destination = new ACE.Entity.Position((uint)spellStatMod.PositionObjCellId, (float)spellStatMod.PositionOriginX,
                                (float)spellStatMod.PositionOriginY, (float)spellStatMod.PositionOriginZ, (float)spellStatMod.PositionAnglesX,
                                (float)spellStatMod.PositionAnglesY, (float)spellStatMod.PositionAnglesZ, (float)spellStatMod.PositionAnglesW);
                            if (destination != null)
                            {
                                ActionChain portalSendingChain = new ActionChain();
                                portalSendingChain.AddDelaySeconds(2.0f);  // 2 second delay
                                portalSendingChain.AddAction(targetPlayer, () => targetPlayer.Teleport(destination));
                                portalSendingChain.EnqueueChain();
                            }
                        }
                        break;
                    case SpellType.PortalLink:
                        if (player != null)
                        {
                            switch (spell.MetaSpellId)
                            {
                                case 47:    // Primary Portal Tie
                                    if (target.WeenieType == WeenieType.Portal)
                                    {
                                        var targetPortal = target as Portal;
                                        if (!targetPortal.NoTie)
                                            player.LinkedPortalOne = targetPortal.Destination;
                                        else
                                            player.Session.Network.EnqueueSend(new GameEventWeenieError(player.Session, WeenieError.YouCannotLinkToThatPortal));
                                    }
                                    else
                                        player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, $"Primary Portal Tie cannot be cast on {target.Name}"));
                                    break;
                                case 2644:  // Lifestone Tie
                                    if (target.WeenieType == WeenieType.LifeStone)
                                        player.LinkedLifestone = target.Location;
                                    else
                                        player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, $"Lifestone Tie cannot be cast on {target.Name}"));
                                    break;
                                case 2646:  // Secondary Portal Tie
                                    if (target.WeenieType == WeenieType.Portal)
                                    {
                                        var targetPortal = target as Portal;
                                        if (!targetPortal.NoTie)
                                            player.LinkedPortalTwo = targetPortal.Destination;
                                        else
                                            player.Session.Network.EnqueueSend(new GameEventWeenieError(player.Session, WeenieError.YouCannotLinkToThatPortal));
                                    }
                                    else
                                        player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, $"Secondary Portal Tie cannot be cast on {target.Name}"));
                                    break;
                            }
                        }
                        break;
                    case SpellType.PortalSummon:
                        uint portalId = 0;
                        ACE.Entity.Position linkedPortal = null;
                        if (itemCaster != null)
                        {
                            portalId = itemCaster.GetProperty(PropertyDataId.LinkedPortalOne) ?? 0;
                        }
                        else
                        {
                            if (spell.Name.Contains("Summon Primary"))
                            {
                                linkedPortal = GetPosition(PositionType.LinkedPortalOne);
                            }
                            if (spell.Name.Contains("Summon Secondary"))
                            {
                                linkedPortal = GetPosition(PositionType.LinkedPortalTwo);
                            }

                            if (linkedPortal != null)
                                portalId = 1955;
                        }

                        if (portalId != 0)
                        {
                            var portal = WorldObjectFactory.CreateNewWorldObject(portalId);
                            portal.SetupTableId = 33556212;
                            portal.RadarBehavior = ACE.Entity.Enum.RadarBehavior.ShowNever;
                            portal.Name = "Gateway";
                            portal.Location = Location.InFrontOf();

                            if (portalId == 1955)
                                portal.Destination = linkedPortal;

                            portal.EnterWorld();

                            // Create portal decay
                            double portalDespawnTime = spellStatMod.PortalLifetime ?? 60.0f;
                            ActionChain despawnChain = new ActionChain();
                            despawnChain.AddDelaySeconds(portalDespawnTime);
                            despawnChain.AddAction(portal, () => portal.CurrentLandblock?.RemoveWorldObject(portal.Guid, false));
                            despawnChain.EnqueueChain();
                        }
                        else
                        {
                            // You must link to a portal to summon it!
                            player.Session.Network.EnqueueSend(new GameEventWeenieError(player.Session, WeenieError.YouMustLinkToPortalToSummonIt));
                        }
                        break;
                    case SpellType.FellowPortalSending:
                        if (targetPlayer != null)
                            enchantmentStatus.message = new GameMessageSystemChat("Spell not implemented, yet!", ChatMessageType.Magic);
                        break;
                }
            }
            else if (spell.MetaSpellType == SpellType.Enchantment)
            {
                if (itemCaster != null)
                    return CreateEnchantment(target, itemCaster, spell, spellStatMod);

                return CreateEnchantment(target, this, spell, spellStatMod);
            }

            return enchantmentStatus;
        }

        /// <summary>
        /// Untargeted War Magic
        /// </summary>
        /// <param name="spell"></param>
        /// <param name="spellStatMod"></param>
        protected void WarMagic(SpellBase spell, Database.Models.World.Spell spellStatMod)
        {
            var spellType = SpellProjectile.GetProjectileSpellType(spell.MetaSpellId);

            if (spellType == SpellProjectile.ProjectileSpellType.Ring)
            {
                var spellProjectiles = CreateRingProjectiles(spell.MetaSpellId, spellStatMod);
                LaunchSpellProjectiles(spellProjectiles);
            }
            else if (spellType == SpellProjectile.ProjectileSpellType.Wall)
            {
                var spellProjectiles = CreateWallProjectiles(spell.MetaSpellId, spellStatMod);
                LaunchSpellProjectiles(spellProjectiles);
            }
            else
            {
                if (WeenieClassId == 1)
                {
                    Player player = (Player)this;
                    player.Session.Network.EnqueueSend(new GameEventUseDone(player.Session, errorType: WeenieError.None),
                        new GameMessageSystemChat($"{spell.Name} spell not implemented, yet!", ChatMessageType.System));
                }
            }
        }

        /// <summary>
        /// Targeted War Magic
        /// </summary>
        /// <param name="target"></param>
        /// <param name="spell"></param>
        /// <param name="spellStatMod"></param>
        protected void WarMagic(WorldObject target, SpellBase spell, Database.Models.World.Spell spellStatMod)
        {
            var spellType = SpellProjectile.GetProjectileSpellType(spell.MetaSpellId);
            // Bolt, Streak, Arc
            if (spellStatMod.NumProjectiles == 1)
            {
                var sp = CreateSpellProjectile(spell.MetaSpellId, (uint)spellStatMod.Wcid, target);
                LaunchSpellProjectile(sp);
            }
            else if (spellType == SpellProjectile.ProjectileSpellType.Volley)
            {
                var spellProjectiles = CreateVolleyProjectiles(target, spell.MetaSpellId, (uint)spellStatMod.Wcid,
                    spellStatMod.NumProjectiles.GetValueOrDefault());
                LaunchSpellProjectiles(spellProjectiles);
            }
            else if (spellType == SpellProjectile.ProjectileSpellType.Blast)
            {
                var spellProjectiles = CreateBlastProjectiles(target, spell.MetaSpellId, spellStatMod);
                LaunchSpellProjectiles(spellProjectiles);
            }
            else
            {
                if (WeenieClassId == 1)
                {
                    Player player = (Player)this;
                    player.Session.Network.EnqueueSend(new GameEventUseDone(player.Session, errorType: WeenieError.None),
                        new GameMessageSystemChat($"{spell.Name} spell not implemented, yet!", ChatMessageType.System));
                }
            }
        }

        /// <summary>
        /// Void Magic
        /// </summary>
        /// <param name="target"></param>
        /// <param name="spell"></param>
        /// <param name="spellStatMod"></param>
        protected void VoidMagic(WorldObject target, SpellBase spell, Database.Models.World.Spell spellStatMod)
        {
            if (WeenieClassId == 1)
            {
                Player player = (Player)this;
                player.Session.Network.EnqueueSend(new GameEventUseDone(player.Session, errorType: WeenieError.None),
                    new GameMessageSystemChat($"{spell.Name} spell not implemented, yet!", ChatMessageType.System));
            }
        }

        /// <summary>
        /// Creates an enchantment and interacts with the Enchantment registry.
        /// Used by Life, Creature, Item, and Void magic
        /// </summary>
        /// <param name="target"></param>
        /// <param name="caster"></param>
        /// <param name="spell"></param>
        /// <param name="spellStatMod"></param>
        /// <returns></returns>
        private EnchantmentStatus CreateEnchantment(WorldObject target, WorldObject caster, SpellBase spell, Database.Models.World.Spell spellStatMod)
        {
            EnchantmentStatus enchantmentStatus = default(EnchantmentStatus);
            double duration;

            if (caster is Creature)
                duration = spell.Duration;
            else
            {
                if (caster.WeenieType == WeenieType.Gem)
                    duration = spell.Duration;
                else
                    duration = -1;
            }

            // create enchantment
            var enchantment = new Enchantment(target, caster.Guid, spellStatMod.Id, duration, 1, (uint)EnchantmentMask.CreatureSpells);
            var stackType = target.EnchantmentManager.Add(enchantment, caster);

            var player = this as Player;
            var playerTarget = target as Player;
            var creatureTarget = target as Creature;

            // build message
            var suffix = "";
            switch (stackType)
            {
                case StackType.Refresh:
                    suffix = $", refreshing {spell.Name}";
                    break;
                case StackType.Surpass:
                    suffix = $", surpassing {target.EnchantmentManager.Surpass.Name}";
                    break;
                case StackType.Surpassed:
                    suffix = $", but it is surpassed by {target.EnchantmentManager.Surpass.Name}";
                    break;
            }

            var targetName = this == target ? "yourself" : target.Name;

            string message;
            if (stackType == StackType.Undef)
                message = null;
            else
            {
                if (stackType == StackType.None)
                    message = null;
                else
                {
                    if (caster is Creature)
                    {
                        if (caster.Guid == Guid)
                            message = $"You cast {spell.Name} on {targetName}{suffix}";
                        else
                            message = $"{caster.Name} casts {spell.Name} on {targetName}{suffix}"; // for the sentinel command `/buff [target player name]`
                    }
                    else
                    {
                        if (target.Name != caster.Name)
                            message = $"{caster.Name} casts {spell.Name} on you{suffix}";
                        else
                            message = null;
                    }
                }
            }

            if (target is Player)
            {
                if (stackType != StackType.Undef)
                {
                    if (stackType != StackType.Surpassed)
                        playerTarget.Session.Network.EnqueueSend(new GameEventMagicUpdateEnchantment(playerTarget.Session, enchantment));

                    if (playerTarget != this)
                        playerTarget.Session.Network.EnqueueSend(new GameMessageSystemChat($"{Name} cast {spell.Name} on you{suffix}", ChatMessageType.Magic));
                }
            }

            if (message != null)
                enchantmentStatus.message = new GameMessageSystemChat(message, ChatMessageType.Magic);
            else
                enchantmentStatus.message = null;
            enchantmentStatus.stackType = stackType;
            return enchantmentStatus;
        }


        /// <summary>
        /// Creates the Magic projectile spells for Life, War, and Void Magic
        /// </summary>
        /// <param name="spellId"></param>
        /// <param name="projectileWcid"></param>
        /// <param name="target"></param>
        /// <param name="lifeProjectileDamage"></param>
        /// <param name="origin"></param>
        /// <param name="velocity"></param>
        /// <returns></returns>
        private SpellProjectile CreateSpellProjectile(uint spellId, uint projectileWcid, WorldObject target = null, uint lifeProjectileDamage = 0, Position origin = null, AceVector3 velocity = null)
        {
            SpellProjectile spellProjectile = WorldObjectFactory.CreateNewWorldObject(projectileWcid) as SpellProjectile;
            spellProjectile.Setup(spellId);

            var useGravity = spellProjectile.SpellType == SpellProjectile.ProjectileSpellType.Arc;

            if (target != null)
            {
                var globalDest = target.Location.ToGlobal();
                globalDest.Z += target.Height / 2.0f;
                var globalOrigin = GetSpellProjectileOrigin(this, spellProjectile, globalDest);
                float dist = (globalDest - globalOrigin).Length();
                float speed = GetSpellProjectileSpeed(spellProjectile.SpellType, dist);

                spellProjectile.DistanceToTarget = dist;
                Position localPos = Location.FromGlobal(globalOrigin);
                spellProjectile.Location = new Position(localPos.LandblockId.Raw, localPos.Pos, this.Location.Rotation);
                spellProjectile.Velocity = GetSpellProjectileVelocity(globalOrigin, target, globalDest, speed, useGravity, out var time);
            }
            // We don't have a target and want to override the projectile origin and velocity
            else
            {
                if (velocity == null)
                {
                    log.Warn($"Untargeted or secondary spell projectiles must have a velocity set.");
                    return spellProjectile;
                }
                spellProjectile.Velocity = velocity;

                if (origin == null)
                {
                    log.Warn($"Untargeted or secondary spell projectiles must have an origin (creation location) set.");
                    return spellProjectile;
                }
                spellProjectile.Location = origin;
            }

            spellProjectile.LifeProjectileDamage = lifeProjectileDamage;
            spellProjectile.ProjectileSource = this;
            spellProjectile.ProjectileTarget = target;
            spellProjectile.SetProjectilePhysicsState(spellProjectile.ProjectileTarget, useGravity);
            spellProjectile.SpawnPos = new Position(spellProjectile.Location);

            return spellProjectile;
        }

        /// <summary>
        /// Creates a spell projectile in the world.
        /// </summary>
        /// <param name="sp"></param>
        private void LaunchSpellProjectile(SpellProjectile sp)
        {
            if (sp.Location == null)
            {
                log.Warn("A spell projectile could not be spawned. Location must not be null.");
                return;
            }

            if (sp.Velocity == null)
            {
                log.Warn("A spell projectile could not be spawned. Velocity must not be null.");
                return;
            }

            LandblockManager.AddObject(sp);
            sp.EnqueueBroadcast(new GameMessageScript(sp.Guid, ACE.Entity.Enum.PlayScript.Launch, sp.PlayscriptIntensity));

            if (sp.ProjectileTarget == null)
                return;

            // Detonate point-blank projectiles immediately
            var radsum = sp.ProjectileTarget.PhysicsObj.GetRadius() + sp.PhysicsObj.GetRadius();
            if (sp.DistanceToTarget < radsum)
                sp.OnCollideObject(sp.ProjectileTarget);
        }

        /// <summary>
        /// Creates multiple spell projectiles in the world.
        /// </summary>
        /// <param name="spellProjectiles"></param>
        private void LaunchSpellProjectiles(List<SpellProjectile> spellProjectiles)
        {
            foreach (var sp in spellProjectiles)
            {
                LaunchSpellProjectile(sp);
            }
        }

        /// <summary>
        /// Calculates the spell projectile origin based on the targets global destination.
        /// </summary>
        /// <param name="caster"></param>
        /// <param name="spellProjectile"></param>
        /// <param name="globalDest"></param>
        /// <returns></returns>
        private Vector3 GetSpellProjectileOrigin(WorldObject caster, SpellProjectile spellProjectile, Vector3 globalDest)
        {
            var globalOrigin = caster.Location.ToGlobal();
            if (spellProjectile.SpellType == SpellProjectile.ProjectileSpellType.Arc)
                globalOrigin.Z += caster.Height;
            else
                globalOrigin.Z += caster.Height * 2.0f / 3.0f;

            var direction = Vector3.Normalize(globalDest - globalOrigin);

            // This is not perfect but is close to values that retail used. TODO: revisit this later.
            globalOrigin += direction * (caster.PhysicsObj.GetRadius() + spellProjectile.PhysicsObj.GetRadius());

            return globalOrigin;
        }

        /// <summary>
        /// Gets the speed of a projectile based on the distance to the target.
        /// </summary>
        /// <param name="spellType"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        private float GetSpellProjectileSpeed(SpellProjectile.ProjectileSpellType spellType, float distance)
        {
            float speed;

            // TODO:
            // Speed seems to increase when target is moving away from the caster and decrease when
            // the target is moving toward the caster. This still needs more research.
            switch (spellType)
            {
                case SpellProjectile.ProjectileSpellType.Bolt:
                case SpellProjectile.ProjectileSpellType.Volley:
                case SpellProjectile.ProjectileSpellType.Blast:
                    speed = GetStationarySpeed(15f, distance);
                    break;
                case SpellProjectile.ProjectileSpellType.Streak:
                    speed = GetStationarySpeed(45f, distance);
                    break;
                case SpellProjectile.ProjectileSpellType.Arc:
                    speed = GetStationarySpeed(40f, distance);
                    break;
                default:
                    speed = 15f;
                    break;
            }

            return speed;
        }

        /// <summary>
        /// Creates a list of volley spell projectiles ready for creation in the world.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="spellId"></param>
        /// <param name="projectileWcid"></param>
        /// <param name="numProjectiles"></param>
        /// <returns></returns>
        private List<SpellProjectile> CreateVolleyProjectiles(WorldObject target, uint spellId, uint projectileWcid, int numProjectiles)
        {
            var spellProjectiles = new List<SpellProjectile>();
            var centerProjectile = CreateSpellProjectile(spellId, projectileWcid, target);
            spellProjectiles.Add(centerProjectile);
            var projectileOrigins = GetVolleyProjectileOrigins(centerProjectile, numProjectiles);

            foreach (var origin in projectileOrigins)
            {
                spellProjectiles.Add(
                    CreateSpellProjectile(spellId, projectileWcid, velocity: centerProjectile.Velocity, origin: origin)
                );
            }

            return spellProjectiles;
        }

        /// <summary>
        /// Gets volley projectile origins based on the position of the center projectile.
        /// </summary>
        /// <param name="centerProjectile"></param>
        /// <param name="numProjectiles"></param>
        /// <returns></returns>
        List<Position> GetVolleyProjectileOrigins(SpellProjectile centerProjectile, int numProjectiles)
        {
            var origins = new List<Position>();
            // Lightning projectiles (WCID 1635) get a little more padding since they have a bigger radius
            var xOffsets = centerProjectile.WeenieClassId == 1635 ? new List<float> { -1.3f, 1.3f, -2.6f, 2.6f } : new List<float> { -1.2f, 1.2f, -2.4f, 2.4f };

            for (int i = 0; i < numProjectiles-1; i++)
            {
                var projOrigin = new Position(centerProjectile.Location);
                // Rotate and add offset to get the new projectile position then rotate back to the original heading
                var originPosition = RotatePosition(projOrigin.Pos, projOrigin.Rotation);
                originPosition += new Vector3(xOffsets[i], 0, 0);
                projOrigin.SetPosition(Vector3.Transform(originPosition, projOrigin.Rotation));
                projOrigin.LandblockId = new LandblockId(projOrigin.GetCell());
                origins.Add(projOrigin);
            }

            return origins;
        }

        /// <summary>
        /// Creates a list of blast spell projectiles ready for creation in the world.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="spellId"></param>
        /// <param name="spellStatMod"></param>
        /// <returns></returns>
        private List<SpellProjectile> CreateBlastProjectiles(WorldObject target, uint spellId, Database.Models.World.Spell spellStatMod)
        {
            var spellProjectiles = GetSpreadProjectiles(spellId, spellStatMod, target);
            return spellProjectiles;
        }

        /// <summary>
        /// Creates a list of ring spell projectiles ready for creation in the world.
        /// </summary>
        /// <param name="spellId"></param>
        /// <param name="spellStatMod"></param>
        /// <returns></returns>
        private List<SpellProjectile> CreateRingProjectiles(uint spellId, Database.Models.World.Spell spellStatMod)
        {
            Vector3 originOffset = GetRingOriginOffset(spellId, (uint) spellStatMod.Wcid);
            AceVector3 velocity = GetRingVelocity(spellId, (uint)spellStatMod.Wcid);

            var spellProjectiles = GetSpreadProjectiles(spellId, spellStatMod, originOffset: originOffset, velocity: velocity);

            return spellProjectiles;
        }

        /// <summary>
        /// Gets the XYZ offsets for a ring spell projectile.
        /// </summary>
        /// <param name="spellId"></param>
        /// <param name="projectileWcid"></param>
        /// <returns></returns>
        private Vector3 GetRingOriginOffset(uint spellId, uint projectileWcid)
        {
            if (projectileWcid >= 7269 && projectileWcid <= 7275)
            {
                var zOffset = this.Height * 2 / 3;
                return new Vector3(0f, 0.82f, zOffset);
            }

            return Vector3.Zero;
        }

        /// <summary>
        /// Gets the default velocity for a ring spell projectile.
        /// </summary>
        /// <param name="spellId"></param>
        /// <param name="projectileWcid"></param>
        /// <returns></returns>
        private AceVector3 GetRingVelocity(uint spellId, uint projectileWcid)
        {
            if (projectileWcid >= 7269 && projectileWcid <= 7275)
                return new AceVector3(0f, 2f, 0);

            return new AceVector3(0, 0, 0);
        }

        /// <summary>
        /// Creates a list of spell projectiles which use spread angles (Blast or Ring spells).
        /// </summary>
        /// <param name="spellId"></param>
        /// <param name="spellStatMod"></param>
        /// <param name="target"></param>
        /// <param name="originOffset"></param>
        /// <param name="velocity"></param>
        /// <returns></returns>
        private List<SpellProjectile> GetSpreadProjectiles(uint spellId, Database.Models.World.Spell spellStatMod,
            WorldObject target = null, Vector3? originOffset = null, AceVector3 velocity = null)
        {
            var spellProjectiles = new List<SpellProjectile>();

            // The first projectile is always created directly in front of the caster
            SpellProjectile centerProjectile;
            var casterLocalOrigin = RotatePosition(this.Location.Pos, this.Location.Rotation);

            if (target != null) // Blast spells
            {
                centerProjectile = CreateSpellProjectile(spellId, (uint)spellStatMod.Wcid, target);
                var localOrigin = RotatePosition(centerProjectile.Location.Pos, this.Location.Rotation);
                originOffset = new Vector3(0, Math.Abs(localOrigin.Y - casterLocalOrigin.Y), 0);
                var localVelocity = RotatePosition(centerProjectile.Velocity.Get(), this.Location.Rotation);
                velocity = new AceVector3(localVelocity.X, localVelocity.Y, localVelocity.Z);
            }
            else // Ring spells
            {
                if (originOffset == null)
                {
                    log.Warn($"Untargeted spread angle spell projectiles must have an origin offset set.");
                    return spellProjectiles;
                }
                if (velocity == null)
                {
                    log.Warn($"Untargeted spread angle spell projectiles must have a default velocity set.");
                    return spellProjectiles;
                }

                var projOrigin = new Position(this.Location);
                projOrigin.SetPosition(Vector3.Transform(casterLocalOrigin + (Vector3) originOffset,
                    this.Location.Rotation));
                projOrigin.LandblockId = new LandblockId(projOrigin.GetCell());
                var globalVelocity = Vector3.Transform(velocity.Get(), this.Location.Rotation);
                centerProjectile = CreateSpellProjectile(spellId, (uint)spellStatMod.Wcid,
                    origin: projOrigin, velocity: new AceVector3(globalVelocity.X, globalVelocity.Y, globalVelocity.Z));
            }

            var numProjectiles = spellStatMod.NumProjectiles.GetValueOrDefault();
            var spreadAngle = spellStatMod.SpreadAngle.GetValueOrDefault();
            spellProjectiles.Add(centerProjectile);
            if (spellStatMod.NumProjectiles == 1)
                return spellProjectiles;

            float degrees = spreadAngle / (numProjectiles - 1);
            int oddEvenCounter = 1;

            for (int i = 1; i < numProjectiles; i++)
            {
                // Odd numbers are created on the -X axis (left of caster) and even are on the +X axis
                var radians = (float)(oddEvenCounter * degrees * Math.PI / 180);
                Quaternion localProjRotation;
                if (i % 2 != 0)
                {
                    localProjRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, radians);
                }
                else
                {
                    localProjRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(2 * Math.PI) - radians);
                    oddEvenCounter++;
                }

                var localProjLocation = Vector3.Transform((Vector3)originOffset, localProjRotation);
                var projOrigin = new Position(this.Location);
                projOrigin.SetPosition(Vector3.Transform(casterLocalOrigin + localProjLocation,
                    this.Location.Rotation));
                projOrigin.LandblockId = new LandblockId(projOrigin.GetCell());
                // Make sure Z component matches the center projectile
                projOrigin.PositionZ = centerProjectile.Location.PositionZ;
                var localProjVelocity = Vector3.Transform(velocity.Get(), localProjRotation);
                var globalProjVelocity = Vector3.Transform(localProjVelocity, this.Location.Rotation);
                spellProjectiles.Add(
                    CreateSpellProjectile(spellId, (uint)spellStatMod.Wcid, origin: projOrigin,
                    velocity: new AceVector3(globalProjVelocity.X, globalProjVelocity.Y, globalProjVelocity.Z)
                ));
            }

            return spellProjectiles;
        }

        /// <summary>
        /// Creates a list of wall spell projectiles ready for creation in the world.
        /// </summary>
        /// <param name="spellId"></param>
        /// <param name="spellStatMod"></param>
        /// <returns></returns>
        private List<SpellProjectile> CreateWallProjectiles(uint spellId, Database.Models.World.Spell spellStatMod)
        {
            var spellProjectiles = new List<SpellProjectile>();
            var projectileOrigins = GetWallProjectileOrigins(spellId, spellStatMod);
            var velocity = GetWallProjectileVelocity(spellId, spellStatMod);

            foreach (var origin in projectileOrigins)
            {
                spellProjectiles.Add(
                    CreateSpellProjectile(spellId, (uint)spellStatMod.Wcid, velocity: velocity, origin: origin)
                );
            }

            return spellProjectiles;
        }

        /// <summary>
        /// Gets the XYZ offsets for wall spell projectiles.
        /// </summary>
        /// <param name="spellId"></param>
        /// <param name="spellStatMod"></param>
        /// <returns></returns>
        private List<Position> GetWallProjectileOrigins(uint spellId, Database.Models.World.Spell spellStatMod)
        {
            List<Vector3> offsetList;
            var isTuskerFists = spellId == 2934;
            var defaultZOffset = this.Height * 2.0f / 3.0f;
            // Lightning spells get some additional padding
            var zPadding = (spellStatMod.Wcid == 7280) ? 1.3f : 1.2f;
            var xPadding = (spellStatMod.Wcid == 7280) ? 0.1f : 0f;
            var topRowZOffset = defaultZOffset + zPadding;

            if (isTuskerFists)
            {
                offsetList = new List<Vector3>
                {
                    new Vector3(0f, 3.2f, defaultZOffset), // Bottom row
                    new Vector3(0f, 4.4f, defaultZOffset), // This front bottom row projectile is shifted back 1 meter
                    new Vector3(1f, 3.2f, defaultZOffset),
                    new Vector3(1f, 5.4f, defaultZOffset),
                    new Vector3(-1f, 3.2f, defaultZOffset),
                    new Vector3(-1f, 5.4f, defaultZOffset),
                    new Vector3(2f, 3.2f, defaultZOffset),
                    new Vector3(2f, 5.4f, defaultZOffset),
                    new Vector3(0f, 3.2f, topRowZOffset),  // Top row
                    new Vector3(0f, 5.4f, topRowZOffset),
                    new Vector3(1f, 3.2f, topRowZOffset),
                    new Vector3(1f, 5.4f, topRowZOffset),
                    new Vector3(-1f, 3.2f, topRowZOffset),
                    new Vector3(-1f, 5.4f, topRowZOffset),
                    new Vector3(2f, 3.2f, topRowZOffset),
                    new Vector3(2f, 5.4f, topRowZOffset)
                };
            }
            else
            {
                offsetList = new List<Vector3> {
                    new Vector3(0f, 3.2f, defaultZOffset),                     // Center bottom
                    new Vector3(0f, 3.2f, topRowZOffset),                      // Center top
                    new Vector3(-2f - (2 * xPadding), 3.2f, defaultZOffset),   // Far left bottom
                    new Vector3(-1f - xPadding, 3.2f, defaultZOffset),         // Near left bottom
                    new Vector3(1f + xPadding, 3.2f, defaultZOffset),          // Near right bottom
                    new Vector3(2f + (2 * xPadding), 3.2f, defaultZOffset),    // Far right bottom
                    new Vector3(-2f - (2 * xPadding), 3.2f, topRowZOffset),    // Far left top
                    new Vector3(-1f - xPadding, 3.2f, topRowZOffset),          // Near left top
                    new Vector3(1f + xPadding, 3.2f, topRowZOffset),           // Near right top
                    new Vector3(2f + (2 * xPadding), 3.2f, topRowZOffset),     // Far right top
                };
            }

            var origins = new List<Position>();
            for (int i = 0; i < spellStatMod.NumProjectiles; i++)
            {
                var projOrigin = new Position(this.Location);
                // Rotate and add offset to get the new projectile position then rotate back to the original heading
                var originPosition = RotatePosition(projOrigin.Pos, projOrigin.Rotation);
                originPosition += offsetList[i];
                projOrigin.SetPosition(Vector3.Transform(originPosition, projOrigin.Rotation));
                projOrigin.LandblockId = new LandblockId(projOrigin.GetCell());
                origins.Add(projOrigin);
            }

            return origins;
        }

        /// <summary>
        /// Get the velocity for wall spell projectiles.
        /// </summary>
        /// <param name="spellId"></param>
        /// <param name="spellStatMod"></param>
        /// <returns></returns>
        private AceVector3 GetWallProjectileVelocity(uint spellId, Database.Models.World.Spell spellStatMod)
        {
            // The Slithering Flames spell does in fact slither slower than other wall spells
            var velocity = (spellId == 1841) ? new Vector3(0, 3f, 0) : new Vector3(0, 4f, 0);
            velocity = Vector3.Transform(velocity, this.Location.Rotation);

            return new AceVector3(velocity.X, velocity.Y, velocity.Z);
        }

        /// <summary>
        /// Rotates a position by the inverse of its rotation.
        /// Useful for getting the local space coordinates of a position.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        /// <returns></returns>
        private static Vector3 RotatePosition(Vector3 position, Quaternion rotation)
        {
            return Vector3.Transform(position, Quaternion.Inverse(rotation));
        }

        /// <summary>
        /// Calculates the velocity of a spell projectile based on distance to the target (assuming it is stationary)
        /// </summary>
        /// <param name="defaultSpeed"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        private float GetStationarySpeed(float defaultSpeed, float distance)
        {
            var speed = (float)((defaultSpeed * .9998363f) - (defaultSpeed * .62034f) / distance +
                                   (defaultSpeed * .44868f) / Math.Pow(distance, 2f) - (defaultSpeed * .25256f)
                                   / Math.Pow(distance, 3f));

            speed = Math.Clamp(speed, 1, 50);

            return speed;
        }

        /// <summary>
        /// Calculates the velocity to launch the projectile from origin to dest
        /// </summary>
        private AceVector3 GetSpellProjectileVelocity(Vector3 origin, WorldObject target, Vector3 dest, float speed, bool useGravity, out float time)
        {
            var targetVelocity = Vector3.Zero;
            if (!useGravity)    // no target tracking for arc spells
                targetVelocity = target.PhysicsObj.CachedVelocity;

            var gravity = useGravity ? PhysicsGlobals.Gravity : 0;
            Trajectory.solve_ballistic_arc_lateral(origin, speed, dest, targetVelocity, gravity, out Vector3 velocity, out time, out var impactPoint);

            return new AceVector3(velocity.X, velocity.Y, velocity.Z);
        }

        private static void GetDamageResistType(uint? eType, out DamageType damageType, out ResistanceType resistanceType)
        {
            switch (eType)
            {
                case null:
                    damageType = DamageType.Undef;
                    resistanceType = ResistanceType.Undef;
                    break;
                case (uint)DamageType.Acid:
                    damageType = DamageType.Acid;
                    resistanceType = ResistanceType.Acid;
                    break;
                case (uint)DamageType.Fire:
                    damageType = DamageType.Fire;
                    resistanceType = ResistanceType.Fire;
                    break;
                case (uint)DamageType.Cold:
                    damageType = DamageType.Cold;
                    resistanceType = ResistanceType.Cold;
                    break;
                case (uint)DamageType.Electric:
                    damageType = DamageType.Electric;
                    resistanceType = ResistanceType.Electric;
                    break;
                case (uint)DamageType.Nether:
                    damageType = DamageType.Nether;
                    resistanceType = ResistanceType.Nether;
                    break;
                case (uint)DamageType.Bludgeon:
                    damageType = DamageType.Bludgeon;
                    resistanceType = ResistanceType.Bludgeon;
                    break;
                case (uint)DamageType.Pierce:
                    damageType = DamageType.Pierce;
                    resistanceType = ResistanceType.Pierce;
                    break;
                case (uint)DamageType.Health:
                    damageType = DamageType.Health;
                    resistanceType = ResistanceType.HealthDrain;
                    break;
                case (uint)DamageType.Stamina:
                    damageType = DamageType.Stamina;
                    resistanceType = ResistanceType.StaminaDrain;
                    break;
                case (uint)DamageType.Mana:
                    damageType = DamageType.Mana;
                    resistanceType = ResistanceType.ManaDrain;
                    break;
                default:
                    damageType = DamageType.Slash;
                    resistanceType = ResistanceType.Slash;
                    break;
            }

            return;
        }

        private enum MagicCritType
        {
            NoCrit,
            PvPCrit,
            PvECrit
        }

        public static double? MagicDamageTarget(Creature source, Creature target, SpellBase spell, Database.Models.World.Spell spellStatMod, out DamageType damageType, ref bool criticalHit, uint lifeMagicDamage = 0)
        {
            var sourceAsPlayer = source as Player;
            var targetAsPlayer = target as Player;

            if (target.Health.Current <= 0)
            {
                // Target already dead
                damageType = DamageType.Undef;
                return -1;
            }

            if (targetAsPlayer != null)
            {
                if (targetAsPlayer.Invincible == true)
                {
                    damageType = DamageType.Undef;
                    return null;
                }
            }

            double damageBonus = 0.0f, minDamageBonus = 0, maxDamageBonus = 0, warSkillBonus = 0.0f, finalDamage = 0.0f;
            MagicCritType magicCritType;

            GetDamageResistType(spellStatMod.EType, out damageType, out ResistanceType resistanceType);

            if (MagicDefenseCheck(source.GetCreatureSkill(spell.School).Current, target.GetCreatureSkill(Skill.MagicDefense).Current))
                return null;

            // critical hit
            var critical = GetWeaponMagicCritFrequencyModifier(source);
            if (Physics.Common.Random.RollDice(0.0f, 1.0f) < critical)
                criticalHit = true;

            if (criticalHit == true)
            {
                if ((sourceAsPlayer != null) && (targetAsPlayer != null)) // PvP
                    magicCritType = MagicCritType.PvPCrit;
                else if (((sourceAsPlayer != null) && (targetAsPlayer == null))) // PvE
                    magicCritType = MagicCritType.PvECrit;
                else
                    magicCritType = MagicCritType.NoCrit;
            }
            else
                magicCritType = MagicCritType.NoCrit;

            // Possible x2 damage bonus for the slayer property
            var slayerBonus = GetWeaponCreatureSlayerModifier(source, target);

            if (spell.School == MagicSchool.LifeMagic)
            {
                if (magicCritType == MagicCritType.PvECrit) // PvE: 50% of the MAX damage added to normal damage
                    damageBonus = lifeMagicDamage * (spellStatMod.DamageRatio ?? 0.0f) * (0.5f + GetWeaponCritMultiplierModifier(source));

                if (magicCritType == MagicCritType.PvPCrit) // PvP: 50% of the MIN damage added to normal damage
                    damageBonus = lifeMagicDamage * (0.5f + GetWeaponCritMultiplierModifier(source));

                finalDamage = (lifeMagicDamage * (spellStatMod.DamageRatio ?? 0.0f)) + damageBonus * slayerBonus;
            }
            else
            {
                if (magicCritType == MagicCritType.PvECrit) // PvE: 50% of the MAX damage added to normal damage roll
                    maxDamageBonus = (((spellStatMod.Variance + spellStatMod.BaseIntensity) ?? 0) * 0.5f) * GetWeaponCritMultiplierModifier(source);
                else if (magicCritType == MagicCritType.PvPCrit) // PvP: 50% of the MIN damage added to normal damage roll
                    minDamageBonus = ((spellStatMod.BaseIntensity ?? 0) * 0.5f) * GetWeaponCritMultiplierModifier(source);

                /* War Magic skill-based damage bonus
                 * http://acpedia.org/wiki/Announcements_-_2002/08_-_Atonement#Letter_to_the_Players
                 */
                if (((source as Player) != null) && (spell.School == MagicSchool.WarMagic))
                {
                    if (source.GetCreatureSkill(spell.School).Current > spell.Power)
                    {
                        // Bonus clamped to a maximum of 50%
                        var percentageBonus = Math.Clamp((source.GetCreatureSkill(spell.School).Current - spell.Power) / 100.0f, 0.0f, 0.5f);
                        warSkillBonus = (spellStatMod.BaseIntensity ?? 0) * percentageBonus;
                    }
                }

                finalDamage = Physics.Common.Random.RollDice((spellStatMod.BaseIntensity ?? 0), (spellStatMod.Variance + spellStatMod.BaseIntensity) ?? 0) + minDamageBonus + maxDamageBonus + warSkillBonus;
            }

            var elementalDmgBonus = GetCasterElementalDamageModifier(source, target, damageType);

            return finalDamage
                * target.GetNaturalResistance(resistanceType, GetWeaponResistanceModifier(source, damageType))
                * slayerBonus
                * elementalDmgBonus;
        }
    }
}
