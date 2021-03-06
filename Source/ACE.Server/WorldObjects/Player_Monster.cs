using System;
using System.Linq;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// Handles player->monster visibility checks
    /// </summary>
    partial class Player
    {
        /// <summary>
        /// Flag indicates if player is attackable
        /// </summary>
        public bool IsAttackable { get => GetProperty(PropertyBool.Attackable) ?? false == true; }

        /// <summary>
        /// Wakes up any monsters within the applicable range
        /// </summary>
        public void CheckMonsters()
        {
            if (!IsAttackable) return;

            if (CurrentLandblock?.Id.MapScope == MapScope.Outdoors)
                GetMonstersInRange();
            else
                GetMonstersInPVS();
        }

        /// <summary>
        /// Sends alerts to monsters within 2D distance for outdoor areas
        /// </summary>
        private void GetMonstersInRange(float range = RadiusAwareness)
        {
            var distSq = range * range;

            var landblocks = CurrentLandblock?.GetLandblocksInRange(Location, range);

            foreach (var landblock in landblocks)
            {
                var monsters = landblock.worldObjects.Values.OfType<Creature>().ToList();
                foreach (var monster in monsters)
                {
                    if (this == monster || monster is Player) continue;

                    if (Location.SquaredDistanceTo(monster.Location) < distSq)
                        AlertMonster(monster);
                }
            }
        }

        /// <summary>
        /// Sends alerts to monsters within PVS range for indoor areas
        /// </summary>
        private void GetMonstersInPVS(float range = RadiusAwareness)
        {
            var distSq = range * range;

            var visibleObjs = PhysicsObj.ObjMaint.VisibleObjectTable.Values;

            foreach (var obj in visibleObjs)
            {
                if (PhysicsObj == obj) continue;

                var monster = obj.WeenieObj.WorldObject as Creature;

                if (monster == null || monster is Player) continue;

                if (Location.SquaredDistanceTo(monster.Location) < distSq)
                    AlertMonster(monster);
            }
        }

        /// <summary>
        /// Wakes up a monster if it can be alerted
        /// </summary>
        private bool AlertMonster(Creature monster)
        {
            var attackable = monster.GetProperty(PropertyBool.Attackable) ?? false;
            var tolerance = (Tolerance)(monster.GetProperty(PropertyInt.Tolerance) ?? 0);

            if (attackable && monster.MonsterState == State.Idle && tolerance == Tolerance.None)
            {
                //Console.WriteLine("Waking up " + monster.Name);

                monster.AttackTarget = this;
                monster.WakeUp();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Called when this player attacks a monster
        /// </summary>
        public void OnAttackMonster(Creature monster)
        {
            var attackable = monster.GetProperty(PropertyBool.Attackable) ?? false;
            var tolerance = (Tolerance)(monster.GetProperty(PropertyInt.Tolerance) ?? 0);
            var hasTolerance = monster.GetProperty(PropertyInt.Tolerance).HasValue;

            /*Console.WriteLine("OnAttackMonster(" + monster.Name + ")");
            Console.WriteLine("Attackable: " + attackable);
            Console.WriteLine("Tolerance: " + tolerance);
            Console.WriteLine("HasTolerance: " + hasTolerance);*/

            if (monster.MonsterState == State.Idle && !tolerance.HasFlag(Tolerance.NoAttack))
            {
                monster.AttackTarget = this;
                monster.WakeUp();
            }
        }
    }
}
