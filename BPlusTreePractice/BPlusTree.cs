using System;
using System.Collections;
using System.IO;
using System.Text;

namespace BPlusTreePractice
{
  /// <summary>
  /// B+ 树
  /// </summary>
  public partial class BPlusTree
  {
    #region Fields

    // 记录在提交或者丢弃时的内存块
    internal Hashtable _freeBlocksOnCommit = new Hashtable();
    internal Hashtable _freeBlocksOnAbort = new Hashtable();

    // 记录节点用于需要释放内存时销毁掉
    private Hashtable _idToTerminalNode = new Hashtable();
    private Hashtable _terminalNodeToID = new Hashtable();
    private int _terminalNodeCount = 0;
    private int _lowerTerminalNodeCount = 0;

    #endregion

    #region Properties

    /// <summary>
    /// 树序列化存储的头部长度（Bytes）
    /// </summary>
    internal static int HeaderSize =
      StorageConstants.TreeFileHeaderPrefix.Length // 魔数前缀
      + 1                                          // 版本         1 Byte
      + StorageConstants.IntegerLength             // 节点容量     4 Bytes
      + StorageConstants.IntegerLength             // 键长度       4 Bytes
      + StorageConstants.LongLength                // 根节点块序号 8 Bytes
      + StorageConstants.LongLength;               // 空闲块头序号 8 Bytes

    /// <summary>
    /// 树使用的块大小（Bytes）
    /// </summary>
    internal int BlockSize
    {
      get
      {
        // node indicator | first seek position | [ key storage | seek position ]*
        return 1                                                                // node indicator      是否为叶节点   1 Byte
          + StorageConstants.LongLength                                         // first seek position 块索引         8 Bytes
          + (this.KeyLength + StorageConstants.LongLength) * this.NodeCapacity; // (键长度 + 值长度) * 节点容量
      }
    }

    /// <summary>
    /// 树序列化头部标示的版本号
    /// </summary>
    internal byte Version { get; private set; }

    /// <summary>
    /// 树序列化的流
    /// </summary>
    internal Stream Stream { get; private set; }

    /// <summary>
    /// 在树序列化流中查找的起始点
    /// </summary>
    internal long SeekStart { get; private set; }

    /// <summary>
    /// 树关联的块文件
    /// </summary>
    internal BlockFile BlockFile { get; private set; }

    /// <summary>
    /// 树允许的键的长度
    /// </summary>
    internal int KeyLength { get; private set; }

    /// <summary>
    /// 树允许的节点可包含键的最大数量
    /// </summary>
    internal int NodeCapacity { get; private set; }

    /// <summary>
    /// 树根节点
    /// </summary>
    internal BPlusTreeNode RootNode { get; private set; }

    /// <summary>
    /// 根节点块序号
    /// </summary>
    internal long RootNodeBlockNumber { get; private set; }

    /// <summary>
    /// 空闲块头序号
    /// </summary>
    internal long FreeBlockHeadNumber { get; private set; }

    /// <summary>
    /// 缩减尺寸控制
    /// </summary>
    internal int FootprintLimit { get; private set; }

    #endregion

    #region Ctors

    /// <summary>
    /// B+ 树
    /// </summary>
    /// <param name="stream">指定的流</param>
    /// <param name="seekStart">流起始查询点</param>
    /// <param name="keyLength">树允许的键的长度</param>
    /// <param name="nodeCapacity">树允许的节点可包含键的最大数量</param>
    /// <param name="version">树的版本号</param>
    public BPlusTree(Stream stream, long seekStart, int keyLength, int nodeCapacity, byte version)
    {
      this.Stream = stream;
      this.SeekStart = seekStart;
      this.NodeCapacity = nodeCapacity;
      this.Version = version;

      // Key 的存储分两部分 = Key 的长度(Short=2Bytes) + Key 的内容(外部指定)
      this.KeyLength = StorageConstants.ShortLength + keyLength;

      this.RootNode = null;
      this.RootNodeBlockNumber = StorageConstants.NullBlockNumber;
      this.FreeBlockHeadNumber = StorageConstants.NullBlockNumber;
      this.FootprintLimit = 100;

      if (this.SeekStart < 0)
      {
        throw new BPlusTreeException("Start seek cannot be negative.");
      }
      if (this.NodeCapacity < StorageConstants.MinNodeCapacity)
      {
        throw new BPlusTreeException("Node size must be greater than 2.");
      }
      if (this.KeyLength < StorageConstants.MinKeyLength)
      {
        throw new BPlusTreeException("Key length must be greater than 5.");
      }
    }

    #endregion

    #region File Header

