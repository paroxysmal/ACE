using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Entity.Enum;

namespace ACE.Server.Entity
{
    [Flags]
    public enum BodyPart
    {
        Head        = 0x1,
        Chest       = 0x2,
        Abdomen     = 0x4,
        UpperArm    = 0x8,
        LowerArm    = 0x10,
        Hand        = 0x20,
        UpperLeg    = 0x40,
        LowerLeg    = 0x80,
        Foot        = 0x100
    }

    public class BodyParts
    {
        public static BodyPart Upper = BodyPart.Head | BodyPart.Chest | BodyPart.UpperArm;
        public static BodyPart Mid = BodyPart.Chest | BodyPart.Abdomen | BodyPart.UpperArm | BodyPart.LowerArm | BodyPart.Hand | BodyPart.UpperLeg;
        public static BodyPart Lower = BodyPart.Foot | BodyPart.LowerLeg;

        public static Dictionary<BodyPart, int> Indices;

        static BodyParts()
        {
            Indices = new Dictionary<BodyPart, int>()
            {
                { BodyPart.Head, 0 },
                { BodyPart.Chest, 1 },
                { BodyPart.Abdomen, 2 },
                { BodyPart.UpperArm, 3 },
                { BodyPart.LowerArm, 4 },
                { BodyPart.Hand, 5 },
                { BodyPart.UpperLeg, 6 },
                { BodyPart.LowerLeg, 7 },
                { BodyPart.Foot, 8 }
            };
        }

        public static BodyPart GetBodyPart(BodyPart bodyParts)
        {
            // get individual parts in bodyParts
            var parts = Enum.GetValues(typeof(BodyPart)).Cast<BodyPart>().Where(p => bodyParts.HasFlag(p));

            // return a random part within list
            return parts.ToList()[Physics.Common.Random.RollDice(0, parts.Count() - 1)];
        }

        public static BodyPart GetBodyPart(AttackHeight attackHeight)
        {
            switch (attackHeight)
            {
                case AttackHeight.High: return GetBodyPart(Upper);
                case AttackHeight.Medium: return GetBodyPart(Mid);
                case AttackHeight.Low:
                default: return GetBodyPart(Lower);
            }
        }
    }
}
