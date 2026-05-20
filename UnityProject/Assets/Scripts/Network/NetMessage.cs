// Ported from: src/qcommon/msg.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Runtime.InteropServices;
using System.Text;
using ET.Core;
using UnityEngine;

namespace ET.Network
{
    // -------------------------------------------------------
    // Constants
    // -------------------------------------------------------
    public static class NetConst
    {
        public const int MAX_MSGLEN        = 16384;
        public const int MAX_STRING_CHARS  = 1024;
        public const int BIG_INFO_STRING   = 8192;
        public const int GENTITYNUM_BITS   = 11;
        public const int MAX_GENTITIES     = 1 << GENTITYNUM_BITS;  // 2048
        public const int ANIM_BITS         = 10;
        public const int FLOAT_INT_BITS    = 13;
        public const int FLOAT_INT_BIAS    = 1 << (FLOAT_INT_BITS - 1);
        public const int ANGLE2SHORT_SCALE = 65536;
        public const float SHORT2ANGLE_SCALE = 360.0f / 65536f;
    }

    // -------------------------------------------------------
    // NetMessage  (mirrors msg_t + MSG_* functions)
    //
    // Two modes:
    //   OOB  (oob=true)  — raw byte access, byte-aligned
    //   Bitstream (oob=false) — Huffman-compressed bit-level I/O
    // -------------------------------------------------------
    public class NetMessage
    {
        private static HuffmanMsg _msgHuff;
        private static bool       _msgInit;

        public byte[] Data     { get; private set; }
        public int    MaxSize  { get; private set; }
        public int    CurSize  { get; set; }
        public int    ReadCount{ get; set; }
        public int    Bit      { get; set; }   // bit position for bitstream I/O
        public bool   Oob      { get; set; }   // out-of-band (raw byte) mode
        public bool   Overflowed{ get; set; }
        public int    UncompSize{ get; set; }  // debug: uncompressed bit count

        // -------------------------------------------------------
        // Construction / Init
        // -------------------------------------------------------
        public static void EnsureHuffmanInit()
        {
            if (_msgInit) return;
            _msgInit = true;
            _msgHuff = new HuffmanMsg();
            for (int i = 0; i < 256; i++)
                for (int j = 0; j < MsgHuffData[i]; j++)
                    _msgHuff.AddRef((byte)i);
        }

        public NetMessage(byte[] data, int maxSize, bool oob = false)
        {
            EnsureHuffmanInit();
            Data    = data;
            MaxSize = maxSize;
            Oob     = oob;
        }

        public static NetMessage CreateOOB(byte[] data, int maxSize)
        {
            var m = new NetMessage(data, maxSize) { Oob = true };
            return m;
        }

        public void Clear()
        {
            CurSize    = 0;
            Overflowed = false;
            Bit        = 0;
        }

        public void SetBitstream() => Oob = false;

        public void SetUncompressed()
        {
            Bit = (Bit + 7) & ~7;
            Oob = true;
        }

        public void BeginReading()     { ReadCount = 0; Bit = 0; Oob = false; }
        public void BeginReadingOOB()  { ReadCount = 0; Bit = 0; Oob = true;  }
        public void BeginReadingUncompressed() { Bit = (Bit + 7) & ~7; Oob = true; }

        public void CopyFrom(NetMessage src, byte[] destData)
        {
            if (destData.Length < src.CurSize)
                throw new Exception("NetMessage.CopyFrom: buffer too small");
            Data    = destData;
            MaxSize = destData.Length;
            CurSize = src.CurSize;
            Oob     = src.Oob;
            Bit     = src.Bit;
            Buffer.BlockCopy(src.Data, 0, destData, 0, src.CurSize);
        }