    /// <summary>
    /// 写入树存储头
    /// </summary>
    private void WriteHeader()
    {
      byte[] header = this.MakeHeader();
      this.Stream.Seek(this.SeekStart, SeekOrigin.Begin);
      this.Stream.Write(header, 0, header.Length);
    }

    /// <summary>
    /// 读取树存储头
    /// </summary>
    private void ReadHeader()
    {
      // 魔数前缀 | 版本 | 节点容量 | 键大小 | 根节点块序号 | 空闲块头序号
      // prefix | version | node capacity | key length | block number of root | block number of free list head
      byte[] header = new byte[HeaderSize];

      this.Stream.Seek(this.SeekStart, SeekOrigin.Begin);
      this.Stream.Read(header, 0, HeaderSize);

      // 验证头前缀魔数
      int index = 0;
      foreach (byte b in StorageConstants.TreeFileHeaderPrefix)
      {
        if (header[index] != b)
        {
          throw new BlockFileException("Invalid header prefix.");
        }
        index++;
      }

      // 版本
      this.Version = header[index];
      index += 1;

      // 节点容量
      this.NodeCapacity = StorageHelper.RetrieveInt(header, index);
      index += StorageConstants.IntegerLength;

      // 键大小
      this.KeyLength = StorageHelper.RetrieveInt(header, index);
      index += StorageConstants.IntegerLength;

      // 根节点块序号
      this.RootNodeBlockNumber = StorageHelper.RetrieveLong(header, index);
      index += StorageConstants.LongLength;

      // 空闲块头序号
      this.FreeBlockHeadNumber = StorageHelper.RetrieveLong(header, index);
      index += StorageConstants.LongLength;

      if (this.NodeCapacity < 2)
      {
        throw new BPlusTreeException("Node size must be greater than 2.");
      }
      if (this.KeyLength < 5)
      {
        throw new BPlusTreeException("Key length must be greater than 5.");
      }
    }

    /// <summary>
    /// 构造树存储头
    /// </summary>
    /// <returns></returns>
    private byte[] MakeHeader()
    {
      // 魔数前缀 | 版本 | 节点容量 | 键大小 | 根节点块序号 | 空闲块头序号
      // prefix | version | node capacity | key length | block number of root | block number of free list head
      byte[] header = new byte[HeaderSize];

      // 魔数前缀
      StorageConstants.TreeFileHeaderPrefix.CopyTo(header, 0);

      // 版本 1 Byte
      header[StorageConstants.TreeFileHeaderPrefix.Length] = Version;

      // 节点容量 4 Bytes
      int index = StorageConstants.TreeFileHeaderPrefix.Length + 1;
      StorageHelper.Store(this.NodeCapacity, header, index);
      index += StorageConstants.IntegerLength;

      // 键大小 4 Bytes
      StorageHelper.Store(this.KeyLength, header, index);
      index += StorageConstants.IntegerLength;

      // 根节点块序号 8 Bytes
      StorageHelper.Store(this.RootNodeBlockNumber, header, index);
      index += StorageConstants.LongLength;

      // 空闲块头序号 8 Bytes
      StorageHelper.Store(this.FreeBlockHeadNumber, header, index);
      index += StorageConstants.LongLength;

      return header;
    }

    #endregion

    #region Shrink Footprint

    /// <summary>
    /// 缩减内存尺寸，用于释放内存映射的缓冲区，减少内存中缓存的节点的数量。
    /// </summary>
    public void ShrinkFootprint()
    {
      this.InvalidateTerminalNodes(this.FootprintLimit);
    }

    /// <summary>
    /// 根据限制的数量，释放一些缓存的节点
    /// </summary>
    /// <param name="limit">限制的数量</param>
    private void InvalidateTerminalNodes(int limit)
    {
      // 如果内存中缓存的节点数量多于限制数量
      while (this._terminalNodeToID.Count > limit)
      {
        // choose oldest nonterminal and deallocate it
        while (!this._idToTerminalNode.ContainsKey(this._lowerTerminalNodeCount))
        {
          this._lowerTerminalNodeCount++;
          if (this._lowerTerminalNodeCount > this._terminalNodeCount)
          {
            throw new BPlusTreeException("Internal error counting nodes, lower limit went too large.");
          }
        }

        int id = this._lowerTerminalNodeCount;
        BPlusTreeNode victim = (BPlusTreeNode)this._idToTerminalNode[id];

        this._idToTerminalNode.Remove(id);
        this._terminalNodeToID.Remove(victim);
        if (victim.BlockNumber != StorageConstants.NullBlockNumber)
        {
          victim.Invalidate(true);
        }
      }
    }

