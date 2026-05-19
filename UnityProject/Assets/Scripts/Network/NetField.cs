// Generic field descriptor for delta-compressed network structs.
// Replaces the C offset+bits hack in netField_t (msg.c).

using System;

namespace ET.Network
{
    /// <summary>
    /// Describes one field in a network-delta struct.
    /// Get/Set operate on raw int bits (floats are reinterpreted via BitConverter).
    /// bits == 0  → field is a float transmitted with FLOAT_INT_BITS encoding.
    /// bits  > 0  → field is an integer transmitted with that many bits.
    /// </summary>
    public sealed class NetField<T>
    {
        public readonly int          Bits;
        public readonly Func<T, int> Get;
        public readonly Action<T, int> Set;

        public NetField(int bits, Func<T, int> getter, Action<T, int> setter)
        {
            Bits = bits;
            Get  = getter;
            Set  = setter;
        }
    }
}
