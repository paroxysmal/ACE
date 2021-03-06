
using ACE.Database.Models.Shard;
using ACE.DatLoader;
using ACE.Entity.Enum;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    partial class Player
    {
        public bool SpellIsKnown(uint spellId)
        {
            return Biota.SpellIsKnown((int)spellId, BiotaDatabaseLock);
        }

        /// <summary>
        /// Will return true if the spell was added, or false if the spell already exists.
        /// </summary>
        public bool AddKnownSpell(uint spellId)
        {
            Biota.GetOrAddKnownSpell((int)spellId, BiotaDatabaseLock, out var spellAdded);

            if (spellAdded)
                ChangesDetected = true;

            return spellAdded;
        }

        public void LearnSpellWithNetworking(uint spellId, bool uiOutput = true)
        {
            var spells = DatManager.PortalDat.SpellTable;

            if (!spells.Spells.ContainsKey(spellId))
            {
                GameMessageSystemChat errorMessage = new GameMessageSystemChat("SpellID not found in Spell Table", ChatMessageType.Broadcast);
                Session.Network.EnqueueSend(errorMessage);
                return;
            }

            if (!AddKnownSpell(spellId))
            {
                GameMessageSystemChat errorMessage = new GameMessageSystemChat("That spell is already known", ChatMessageType.Broadcast);
                Session.Network.EnqueueSend(errorMessage);
                return;
            }

            GameEventMagicUpdateSpell updateSpellEvent = new GameEventMagicUpdateSpell(Session, spellId);
            Session.Network.EnqueueSend(updateSpellEvent);

            //Check to see if we echo output to the client
            if (uiOutput == true)
            {
                // Always seems to be this SkillUpPurple effect
                ApplyVisualEffects(ACE.Entity.Enum.PlayScript.SkillUpPurple);

                string message = $"You learn the {spells.Spells[spellId].Name} spell.\n";
                GameMessageSystemChat learnMessage = new GameMessageSystemChat(message, ChatMessageType.Broadcast);
                Session.Network.EnqueueSend(learnMessage);
            }
        }

        public void HandleActionMagicRemoveSpellId(uint spellId)
        {
            if (!Biota.TryRemoveKnownSpell((int)spellId, out _, BiotaDatabaseLock))
            {
                log.Error("Invalid spellId passed to Player.RemoveSpellFromSpellBook");
                return;
            }

            ChangesDetected = true;

            GameEventMagicRemoveSpellId removeSpellEvent = new GameEventMagicRemoveSpellId(Session, spellId);
            Session.Network.EnqueueSend(removeSpellEvent);
        }
    }
}