        // -------------------------------------------------------
        // Core bit I/O
        // -------------------------------------------------------
        public void WriteBits(int value, int bits)
        {
            UncompSize += Math.Abs(bits);

            if (MaxSize - CurSize < 32) { Overflowed = true; return; }
            if (bits == 0 || bits < -31 || bits > 32)
                throw new Exception($"WriteBits: bad bits {bits}");

            if (bits < 0) bits = -bits;

            if (Oob)
            {
                if      (bits ==  8) { Data[CurSize++] = (byte)value; Bit +=  8; }
                else if (bits == 16) { var v = ETShared.LittleShort((short)value); BitConverter.GetBytes(v).CopyTo(Data, CurSize); CurSize += 2; Bit += 16; }
                else if (bits == 32) { var v = value; BitConverter.GetBytes(v).CopyTo(Data, CurSize); CurSize += 4; Bit += 32; }
                else throw new Exception($"WriteBits OOB: unsupported {bits} bits");
            }
            else
            {
                value &= (int)(0xFFFFFFFFu >> (32 - bits));
                int rem = bits & 7;
                int bitPos = Bit;   // local copy — properties can't be ref-passed in C#
                if (rem != 0)
                {
                    for (int i = 0; i < rem; i++)
                    {
                        Huffman.PutBitStatic(value & 1, Data, ref bitPos);
                        value >>= 1;
                    }
                    bits -= rem;
                }
                for (int i = 0; i < bits; i += 8)
                {
                    _msgHuff.OffsetTransmit(value & 0xFF, Data, ref bitPos);
                    value >>= 8;
                }
                Bit = bitPos;
                CurSize = (Bit >> 3) + 1;
            }
        }

        public int ReadBits(int bits)
        {
            bool sgn = bits < 0;
            if (sgn) bits = -bits;

            int value;
            if (Oob)
            {
                if      (bits ==  8) { value = Data[ReadCount++]; Bit +=  8; }
                else if (bits == 16) { value = ETShared.LittleShort(BitConverter.ToInt16(Data, ReadCount)); ReadCount += 2; Bit += 16; }
                else if (bits == 32) { value = BitConverter.ToInt32(Data, ReadCount); ReadCount += 4; Bit += 32; }
                else throw new Exception($"ReadBits OOB: unsupported {bits} bits");
            }
            else
            {
                value = 0;
                int bitPos = Bit;   // local copy for ref-passing
                int nbits = bits & 7;
                for (int i = 0; i < nbits; i++)
                    value |= Huffman.GetBitStatic(Data, ref bitPos) << i;

                for (int i = 0; i < (bits - nbits); i += 8)
                {
                    _msgHuff.OffsetReceive(Data, ref bitPos, out int ch);
                    value |= (ch << (i + nbits));
                }
                Bit = bitPos;
                ReadCount = (Bit >> 3) + 1;
            }

            if (sgn && (value & (1 << (bits - 1))) != 0)
                value |= -1 ^ ((1 << bits) - 1);

            return value;
        }


        // -------------------------------------------------------
        // Typed write helpers
        // -------------------------------------------------------
        public void WriteChar  (int c)   => WriteBits(c, 8);
        public void WriteByte  (int c)   => WriteBits(c, 8);
        public void WriteShort (int c)   => WriteBits(c, 16);
        public void WriteLong  (int c)   => WriteBits(c, 32);

        public void WriteFloat(float f)
        {
            int i = BitConverter.SingleToInt32Bits(f);
            WriteBits(i, 32);
        }

        public void WriteString(string s)
        {
            if (string.IsNullOrEmpty(s)) { WriteByte(0); return; }
            if (s.Length >= NetConst.MAX_STRING_CHARS) { Debug.LogWarning("WriteString overflow"); WriteByte(0); return; }
            var bytes = Encoding.ASCII.GetBytes(s);
            WriteData(bytes, bytes.Length);
            WriteByte(0);
        }

        public void WriteBigString(string s)
        {
            if (string.IsNullOrEmpty(s)) { WriteByte(0); return; }
            if (s.Length >= NetConst.BIG_INFO_STRING) { Debug.LogWarning("WriteBigString overflow"); WriteByte(0); return; }
            var bytes = Encoding.ASCII.GetBytes(s);
            WriteData(bytes, bytes.Length);
            WriteByte(0);
        }

