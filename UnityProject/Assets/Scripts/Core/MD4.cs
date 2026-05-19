// Ported from: src/qcommon/md4.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.
//
// MD4 Message-Digest Algorithm
// RSA Data Security, Inc. MD4 Message-Digest Algorithm (RFC 1320)
// Used by ET for file integrity checks and network checksums (Com_BlockChecksum).

using System;

namespace ET.Core
{
    public static class MD4
    {
        // -------------------------------------------------------
        // Round constants
        // -------------------------------------------------------
        private const int S11 = 3,  S12 = 7,  S13 = 11, S14 = 19;
        private const int S21 = 3,  S22 = 5,  S23 = 9,  S24 = 13;
        private const int S31 = 3,  S32 = 9,  S33 = 11, S34 = 15;

        private static readonly byte[] Padding = new byte[64]
        {
            0x80, 0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,
            0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,
            0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,
            0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,
        };

        // -------------------------------------------------------
        // Context
        // -------------------------------------------------------
        private struct Context
        {
            public uint[] State;   // ABCD
            public uint[] Count;   // bits mod 2^64
            public byte[] Buffer;  // 64-byte input block

            public static Context Create()
            {
                var ctx = new Context
                {
                    State  = new uint[4],
                    Count  = new uint[2],
                    Buffer = new byte[64],
                };
                ctx.State[0] = 0x67452301u;
                ctx.State[1] = 0xEFCDAB89u;
                ctx.State[2] = 0x98BADCFEu;
                ctx.State[3] = 0x10325476u;
                return ctx;
            }
        }

        // -------------------------------------------------------
        // Basic MD4 functions
        // -------------------------------------------------------
        private static uint F(uint x, uint y, uint z) => (x & y) | (~x & z);
        private static uint G(uint x, uint y, uint z) => (x & y) | (x & z) | (y & z);
        private static uint H(uint x, uint y, uint z) => x ^ y ^ z;

        private static uint RotateLeft(uint x, int n) => (x << n) | (x >> (32 - n));

        private static uint FF(uint a, uint b, uint c, uint d, uint x, int s)
            => RotateLeft(a + F(b, c, d) + x, s);

        private static uint GG(uint a, uint b, uint c, uint d, uint x, int s)
            => RotateLeft(a + G(b, c, d) + x + 0x5A827999u, s);

        private static uint HH(uint a, uint b, uint c, uint d, uint x, int s)
            => RotateLeft(a + H(b, c, d) + x + 0x6ED9EBA1u, s);

        // -------------------------------------------------------
        // Core transform (one 512-bit block)
        // -------------------------------------------------------
        private static void Transform(uint[] state, byte[] block, int blockOffset)
        {
            uint a = state[0], b = state[1], c = state[2], d = state[3];
            uint[] x = Decode(block, blockOffset, 64);

            // Round 1
            a = FF(a, b, c, d, x[ 0], S11); d = FF(d, a, b, c, x[ 1], S12);
            c = FF(c, d, a, b, x[ 2], S13); b = FF(b, c, d, a, x[ 3], S14);
            a = FF(a, b, c, d, x[ 4], S11); d = FF(d, a, b, c, x[ 5], S12);
            c = FF(c, d, a, b, x[ 6], S13); b = FF(b, c, d, a, x[ 7], S14);
            a = FF(a, b, c, d, x[ 8], S11); d = FF(d, a, b, c, x[ 9], S12);
            c = FF(c, d, a, b, x[10], S13); b = FF(b, c, d, a, x[11], S14);
            a = FF(a, b, c, d, x[12], S11); d = FF(d, a, b, c, x[13], S12);
            c = FF(c, d, a, b, x[14], S13); b = FF(b, c, d, a, x[15], S14);

            // Round 2
            a = GG(a, b, c, d, x[ 0], S21); d = GG(d, a, b, c, x[ 4], S22);
            c = GG(c, d, a, b, x[ 8], S23); b = GG(b, c, d, a, x[12], S24);
            a = GG(a, b, c, d, x[ 1], S21); d = GG(d, a, b, c, x[ 5], S22);
            c = GG(c, d, a, b, x[ 9], S23); b = GG(b, c, d, a, x[13], S24);
            a = GG(a, b, c, d, x[ 2], S21); d = GG(d, a, b, c, x[ 6], S22);
            c = GG(c, d, a, b, x[10], S23); b = GG(b, c, d, a, x[14], S24);
            a = GG(a, b, c, d, x[ 3], S21); d = GG(d, a, b, c, x[ 7], S22);
            c = GG(c, d, a, b, x[11], S23); b = GG(b, c, d, a, x[15], S24);

            // Round 3
            a = HH(a, b, c, d, x[ 0], S31); d = HH(d, a, b, c, x[ 8], S32);
            c = HH(c, d, a, b, x[ 4], S33); b = HH(b, c, d, a, x[12], S34);
            a = HH(a, b, c, d, x[ 2], S31); d = HH(d, a, b, c, x[10], S32);
            c = HH(c, d, a, b, x[ 6], S33); b = HH(b, c, d, a, x[14], S34);
            a = HH(a, b, c, d, x[ 1], S31); d = HH(d, a, b, c, x[ 9], S32);
            c = HH(c, d, a, b, x[ 5], S33); b = HH(b, c, d, a, x[13], S34);
            a = HH(a, b, c, d, x[ 3], S31); d = HH(d, a, b, c, x[11], S32);
            c = HH(c, d, a, b, x[ 7], S33); b = HH(b, c, d, a, x[15], S34);

            state[0] += a; state[1] += b; state[2] += c; state[3] += d;
        }

