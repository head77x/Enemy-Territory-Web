// Ported from: src/qcommon/huffman.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.
//
// Adaptive Huffman coding — Sayood's algorithm.
// Ranks are implicitly defined by node position in the doubly-linked list.

using System;

namespace ET.Core
{
    public class Huffman
    {
        private const int NYT            = 256; // Not Yet Transmitted symbol
        private const int INTERNAL_NODE  = 257;
        private const int MAX_NODES      = 768;
        private const int MAX_SYMBOLS    = 258;

        // -------------------------------------------------------
        // Node
        // -------------------------------------------------------
        private class Node
        {
            public Node Left, Right, Parent;
            public Node Next, Prev;
            public Node[] Head; // pointer-to-pointer via 1-element array
            public int    Weight;
            public int    Symbol;
        }

        // -------------------------------------------------------
        // HuffTree — mirrors huff_t
        // -------------------------------------------------------
        private class HuffTree
        {
            public Node   Tree;
            public Node   LHead;
            public Node   LTail;
            public int    BlocNode;
            public int    BlocPtrs;
            public Node[] NodeList  = new Node[MAX_NODES];
            public Node[] NodePtrs  = new Node[MAX_NODES]; // ptr-pool (stored as object refs)
            public Node[][] PtrPool = new Node[MAX_NODES][];
            public Node[]   FreeList;
            public Node[]   Loc      = new Node[MAX_SYMBOLS];

            public HuffTree()
            {
                for (int i = 0; i < MAX_NODES;    i++) NodeList[i] = new Node();
                for (int i = 0; i < MAX_NODES;    i++) PtrPool[i]  = new Node[1];
            }
        }

        // -------------------------------------------------------
        // Public state (mirrors huffman_t with compressor/decompressor)
        // -------------------------------------------------------
        private HuffTree _compressor;
        private HuffTree _decompressor;

        // -------------------------------------------------------
        // Init
        // -------------------------------------------------------
        public Huffman()
        {
            _compressor   = new HuffTree();
            _decompressor = new HuffTree();
            InitTree(_decompressor, isCompressor: false);
            InitTree(_compressor,   isCompressor: true);
        }

        private static void InitTree(HuffTree h, bool isCompressor)
        {
            Node root = h.NodeList[h.BlocNode++];
            root.Symbol = NYT;
            root.Weight = 0;
            root.Next = root.Prev = null;
            root.Parent = root.Left = root.Right = null;
            h.Tree = h.LHead = h.Loc[NYT] = root;
            if (isCompressor) h.LTail = root;
        }

        // -------------------------------------------------------
        // Bit I/O helpers
        // -------------------------------------------------------
        private static void PutBit(int bit, byte[] fout, ref int offset)
        {
            int x = offset >> 3;
            int y = offset & 7;
            if (y == 0) fout[x] = 0;
            fout[x] |= (byte)(bit << y);
            offset++;
        }

        private static int GetBit(byte[] fin, ref int offset)
        {
            int t = (fin[offset >> 3] >> (offset & 7)) & 0x1;
            offset++;
            return t;
        }

        // -------------------------------------------------------
        // Node pool management
        // -------------------------------------------------------
        private static Node[] GetPpNode(HuffTree huff)
        {
            if (huff.FreeList == null)
            {
                return huff.PtrPool[huff.BlocPtrs++];
            }
            Node[] tmp = new Node[1];
            tmp[0] = huff.FreeList[0];
            // restore freelist pointer (encoded as the stored node ref)
            // simplified: just null the slot and return it
            huff.FreeList = null;
            return tmp;
        }

        private static void FreePpNode(HuffTree huff, Node[] ppnode)
        {
            huff.FreeList = ppnode;
        }

        // -------------------------------------------------------
        // Tree manipulation
        // -------------------------------------------------------
        private static void Swap(HuffTree huff, Node node1, Node node2)
        {
            Node par1 = node1.Parent;
            Node par2 = node2.Parent;

            if (par1 != null)
            {
                if (par1.Left == node1) par1.Left  = node2;
                else                    par1.Right = node2;
            }
            else huff.Tree = node2;

            if (par2 != null)
            {
                if (par2.Left == node2) par2.Left  = node1;
                else                    par2.Right = node1;
            }
            else huff.Tree = node1;

            node1.Parent = par2;
            node2.Parent = par1;
        }