        public void WriteData(byte[] data, int length)
        {
            for (int i = 0; i < length; i++) WriteByte(data[i]);
        }

        // Angle encoded as 1 byte (256 steps per revolution)
        public void WriteAngle  (float f) => WriteByte((int)(f * 256f / 360f) & 255);
        // Angle encoded as 2 bytes (65536 steps per revolution)
        public void WriteAngle16(float f) => WriteShort((int)(f * NetConst.ANGLE2SHORT_SCALE / 360f) & 0xFFFF);

        // -------------------------------------------------------
        // Typed read helpers
        // -------------------------------------------------------
        public int   ReadChar()    => ReadCount > CurSize ? -1 : (sbyte)ReadBits(8);
        public int   ReadByte()    => ReadCount > CurSize ? -1 : (byte) ReadBits(8);
        public int   ReadShort()   => ReadCount > CurSize ? -1 : (short)ReadBits(16);
        public int   ReadLong()    => ReadCount > CurSize ? -1 : ReadBits(32);

        public float ReadFloat()
        {
            int i = ReadBits(32);
            return ReadCount > CurSize ? -1f : BitConverter.Int32BitsToSingle(i);
        }

        public string ReadString()
        {
            var sb = new StringBuilder();
            while (true)
            {
                int c = ReadByte();
                if (c == -1 || c == 0) break;
                if (c == '%') c = '.';
                if (c > 127) c = '.';
                sb.Append((char)c);
                if (sb.Length >= NetConst.MAX_STRING_CHARS - 1) break;
            }
            return sb.ToString();
        }

        public string ReadBigString()
        {
            var sb = new StringBuilder();
            while (true)
            {
                int c = ReadByte();
                if (c == -1 || c == 0) break;
                if (c == '%') c = '.';
                sb.Append((char)c);
                if (sb.Length >= NetConst.BIG_INFO_STRING - 1) break;
            }
            return sb.ToString();
        }

        public string ReadStringLine()
        {
            var sb = new StringBuilder();
            while (true)
            {
                int c = ReadByte();
                if (c == -1 || c == 0 || c == '\n') break;
                if (c == '%') c = '.';
                sb.Append((char)c);
                if (sb.Length >= NetConst.MAX_STRING_CHARS - 1) break;
            }
            return sb.ToString();
        }

        public void ReadData(byte[] buf, int length)
        {
            for (int i = 0; i < length; i++) buf[i] = (byte)ReadByte();
        }

        public float ReadAngle16() => ReadShort() * NetConst.SHORT2ANGLE_SCALE;

        // -------------------------------------------------------
        // Delta I/O (1-bit changed flag + value)
        // -------------------------------------------------------
        public void WriteDelta(int oldV, int newV, int bits)
        {
            if (oldV == newV) { WriteBits(0, 1); return; }
            WriteBits(1, 1);
            WriteBits(newV, bits);
        }

        public int ReadDelta(int oldV, int bits)
            => ReadBits(1) != 0 ? ReadBits(bits) : oldV;

        public void WriteDeltaFloat(float oldV, float newV)
        {
            if (oldV == newV) { WriteBits(0, 1); return; }
            WriteBits(1, 1);
            WriteBits(BitConverter.SingleToInt32Bits(newV), 32);
        }

        public float ReadDeltaFloat(float oldV)
        {
            if (ReadBits(1) == 0) return oldV;
            return BitConverter.Int32BitsToSingle(ReadBits(32));
        }

        // -------------------------------------------------------
        // Keyed delta (XOR with key to obscure data)
        // -------------------------------------------------------
        private static readonly int[] KBitMask = new int[32]
        {
            0x00000001, 0x00000003, 0x00000007, 0x0000000F,
            0x0000001F, 0x0000003F, 0x0000007F, 0x000000FF,
            0x000001FF, 0x000003FF, 0x000007FF, 0x00000FFF,
            0x00001FFF, 0x00003FFF, 0x00007FFF, 0x0000FFFF,
            0x0001FFFF, 0x0003FFFF, 0x0007FFFF, 0x000FFFFF,
            0x001FFFFF, 0x003FFFFF, 0x007FFFFF, 0x00FFFFFF,
            0x01FFFFFF, 0x03FFFFFF, 0x07FFFFFF, 0x0FFFFFFF,
            0x1FFFFFFF, 0x3FFFFFFF, 0x7FFFFFFF, unchecked((int)0xFFFFFFFF),
        };