        // -------------------------------------------------------
        // Update — feed data into context
        // -------------------------------------------------------
        private static void Update(ref Context ctx, byte[] input, int inputOffset, int inputLen)
        {
            uint index = (ctx.Count[0] >> 3) & 0x3F;

            ctx.Count[0] += (uint)(inputLen << 3);
            if (ctx.Count[0] < (uint)(inputLen << 3)) ctx.Count[1]++;
            ctx.Count[1] += (uint)(inputLen >> 29);

            int partLen = 64 - (int)index;
            int i;
            if (inputLen >= partLen)
            {
                Buffer.BlockCopy(input, inputOffset, ctx.Buffer, (int)index, partLen);
                Transform(ctx.State, ctx.Buffer, 0);
                for (i = partLen; i + 63 < inputLen; i += 64)
                    Transform(ctx.State, input, inputOffset + i);
                index = 0;
            }
            else i = 0;

            Buffer.BlockCopy(input, inputOffset + i, ctx.Buffer, (int)index, inputLen - i);
        }

        // -------------------------------------------------------
        // Final — produce 16-byte digest
        // -------------------------------------------------------
        private static byte[] Final(ref Context ctx)
        {
            byte[] bits = Encode(ctx.Count, 8);
            uint index  = (ctx.Count[0] >> 3) & 0x3F;
            int padLen  = (index < 56) ? (int)(56 - index) : (int)(120 - index);
            Update(ref ctx, Padding, 0, padLen);
            Update(ref ctx, bits, 0, 8);

            byte[] digest = Encode(ctx.State, 16);
            ctx = default; // zeroize
            return digest;
        }

        // -------------------------------------------------------
        // Encode: uint[] → byte[]  (little-endian)
        // -------------------------------------------------------
        private static byte[] Encode(uint[] input, int len)
        {
            byte[] output = new byte[len];
            for (int i = 0, j = 0; j < len; i++, j += 4)
            {
                output[j]   = (byte)( input[i]        & 0xFF);
                output[j+1] = (byte)((input[i] >>  8) & 0xFF);
                output[j+2] = (byte)((input[i] >> 16) & 0xFF);
                output[j+3] = (byte)((input[i] >> 24) & 0xFF);
            }
            return output;
        }

        // -------------------------------------------------------
        // Decode: byte[] → uint[]  (little-endian)
        // -------------------------------------------------------
        private static uint[] Decode(byte[] input, int offset, int len)
        {
            uint[] output = new uint[len / 4];
            for (int i = 0, j = 0; j < len; i++, j += 4)
            {
                output[i] = input[offset+j]
                          | ((uint)input[offset+j+1] <<  8)
                          | ((uint)input[offset+j+2] << 16)
                          | ((uint)input[offset+j+3] << 24);
            }
            return output;
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /// <summary>Returns 16-byte MD4 digest of buffer[0..length).</summary>
        public static byte[] Hash(byte[] buffer, int offset, int length)
        {
            var ctx = Context.Create();
            Update(ref ctx, buffer, offset, length);
            return Final(ref ctx);
        }

        public static byte[] Hash(byte[] buffer) => Hash(buffer, 0, buffer.Length);

        /// <summary>
        /// Com_BlockChecksum — XORs the four 32-bit words of the MD4 digest.
        /// Used by ET to verify map/PK3 integrity.
        /// </summary>
        public static uint BlockChecksum(byte[] buffer, int length)
        {
            byte[] digest = Hash(buffer, 0, length);
            uint val = BitConverter.ToUInt32(digest,  0)
                     ^ BitConverter.ToUInt32(digest,  4)
                     ^ BitConverter.ToUInt32(digest,  8)
                     ^ BitConverter.ToUInt32(digest, 12);
            return val;
        }

        /// <summary>
        /// Com_BlockChecksumKey — same as BlockChecksum but salted with a 4-byte key.
        /// Used by ET for challenge-response verification.
        /// </summary>
        public static uint BlockChecksumKey(byte[] buffer, int length, int key)
        {
            var ctx = Context.Create();
            byte[] keyBytes = BitConverter.GetBytes(key);
            Update(ref ctx, keyBytes, 0, 4);
            Update(ref ctx, buffer,   0, length);
            byte[] digest = Final(ref ctx);

            return BitConverter.ToUInt32(digest,  0)
                 ^ BitConverter.ToUInt32(digest,  4)
                 ^ BitConverter.ToUInt32(digest,  8)
                 ^ BitConverter.ToUInt32(digest, 12);
        }
    }
}