        private static void SwapList(Node node1, Node node2)
        {
            Node tmp;

            tmp = node1.Next; node1.Next = node2.Next; node2.Next = tmp;
            tmp = node1.Prev; node1.Prev = node2.Prev; node2.Prev = tmp;

            if (node1.Next == node1) node1.Next = node2;
            if (node2.Next == node2) node2.Next = node1;
            if (node1.Next != null)  node1.Next.Prev = node1;
            if (node2.Next != null)  node2.Next.Prev = node2;
            if (node1.Prev != null)  node1.Prev.Next = node1;
            if (node2.Prev != null)  node2.Prev.Next = node2;
        }

        private static void Increment(HuffTree huff, Node node)
        {
            if (node == null) return;

            if (node.Next != null && node.Next.Weight == node.Weight)
            {
                Node lnode = node.Head[0];
                if (lnode != node.Parent)
                    Swap(huff, lnode, node);
                SwapList(lnode, node);
            }

            if (node.Prev != null && node.Prev.Weight == node.Weight)
            {
                node.Head[0] = node.Prev;
            }
            else
            {
                node.Head[0] = null;
                FreePpNode(huff, node.Head);
            }

            node.Weight++;

            if (node.Next != null && node.Next.Weight == node.Weight)
            {
                node.Head = node.Next.Head;
            }
            else
            {
                node.Head = GetPpNode(huff);
                node.Head[0] = node;
            }

            if (node.Parent != null)
            {
                Increment(huff, node.Parent);
                if (node.Prev == node.Parent)
                {
                    SwapList(node, node.Parent);
                    if (node.Head[0] == node)
                        node.Head[0] = node.Parent;
                }
            }
        }

        private static void AddRef(HuffTree huff, byte ch)
        {
            if (huff.Loc[ch] == null)
            {
                Node tnode2 = huff.NodeList[huff.BlocNode++];
                Node tnode  = huff.NodeList[huff.BlocNode++];

                tnode2.Symbol = INTERNAL_NODE;
                tnode2.Weight = 1;
                tnode2.Next = huff.LHead.Next;
                if (huff.LHead.Next != null)
                {
                    huff.LHead.Next.Prev = tnode2;
                    tnode2.Head = huff.LHead.Next.Weight == 1
                        ? huff.LHead.Next.Head
                        : GetPpNode(huff);
                    if (tnode2.Head[0] == null) tnode2.Head[0] = tnode2;
                }
                else
                {
                    tnode2.Head = GetPpNode(huff);
                    tnode2.Head[0] = tnode2;
                }
                huff.LHead.Next = tnode2;
                tnode2.Prev = huff.LHead;

                tnode.Symbol = ch;
                tnode.Weight = 1;
                tnode.Next = huff.LHead.Next;
                if (huff.LHead.Next != null)
                {
                    huff.LHead.Next.Prev = tnode;
                    tnode.Head = huff.LHead.Next.Weight == 1
                        ? huff.LHead.Next.Head
                        : GetPpNode(huff);
                    if (tnode.Head[0] == null) tnode.Head[0] = tnode2;
                }
                else
                {
                    tnode.Head = GetPpNode(huff);
                    tnode.Head[0] = tnode;
                }
                huff.LHead.Next = tnode;
                tnode.Prev = huff.LHead;
                tnode.Left = tnode.Right = null;

                if (huff.LHead.Parent != null)
                {
                    if (huff.LHead.Parent.Left == huff.LHead) huff.LHead.Parent.Left  = tnode2;
                    else                                       huff.LHead.Parent.Right = tnode2;
                }
                else huff.Tree = tnode2;

                tnode2.Right = tnode;
                tnode2.Left  = huff.LHead;
                tnode2.Parent = huff.LHead.Parent;
                huff.LHead.Parent = tnode.Parent = tnode2;
                huff.Loc[ch] = tnode;

                Increment(huff, tnode2.Parent);
            }
            else
            {
                Increment(huff, huff.Loc[ch]);
            }
        }