        public void WriteDeltaKey(int key, int oldV, int newV, int bits)
        {
            if (oldV == newV) { WriteBits(0, 1); return; }
            WriteBits(1, 1);
            WriteBits(newV ^ key, bits);
        }

        public int ReadDeltaKey(int key, int oldV, int bits)
            => ReadBits(1) != 0 ? ReadBits(bits) ^ (key & KBitMask[bits - 1]) : oldV;

        public void WriteDeltaKeyFloat(int key, float oldV, float newV)
        {
            if (oldV == newV) { WriteBits(0, 1); return; }
            WriteBits(1, 1);
            WriteBits(BitConverter.SingleToInt32Bits(newV) ^ key, 32);
        }

        public float ReadDeltaKeyFloat(int key, float oldV)
        {
            if (ReadBits(1) == 0) return oldV;
            return BitConverter.Int32BitsToSingle(ReadBits(32) ^ key);
        }

        // -------------------------------------------------------
        // UserCmd delta
        // -------------------------------------------------------
        public void WriteDeltaUserCmd(UserCmd from, UserCmd to)
        {
            int dt = to.ServerTime - from.ServerTime;
            if (dt < 256) { WriteBits(1, 1); WriteBits(dt, 8); }
            else          { WriteBits(0, 1); WriteBits(to.ServerTime, 32); }

            WriteDelta(from.Angles[0], to.Angles[0], 16);
            WriteDelta(from.Angles[1], to.Angles[1], 16);
            WriteDelta(from.Angles[2], to.Angles[2], 16);
            WriteDelta(from.ForwardMove, to.ForwardMove, 8);
            WriteDelta(from.RightMove,   to.RightMove,   8);
            WriteDelta(from.UpMove,      to.UpMove,      8);
            WriteDelta(from.Buttons,     to.Buttons,     8);
            WriteDelta(from.WButtons,    to.WButtons,    8);
            WriteDelta(from.Weapon,      to.Weapon,      8);
            WriteDelta(from.Flags,       to.Flags,       8);
            WriteDelta(from.DoubleTap,   to.DoubleTap,   3);
            WriteDelta(from.IdentClient, to.IdentClient, 8);
        }

        public void ReadDeltaUserCmd(UserCmd from, UserCmd to)
        {
            to.ServerTime = ReadBits(1) != 0
                ? from.ServerTime + ReadBits(8)
                : ReadBits(32);

            to.Angles[0]   = ReadDelta(from.Angles[0],   16);
            to.Angles[1]   = ReadDelta(from.Angles[1],   16);
            to.Angles[2]   = ReadDelta(from.Angles[2],   16);
            to.ForwardMove = ReadDelta(from.ForwardMove,  8);
            to.RightMove   = ReadDelta(from.RightMove,    8);
            to.UpMove      = ReadDelta(from.UpMove,       8);
            to.Buttons     = ReadDelta(from.Buttons,      8);
            to.WButtons    = ReadDelta(from.WButtons,     8);
            to.Weapon      = ReadDelta(from.Weapon,       8);
            to.Flags       = ReadDelta(from.Flags,        8);
            to.DoubleTap   = ReadDelta(from.DoubleTap,    3) & 0x7;
            to.IdentClient = ReadDelta(from.IdentClient,  8);
        }

