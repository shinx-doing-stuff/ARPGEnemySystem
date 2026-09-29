using System;
using System.IO;
using ARPGEnemySystem.Common.GlobalNPCs;

namespace ARPGEnemySystem.Common.Scaling
{
    // Wire format for a profile's inputs; the receiver rebuilds everything derived.
    // level: 7-bit int | header byte: bit 0 kind, bits 1-2 phase, bits 3-5 rarity |
    // modifier count byte | per modifier: type byte + 7-bit magnitude.
    public static class EnemyProfileCodec
    {
        public static void Write(BinaryWriter w, EnemyProfile p)
        {
            w.Write7BitEncodedInt(p.Level);
            w.Write((byte)((int)p.Kind | (p.Phase << 1) | ((int)p.Rarity << 3)));
            w.Write((byte)p.Modifiers.Length);
            foreach (var m in p.Modifiers)
            {
                w.Write((byte)m.modifierType);
                w.Write7BitEncodedInt(m.magnitude);
            }
        }

        // Returns current when the wire matches it, so repeated movement packets allocate nothing.
        public static EnemyProfile Read(BinaryReader r, EnemyProfile current, ScalingSettings settings)
        {
            int level = r.Read7BitEncodedInt();
            byte header = r.ReadByte();
            var kind = (EnemyKind)(header & 1);
            int phase = (header >> 1) & 3;
            var rarity = (Rarity)((header >> 3) & 7);

            int count = r.ReadByte();
            Span<EnemyModifier> modifiers = stackalloc EnemyModifier[count];
            for (int i = 0; i < count; i++)
                modifiers[i] = new EnemyModifier((ModifierType)r.ReadByte(), r.Read7BitEncodedInt());

            if (current != null && current.SameInputs(kind, level, phase, rarity, modifiers))
                return current;
            return new EnemyProfile(kind, level, phase, rarity, modifiers.ToArray(), settings);
        }
    }
}