        // -------------------------------------------------------
        // Symbol I/O
        // -------------------------------------------------------
        private static int Receive(Node node, out int ch, byte[] fin, ref int bloc)
        {
            while (node != null && node.Symbol == INTERNAL_NODE)
            {
                int bit = (fin[bloc >> 3] >> (bloc & 7)) & 0x1;
                bloc++;
                node = bit != 0 ? node.Right : node.Left;
            }
            if (node == null) { ch = 0; return 0; }
            ch = node.Symbol;
            return ch;
        }

        private static void Send(Node node, Node child, byte[] fout, ref int bloc)
        {
            if (node.Parent != null) Send(node.Parent, node, fout, ref bloc);
            if (child != null)
            {
                int bit = (node.Right == child) ? 1 : 0;
                int x = bloc >> 3;
                int y = bloc & 7;
                if (y == 0) fout[x] = 0;
                fout[x] |= (byte)(bit << y);
                bloc++;
            }
        }

        private static void Transmit(HuffTree huff, int ch, byte[] fout, ref int bloc)
        {
            if (huff.Loc[ch] == null)
            {
                Transmit(huff, NYT, fout, ref bloc);
                for (int i = 7; i >= 0; i--)
                {
                    int bit = (ch >> i) & 0x1;
                    int x = bloc >> 3;
                    int y = bloc & 7;
                    if (y == 0) fout[x] = 0;
                    fout[x] |= (byte)(bit << y);
                    bloc++;
                }
            }
            else
            {
                Send(huff.Loc[ch], null, fout, ref bloc);
            }
        }

        // -------------------------------------------------------
        // Public API: Compress / Decompress  (operate on byte[])
        // -------------------------------------------------------

        public byte[] Compress(byte[] input, int offset)
        {
            int size = input.Length - offset;
            if (size <= 0) return input;

            var huff = new HuffTree();
            InitTree(huff, isCompressor: true);

            byte[] seq = new byte[65536];
            seq[0] = (byte)(size >> 8);
            seq[1] = (byte)(size & 0xFF);
            int bloc = 16;

            for (int i = 0; i < size; i++)
            {
                int ch = input[offset + i];
                Transmit(huff, ch, seq, ref bloc);
                AddRef(huff, (byte)ch);
            }

            bloc += 8; // align to next byte
            int outSize = (bloc >> 3) + offset;
            byte[] result = new byte[outSize];
            Buffer.BlockCopy(input, 0, result, 0, offset);
            Buffer.BlockCopy(seq, 0, result, offset, bloc >> 3);
            return result;
        }

        public byte[] Decompress(byte[] input, int offset)
        {
            int size = input.Length - offset;
            if (size <= 0) return input;

            var huff = new HuffTree();
            InitTree(huff, isCompressor: false);

            byte[] buffer = new byte[size];
            Buffer.BlockCopy(input, offset, buffer, 0, size);

            int cch  = buffer[0] * 256 + buffer[1];
            if (cch > input.Length - offset) cch = input.Length - offset;

            byte[] seq  = new byte[65536];
            int    bloc = 16;

            for (int j = 0; j < cch; j++)
            {
                if ((bloc >> 3) > size) { seq[j] = 0; break; }
                Receive(huff.Tree, out int ch, buffer, ref bloc);
                if (ch == NYT)
                {
                    ch = 0;
                    for (int i = 0; i < 8; i++)
                        ch = (ch << 1) + GetBit(buffer, ref bloc);
                }
                seq[j] = (byte)ch;
                AddRef(huff, (byte)ch);
            }

            byte[] result = new byte[cch + offset];
            Buffer.BlockCopy(input, 0, result, 0, offset);
            Buffer.BlockCopy(seq, 0, result, offset, cch);
            return result;
        }

        // -------------------------------------------------------
        // Static one-shot helpers (bit-level, mirrors Huff_putBit/getBit)
        // -------------------------------------------------------
        public static void PutBitStatic(int bit, byte[] fout, ref int offset)
            => PutBit(bit, fout, ref offset);

        public static int GetBitStatic(byte[] fin, ref int offset)
            => GetBit(fin, ref offset);
    }
}