        public void WriteDeltaUserCmdKey(int key, UserCmd from, UserCmd to)
        {
            int dt = to.ServerTime - from.ServerTime;
            if (dt < 256) { WriteBits(1, 1); WriteBits(dt, 8); }
            else          { WriteBits(0, 1); WriteBits(to.ServerTime, 32); }

            if (from.Equals(to)) { WriteBits(0, 1); return; }

            key ^= to.ServerTime;
            WriteBits(1, 1);
            WriteDeltaKey(key, from.Angles[0], to.Angles[0], 16);
            WriteDeltaKey(key, from.Angles[1], to.Angles[1], 16);
            WriteDeltaKey(key, from.Angles[2], to.Angles[2], 16);
            WriteDeltaKey(key, from.ForwardMove, to.ForwardMove, 8);
            WriteDeltaKey(key, from.RightMove,   to.RightMove,   8);
            WriteDeltaKey(key, from.UpMove,      to.UpMove,      8);
            WriteDeltaKey(key, from.Buttons,     to.Buttons,     8);
            WriteDeltaKey(key, from.WButtons,    to.WButtons,    8);
            WriteDeltaKey(key, from.Weapon,      to.Weapon,      8);
            WriteDeltaKey(key, from.Flags,       to.Flags,       8);
            WriteDeltaKey(key, from.DoubleTap,   to.DoubleTap,   3);
            WriteDeltaKey(key, from.IdentClient, to.IdentClient, 8);
        }

        public void ReadDeltaUserCmdKey(int key, UserCmd from, UserCmd to)
        {
            to.ServerTime = ReadBits(1) != 0
                ? from.ServerTime + ReadBits(8)
                : ReadBits(32);

            if (ReadBits(1) == 0)
            {
                to.CopyFrom(from); return;
            }

            key ^= to.ServerTime;
            to.Angles[0]   = ReadDeltaKey(key, from.Angles[0],   16);
            to.Angles[1]   = ReadDeltaKey(key, from.Angles[1],   16);
            to.Angles[2]   = ReadDeltaKey(key, from.Angles[2],   16);
            to.ForwardMove = ReadDeltaKey(key, from.ForwardMove,  8);
            to.RightMove   = ReadDeltaKey(key, from.RightMove,    8);
            to.UpMove      = ReadDeltaKey(key, from.UpMove,       8);
            to.Buttons     = ReadDeltaKey(key, from.Buttons,      8);
            to.WButtons    = ReadDeltaKey(key, from.WButtons,     8);
            to.Weapon      = ReadDeltaKey(key, from.Weapon,       8);
            to.Flags       = ReadDeltaKey(key, from.Flags,        8);
            to.DoubleTap   = ReadDeltaKey(key, from.DoubleTap,    3) & 0x7;
            to.IdentClient = ReadDeltaKey(key, from.IdentClient,  8);
        }

        // -------------------------------------------------------
        // EntityState delta
        // -------------------------------------------------------
        public void WriteDeltaEntity(EntityState from, EntityState to, bool force)
        {
            if (to == null)
            {
                if (from == null) return;
                WriteBits(from.Number, NetConst.GENTITYNUM_BITS);
                WriteBits(1, 1); // removed
                return;
            }

            var fields = EntityState.NetFields;
            int lc = 0;
            for (int i = 0; i < fields.Length; i++)
                if (fields[i].Get(from) != fields[i].Get(to)) lc = i + 1;

            if (lc == 0 && !force)
                return;

            WriteBits(to.Number, NetConst.GENTITYNUM_BITS);
            WriteBits(0, 1); // not removed
            WriteBits(lc == 0 ? 0 : 1, 1); // has delta

            if (lc == 0) return;

            WriteByte(lc);
            for (int i = 0; i < lc; i++)
            {
                var f = fields[i];
                int fv = f.Get(from), tv = f.Get(to);
                if (fv == tv) { WriteBits(0, 1); continue; }
                WriteBits(1, 1);

                if (f.Bits == 0) WriteCompressedFloat(tv);
                else
                {
                    if (tv == 0) WriteBits(0, 1);
                    else { WriteBits(1, 1); WriteBits(tv, f.Bits); }
                }
            }
        }