    #endregion

    #region Allocate Node Block

    /// <summary>
    /// 分配块，如果有空闲块则分配空闲块，否则分配新块。
    /// </summary>
    /// <returns>块序号</returns>
    public long AllocateBlock()
    {
      long allocated = -1;
      if (this.FreeBlockHeadNumber == StorageConstants.NullBlockNumber)
      {
        // 分配之后立即写入
        allocated = this.BlockFile.NextBlockNumber();
        return allocated;
      }

      // 重新使用空闲的块
      allocated = this.FreeBlockHeadNumber;

      // 检索新的空闲块
      this.FreeBlockHeadNumber = this.ParseFreeBlock(allocated);

      return allocated;
    }

    /// <summary>
    /// 回收再利用指定序号的块
    /// </summary>
    /// <param name="blockNumber">指定序号</param>
    public void ReclaimBlock(long blockNumber)
    {
      int freeSize = 1 + StorageConstants.LongLength;
      byte[] block = new byte[freeSize];

      this.BlockFile.ReadBlock(blockNumber, block, 0, 1);
      if (block[0] == (byte)BPlusTreeNodeIndicator.Free)
      {
        throw new BPlusTreeException("Attempt to re-free free block not allowed.");
      }
      block[0] = (byte)BPlusTreeNodeIndicator.Free;

      // 将指定序号的块置为空闲
      StorageHelper.Store(this.FreeBlockHeadNumber, block, 1);
      this.BlockFile.WriteBlock(blockNumber, block, 0, freeSize);
      this.FreeBlockHeadNumber = blockNumber;
    }

    #endregion

    #region Record Terminal Node

    /// <summary>
    /// 记录节点是可以被优化释放掉的
    /// </summary>
    /// <param name="terminalNode">可以被优化释放掉的节点</param>
    public void RecordTerminalNode(BPlusTreeNode terminalNode)
    {
      if (terminalNode == this.RootNode)
      {
        return; // never record the root node
      }
      if (this._terminalNodeToID.ContainsKey(terminalNode))
      {
        return; // don't record it again
      }

      int id = this._terminalNodeCount;
      this._terminalNodeCount++;

      this._terminalNodeToID[terminalNode] = id;
      this._idToTerminalNode[id] = terminalNode;
    }

    /// <summary>
    /// 记录节点是不可以被优化释放掉的
    /// </summary>
    /// <param name="nonTerminalNode">不可以被优化释放掉的节点</param>
    public void ForgetTerminalNode(BPlusTreeNode nonTerminalNode)
    {
      if (!this._terminalNodeToID.ContainsKey(nonTerminalNode))
      {
        return;
      }

      int id = (int)this._terminalNodeToID[nonTerminalNode];
      if (id == this._lowerTerminalNodeCount)
      {
        this._lowerTerminalNodeCount++;
      }

      this._idToTerminalNode.Remove(id);
      this._terminalNodeToID.Remove(nonTerminalNode);
    }

    #endregion

    #region Check

    /// <summary>
    /// 检查空闲块
    /// </summary>
    public void CheckFreeBlock()
    {
      this.Recover(false);

      // look at all deferred deallocations -- they should not be free
      byte[] block = new byte[1];

      foreach (DictionaryEntry thing in this._freeBlocksOnAbort)
      {
        long blockNumber = (long)thing.Key;
        this.BlockFile.ReadBlock(blockNumber, block, 0, 1);
        if (block[0] == (byte)BPlusTreeNodeIndicator.Free)
        {
          throw new BPlusTreeException(
            string.Format("Free on abort block already marked free {0}.", blockNumber));
        }
      }

      foreach (DictionaryEntry thing in this._freeBlocksOnCommit)
      {
        long blockNumber = (long)thing.Key;
        this.BlockFile.ReadBlock(blockNumber, block, 0, 1);
        if (block[0] == (byte)BPlusTreeNodeIndicator.Free)
        {
          throw new BPlusTreeException(
            string.Format("Free on commit block already marked free {0}.", blockNumber));
        }
      }
    }

    /// <summary>
    /// 检测给定的键是否符合树要求键的长度
    /// </summary>
    /// <param name="key">给定的键</param>
    /// <param name="tree">给定的树</param>
    /// <returns>是否符合树要求键的长度</returns>
    public static bool ValidateKey(string key, BPlusTree tree)
    {
      if (tree == null)
        throw new ArgumentNullException("tree");

      if (string.IsNullOrEmpty(key))
      {
        return false;
      }

      int maxKeyLength = tree.KeyLength;
      int maxKeyPayload = maxKeyLength - StorageConstants.ShortLength;

      char[] keyChars = key.ToCharArray();
      int charCount = Encoding.UTF8.GetEncoder().GetByteCount(keyChars, 0, keyChars.Length, true);
      if (charCount > maxKeyPayload)
      {
        return false;
      }

      return true;
    }