        public void ReadDeltaEntity(EntityState from, EntityState to, int number)
        {
            if (ReadBits(1) == 1) // removed
            {
                to.Clear(); to.Number = NetConst.MAX_GENTITIES - 1; return;
            }
            if (ReadBits(1) == 0) // no delta
            {
                to.CopyFrom(from); to.Number = number; return;
            }

            var fields = EntityState.NetFields;
            int lc = ReadByte();
            if (lc > fields.Length) lc = fields.Length;

            to.Number = number;
            for (int i = 0; i < lc; i++)
            {
                var f = fields[i];
                if (ReadBits(1) == 0) { f.Set(to, f.Get(from)); continue; }

                if (f.Bits == 0) f.Set(to, ReadCompressedFloat());
                else
                {
                    if (ReadBits(1) == 0) f.Set(to, 0);
                    else f.Set(to, ReadBits(f.Bits));
                }
            }
            for (int i = lc; i < fields.Length; i++)
                fields[i].Set(to, fields[i].Get(from));
        }

        // -------------------------------------------------------
        // PlayerState delta
        // -------------------------------------------------------
        public void WriteDeltaPlayerState(PlayerState from, PlayerState to)
        {
            from ??= new PlayerState();
            var fields = PlayerState.NetFields;
            int lc = 0;
            for (int i = 0; i < fields.Length; i++)
                if (fields[i].Get(from) != fields[i].Get(to)) lc = i + 1;

            WriteByte(lc);
            for (int i = 0; i < lc; i++)
            {
                var f = fields[i];
                int fv = f.Get(from), tv = f.Get(to);
                if (fv == tv) { WriteBits(0, 1); continue; }
                WriteBits(1, 1);

                if (f.Bits == 0) WriteCompressedFloatPS(tv);
                else WriteBits(tv, f.Bits);
            }

            // arrays: stats, persistant, holdable, powerups
            WriteArrayDelta16(from.Stats,     to.Stats,     16);
            WriteArrayDelta16(from.Persistant,to.Persistant,16);
            WriteArrayDelta16(from.Holdable,  to.Holdable,  16);

            // powerups uses longs
            int pwbits = 0;
            for (int i = 0; i < 16; i++)
                if (to.Powerups[i] != from.Powerups[i]) pwbits |= 1 << i;
            if (pwbits != 0)
            {
                WriteBits(1, 1);
                WriteShort(pwbits);
                for (int i = 0; i < 16; i++)
                    if ((pwbits & (1 << i)) != 0) WriteLong(to.Powerups[i]);
            }
            else WriteBits(0, 1);

            // check general arrays group bit
            // (already written inline above as separate bits in the original)

            // ammo (4 groups of 16 weapons)
            WriteAmmoGroup(from.Ammo,     to.Ammo,     false);
            WriteAmmoGroup(from.AmmoClip, to.AmmoClip, true);
        }

        public void ReadDeltaPlayerState(PlayerState from, PlayerState to)
        {
            from ??= new PlayerState();
            to.CopyFrom(from);

            var fields = PlayerState.NetFields;
            int lc = ReadByte();
            for (int i = 0; i < lc; i++)
            {
                var f = fields[i];
                if (ReadBits(1) == 0) { f.Set(to, f.Get(from)); continue; }

                if (f.Bits == 0) f.Set(to, ReadCompressedFloatPS());
                else f.Set(to, ReadBits(f.Bits));
            }

            // arrays
            if (ReadBits(1) != 0)
            {
                if (ReadBits(1) != 0) { int bits = ReadShort(); for (int i=0;i<16;i++) if((bits&(1<<i))!=0) to.Stats[i]=(short)ReadShort(); }
                if (ReadBits(1) != 0) { int bits = ReadShort(); for (int i=0;i<16;i++) if((bits&(1<<i))!=0) to.Persistant[i]=(short)ReadShort(); }
                if (ReadBits(1) != 0) { int bits = ReadShort(); for (int i=0;i<16;i++) if((bits&(1<<i))!=0) to.Holdable[i]=(short)ReadShort(); }
                if (ReadBits(1) != 0) { int bits = ReadShort(); for (int i=0;i<16;i++) if((bits&(1<<i))!=0) to.Powerups[i]=ReadLong(); }
            }

            // ammo
            if (ReadBits(1) != 0)
                for (int j = 0; j < 4; j++)
                    if (ReadBits(1) != 0) { int bits = ReadShort(); for (int i=0;i<16;i++) if((bits&(1<<i))!=0) to.Ammo[i+j*16]=(short)ReadShort(); }
            for (int j = 0; j < 4; j++)
                if (ReadBits(1) != 0) { int bits = ReadShort(); for (int i=0;i<16;i++) if((bits&(1<<i))!=0) to.AmmoClip[i+j*16]=(short)ReadShort(); }
        }

        // -------------------------------------------------------
        // Float compression helpers (matches FLOAT_INT_BITS encoding)
        // -------------------------------------------------------
        private void WriteCompressedFloat(int rawBits)
        {
            float f = BitConverter.Int32BitsToSingle(rawBits);
            if (f == 0f) { WriteBits(0, 1); return; }
            WriteBits(1, 1);
            int trunc = (int)f;
            if ((float)trunc == f && trunc + NetConst.FLOAT_INT_BIAS >= 0 &&
                trunc + NetConst.FLOAT_INT_BIAS < (1 << NetConst.FLOAT_INT_BITS))
            { WriteBits(0, 1); WriteBits(trunc + NetConst.FLOAT_INT_BIAS, NetConst.FLOAT_INT_BITS); }
            else
            { WriteBits(1, 1); WriteBits(rawBits, 32); }
        }

        private int ReadCompressedFloat()
        {
            if (ReadBits(1) == 0) return 0;
            if (ReadBits(1) == 0)
            {
                int trunc = ReadBits(NetConst.FLOAT_INT_BITS) - NetConst.FLOAT_INT_BIAS;
                return BitConverter.SingleToInt32Bits((float)trunc);
            }
            return ReadBits(32);
        }

        // PlayerState uses slightly different encoding (no 0-special-case)
        private void WriteCompressedFloatPS(int rawBits)
        {
            float f = BitConverter.Int32BitsToSingle(rawBits);
            int trunc = (int)f;
            if ((float)trunc == f && trunc + NetConst.FLOAT_INT_BIAS >= 0 &&
                trunc + NetConst.FLOAT_INT_BIAS < (1 << NetConst.FLOAT_INT_BITS))
            { WriteBits(0, 1); WriteBits(trunc + NetConst.FLOAT_INT_BIAS, NetConst.FLOAT_INT_BITS); }
            else
            { WriteBits(1, 1); WriteBits(rawBits, 32); }
        }

        private int ReadCompressedFloatPS()
        {
            if (ReadBits(1) == 0)
                return BitConverter.SingleToInt32Bits((float)(ReadBits(NetConst.FLOAT_INT_BITS) - NetConst.FLOAT_INT_BIAS));
            return ReadBits(32);
        }

        // Write 16-element short array with bitmask
        private void WriteArrayDelta16(short[] from, short[] to, int count)
        {
            int bits = 0;
            for (int i = 0; i < count; i++)
                if (to[i] != from[i]) bits |= 1 << i;
            WriteBits(bits != 0 ? 1 : 0, 1);
            if (bits == 0) return;
            WriteShort(bits);
            for (int i = 0; i < count; i++)
                if ((bits & (1 << i)) != 0) WriteShort(to[i]);
        }

        private void WriteAmmoGroup(short[] from, short[] to, bool isClip)
        {
            for (int j = 0; j < 4; j++)
            {
                int bits = 0;
                for (int i = 0; i < 16; i++)
                    if (to[i + j*16] != from[i + j*16]) bits |= 1 << i;
                WriteBits(bits != 0 ? 1 : 0, 1);
                if (bits == 0) continue;
                WriteShort(bits);
                for (int i = 0; i < 16; i++)
                    if ((bits & (1 << i)) != 0) WriteShort(to[i + j*16]);
            }
        }