    /// <summary>
    /// 重置记录项
    /// </summary>
    private void ResetBookkeeping()
    {
      this._freeBlocksOnCommit.Clear();
      this._freeBlocksOnAbort.Clear();
      this._idToTerminalNode.Clear();
      this._terminalNodeToID.Clear();
    }

    /// <summary>
    /// 在指定的块序号之后，查找新的空闲块头序号
    /// </summary>
    /// <param name="blockNumber">指定的块序号</param>
    /// <returns>新的空闲块头序号</returns>
    private long ParseFreeBlock(long blockNumber)
    {
      int freeSize = 1 + StorageConstants.LongLength;
      byte[] block = new byte[freeSize];

      this.BlockFile.ReadBlock(blockNumber, block, 0, freeSize);
      if (block[0] != (byte)BPlusTreeNodeIndicator.Free)
      {
        throw new BPlusTreeException("Free block not marked free.");
      }

      long newHead = StorageHelper.RetrieveLong(block, 1);

      return newHead;
    }

    #endregion

    #region ToString

    /// <summary>
    /// 将树转成字符串描述
    /// </summary>
    /// <returns>树的字符串描述</returns>
    public string ToText()
    {
      StringBuilder sb = new StringBuilder();

      sb.AppendLine("B+Tree Begin -->");
      sb.AppendLine();

      // 打印基本属性
      sb.Append("NodeCapacity = " + this.NodeCapacity);
      sb.Append(", SeekStart = " + this.SeekStart);
      sb.Append(", HeaderSize = " + HeaderSize);
      sb.Append(", BlockSize = " + this.BlockSize);
      sb.Append(", KeyLength = " + this.KeyLength);
      sb.Append(", FootprintLimit = " + this.FootprintLimit);
      sb.Append(", RootNodeBlockNumber = " + this.RootNodeBlockNumber);
      sb.Append(", FreeBlockHeadNumber = " + this.FreeBlockHeadNumber);
      sb.AppendLine();
      sb.AppendLine();

      // 打印空闲块
      sb.Append("FreeBlocks : ");
      long freeHead = this.FreeBlockHeadNumber;
      string allFreeString = "[";
      Hashtable freeVisit = new Hashtable();
      while (freeHead != StorageConstants.NullBlockNumber)
      {
        allFreeString = allFreeString + " " + freeHead + ",";
        if (freeVisit.ContainsKey(freeHead))
        {
          throw new BPlusTreeException(
            string.Format("Cycle in free block list {0}.", freeHead));
        }
        freeVisit[freeHead] = freeHead;
        freeHead = this.ParseFreeBlock(freeHead);
      }
      sb.Append(allFreeString);
      sb.AppendLine("]");

      sb.Append("FreeBlocksOnCommit = " + this._freeBlocksOnCommit.Count + " : [");
      foreach (DictionaryEntry thing in this._freeBlocksOnCommit)
      {
        sb.Append(thing.Key + ",");
      }
      sb.AppendLine("]");

      sb.Append("FreeBlocksOnAbort = " + this._freeBlocksOnAbort.Count + " : [");
      foreach (DictionaryEntry thing in this._freeBlocksOnAbort)
      {
        sb.Append(thing.Key + ",");
      }
      sb.Append("]");
      sb.AppendLine();
      sb.AppendLine();

      // 打印根节点
      sb.AppendLine("Root Begin-->");
      if (this.RootNode == null)
      {
        sb.Append("[NULL ROOT]");
      }
      else
      {
        sb.Append(this.RootNode.ToText(""));
      }
      sb.AppendLine("Root End-->");
      sb.AppendLine();

      sb.Append("IdToTerminalNode : [");
      foreach (DictionaryEntry entry in _idToTerminalNode)
      {
        sb.Append(entry.Key.ToString() + "-->" + ((BPlusTreeNode)entry.Value).BlockNumber + ", ");
      }
      sb.AppendLine("]");
      sb.AppendLine();

      sb.AppendLine("B+Tree End -->");

      return sb.ToString();
    }

    public override string ToString()
    {
      return string.Format("NodeCapacity[{0}], SeekStart[{1}], HeaderSize[{2}], BlockSize[{3}], KeyLength[{4}], FootprintLimit[{5}]",
        NodeCapacity, SeekStart, HeaderSize, BlockSize, KeyLength, FootprintLimit);
    }

    #endregion
  }
}