        // -------------------------------------------------------
        // Out-of-band helpers
        // -------------------------------------------------------
        public static byte[] BuildOOBPrint(string message)
        {
            var bytes = new byte[4 + message.Length];
            bytes[0] = bytes[1] = bytes[2] = bytes[3] = 0xFF;
            Encoding.ASCII.GetBytes(message, 0, message.Length, bytes, 4);
            return bytes;
        }

        // -------------------------------------------------------
        // Pre-seeded Huffman frequency table (msg_hData from msg.c)
        // -------------------------------------------------------
        private static readonly int[] MsgHuffData = new int[256]
        {
            250315, 41193,  6292,   7106,   3730,   3750,   6110,  23283,
             33317,  6950,  7838,   9714,   9257,  17259,   3949,   1778,
              8288,  1604,  1590,   1663,   1100,   1213,   1238,   1134,
              1749,  1059,  1246,   1149,   1273,   4486,   2805,   3472,
             21819,  1159,  1670,   1066,   1043,   1012,   1053,   1070,
              1726,   888,  1180,    850,    960,    780,   1752,   3296,
             10630,  4514,  5881,   2685,   4650,   3837,   2093,   1867,
              2584,  1949,  1972,    940,   1134,   1788,   1670,   1206,
              5719,  6128,  7222,   6654,   3710,   3795,   1492,   1524,
              2215,  1140,  1355,    971,   2180,   1248,   1328,   1195,
              1770,  1078,  1264,   1266,   1168,    965,   1155,   1186,
              1347,  1228,  1529,   1600,   2617,   2048,   2546,   3275,
              2410,  3585,  2504,   2800,   2675,   6146,   3663,   2840,
             14253,  3164,  2221,   1687,   3208,   2739,   3512,   4796,
              4091,  3515,  5288,   4016,   7937,   6031,   5360,   3924,
              4892,  3743,  4566,   4807,   5852,   6400,   6225,   8291,
             23243,  7838,  7073,   8935,   5437,   4483,   3641,   5256,
              5312,  5328,  5370,   3492,   2458,   1694,   1821,   2121,
              1916,  1149,  1516,   1367,   1236,   1029,   1258,   1104,
              1245,  1006,  1149,   1025,   1241,    952,   1287,    997,
              1713,  1009,  1187,    879,   1099,    929,   1078,    951,
              1656,   930,  1153,   1030,   1262,   1062,   1214,   1060,
              1621,   930,  1106,    912,   1034,    892,   1158,    990,
              1175,   850,  1121,    903,   1087,    920,   1144,   1056,
              3462,  2240,  4397,  12136,   7758,   1345,   1307,   3278,
              1950,   886,  1023,   1112,   1077,   1042,   1061,   1071,
              1484,  1001,  1096,    915,   1052,    995,   1070,    876,
              1111,   851,  1059,    805,   1112,    923,   1103,    817,
              1899,  1872,   976,    841,   1127,    956,   1159,    950,
              7791,   954,  1289,    933,   1127,   3207,   1020,    927,
              1355,   768,  1040,    745,    952,    805,   1073,    740,
              1013,   805,  1008,    796,    996,   1057,  11457,  13504,
        };
    }

    // -------------------------------------------------------
    // HuffmanMsg — wrapper around Huffman that mirrors huff_t used in msg.c
    // Maintains separate compressor and decompressor trees (both pre-seeded).
    // -------------------------------------------------------
    public class HuffmanMsg
    {
        private readonly Huffman _compressor   = new();
        private readonly Huffman _decompressor = new();

        public void AddRef(byte ch)
        {
            // Both trees are seeded identically during init
            _compressor.AddRef(ch);
            _decompressor.AddRef(ch);
        }

        public void OffsetTransmit(int ch, byte[] data, ref int offset)
            => _compressor.OffsetTransmit(ch, data, ref offset);

        public void OffsetReceive(byte[] data, ref int offset, out int ch)
            => _decompressor.OffsetReceive(data, ref offset, out ch);
    }
}
