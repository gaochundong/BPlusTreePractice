using System.Text;

namespace BPlusTreePractice
{
  public partial class BPlusTreeNode
  {
    #region Dump

    /// <summary>
    /// 将当前节点数据序列化至磁盘块中
    /// </summary>
    /// <returns>新的磁盘块序号</returns>
    internal long DumpToNewBlock()
    {
      // 由树分配存储的块序号
      long oldBlockNumber = this.BlockNumber;
      long newBlockNumber = this.Tree.AllocateBlock();

      // 写入磁盘
      this.DumpToBlock(newBlockNumber);

      // 老节点需要被释放
      if (oldBlockNumber != StorageConstants.NullBlockNumber)
      {
        if (this.Tree._freeBlocksOnAbort.ContainsKey(oldBlockNumber))
        {
          // 释放后回收再利用
          this.Tree._freeBlocksOnAbort.Remove(oldBlockNumber);
          this.Tree.ReclaimBlock(oldBlockNumber);
        }
        else
        {
          // 释放等待再提交
          this.Tree._freeBlocksOnCommit[oldBlockNumber] = oldBlockNumber;
        }
      }

      this.Tree._freeBlocksOnAbort[newBlockNumber] = newBlockNumber;

      return newBlockNumber;
    }

    /// <summary>
    /// 将当前节点数据序列化至磁盘块中
    /// </summary>
    /// <param name="blockNumber">块序号</param>
    private void DumpToBlock(long blockNumber)
    {
      // 将当前节点内容写入缓存数组
      byte[] raw = new byte[this.Tree.BlockSize];
      this.Dump(raw);

      // 将缓存数组写入磁盘块
      this.Tree.BlockFile.WriteBlock(blockNumber, raw, 0, raw.Length);

      // 则当前节点不再是脏节点
      this.IsDirty = false;
      this.BlockNumber = blockNumber;

      // 更新节点在父节点中的块序号关系，Value 存的是块序号
      if (this.Parent != null
        && this.PositionInParent >= 0
        && this.Parent._childValues[this.PositionInParent] != blockNumber)
      {
        if (this.Parent._childNodes[this.PositionInParent] != this)
        {
          throw new BPlusTreeException(
            string.Format("Invalid parent connection {0} at {1}.",
            this.Parent.BlockNumber, this.PositionInParent));
        }
        this.Parent._childValues[this.PositionInParent] = blockNumber;
        this.Parent.Soil();
      }
    }

    /// <summary>
    /// 将数据导入指定的块
    /// </summary>
    /// <param name="block">指定的块</param>
    private void Dump(byte[] block)
    {
      // indicator | first seek position | [ key storage | item storage ] * node capacity
      if (block.Length != this.Tree.BlockSize)
      {
        throw new BPlusTreeException(
          string.Format("Bad block size {0} should be {1}.",
          block.Length, this.Tree.BlockSize));
      }

      int index = 0;

      // 写入节点类型
      block[0] = (byte)BPlusTreeNodeIndicator.Internal;
      if (this.IsLeaf)
      {
        block[0] = (byte)BPlusTreeNodeIndicator.Leaf;
      }
      index++;

      // 写入首次寻址位置
      StorageHelper.Store(this._childValues[0], block, index);
      index += StorageConstants.LongLength;

      // 写入后续的键值对
      Encoder encode = Encoding.UTF8.GetEncoder();
      int maxKeyLength = this.Tree.KeyLength;
      int maxKeyPayload = maxKeyLength - StorageConstants.ShortLength;

      string lastkey = "";
      for (int keyIndex = 0; keyIndex < this.Capacity; keyIndex++)
      {
        // 键的存储分两部分 = 键的长度(Short=2Bytes) + 键的内容(外部指定)
        string key = this._childKeys[keyIndex];
        short charCount = -1;
        if (key != null)
        {
          char[] keyChars = key.ToCharArray();
          charCount = (short)encode.GetByteCount(keyChars, 0, keyChars.Length, true);
          if (charCount > maxKeyPayload)
          {
            throw new BPlusTreeException(
              string.Format("String bytes to large for use as key {0} > {1}.",
              charCount, maxKeyPayload));
          }

          // 写入键的长度(Short=2Bytes)
          StorageHelper.Store(charCount, block, index);
          index += StorageConstants.ShortLength;

          // 写入键的内容
          encode.GetBytes(keyChars, 0, keyChars.Length, block, index, true);

          index += maxKeyPayload;
        }
        else
        {
          // 如果键为 NULL，则默认存 -1
          StorageHelper.Store(charCount, block, index);

          index += StorageConstants.ShortLength;
          index += maxKeyPayload;
        }

        // 写入存储的数据
        long seekPosition = this._childValues[keyIndex + 1]; // 第一个已经被存储了
        if (key == null && seekPosition != StorageConstants.NullBlockNumber && !this.IsLeaf)
        {
          throw new BPlusTreeException(
            string.Format("Null key paired with non-null location {0}.", keyIndex));
        }
        if (lastkey == null && key != null)
        {
          throw new BPlusTreeException(
            string.Format("Null key followed by non-null key {0}.", keyIndex));
        }
        lastkey = key;

        StorageHelper.Store(seekPosition, block, index);
        index += StorageConstants.LongLength;
      }
    }

    #endregion

    #region Load

    /// <summary>
    /// 加载指定位置点的节点对象
    /// </summary>
    /// <param name="insertPosition">插入的位置点</param>
    /// <returns>节点对象</returns>
    private BPlusTreeNode LoadNodeAtPosition(int insertPosition)
    {
      // 只对内部节点应用有效
      if (this.IsLeaf)
      {
        throw new BPlusTreeException("Cannot materialize child for leaf node.");
      }

      // 获取指定位置对应子节点的块序号
      long childBlockNumber = this._childValues[insertPosition];
      if (childBlockNumber == StorageConstants.NullBlockNumber)
      {
        throw new BPlusTreeException(
          string.Format("Cannot search null sub-tree at position {0} in {1}.",
          insertPosition, this.BlockNumber));
      }

      // 节点对象已经加载过吗
      BPlusTreeNode node = this._childNodes[insertPosition];
      if (node != null) return node;

      // 如果未加载过则从块中加载，暂默认设为叶节点
      node = new BPlusTreeNode(this.Tree, this, insertPosition, true);
      node.LoadFromBlock(childBlockNumber);
      this._childNodes[insertPosition] = node;

      // 新加载的节点不需要再序列化
      this.Tree.ForgetTerminalNode(this);

      return node;
    }

    /// <summary>
    /// 从指定的块序号位置读取数据
    /// </summary>
    /// <param name="blockNumber">指定的块序号位置</param>
    internal void LoadFromBlock(long blockNumber)
    {
      // 从磁盘块文件读取块数据
      byte[] raw = new byte[this.Tree.BlockSize];
      this.Tree.BlockFile.ReadBlock(blockNumber, raw, 0, raw.Length);

      // 将原始块数据加载进当前节点
      this.Load(raw);

      // 我是新加载的，绝对不脏
      this.IsDirty = false;
      this.BlockNumber = blockNumber;

      // 记录下这个新加载的节点
      this.Tree.RecordTerminalNode(this);
    }

    /// <summary>
    /// 从给定的块中加载数据
    /// </summary>
    /// <param name="block">给定的块</param>
    private void Load(byte[] block)
    {
      this.Clear();

      // indicator | first seek position | [ key storage | item storage ] * node capacity
      if (block.Length != this.Tree.BlockSize)
      {
        throw new BPlusTreeException(
          string.Format("Bad block size {0} should be {1}.",
          block.Length, this.Tree.BlockSize));
      }

      // 获取节点类型
      byte indicator = block[0];
      this.IsLeaf = false;
      if (indicator == (byte)BPlusTreeNodeIndicator.Leaf)
      {
        this.IsLeaf = true;
      }
      else if (indicator != (byte)BPlusTreeNodeIndicator.Internal)
      {
        throw new BPlusTreeException(
          string.Format("Bad indicator {0}, not leaf or non-leaf in tree.", indicator));
      }

      // 获取首次寻址位置
      int index = 1;
      this._childValues[0] = StorageHelper.RetrieveLong(block, index);
      index += StorageConstants.LongLength;

      Decoder decode = Encoding.UTF8.GetDecoder();
      int maxKeyLength = this.Tree.KeyLength;
      int maxKeyPayload = maxKeyLength - StorageConstants.ShortLength;

      // 获取后续的键值对
      string lastKey = "";
      for (int keyIndex = 0; keyIndex < this.Capacity; keyIndex++)
      {
        // 键的存储分两部分 = 键的长度(Short=2Bytes) + 键的内容(外部指定)

        // 获取键的长度
        short keylength = StorageHelper.RetrieveShort(block, index);
        if (keylength < -1 || keylength > maxKeyPayload)
        {
          throw new BPlusTreeException("Invalid key length decoded.");
        }
        index += StorageConstants.ShortLength;

        // 获取键
        string key = null;
        if (keylength == 0)
        {
          key = "";
        }
        else if (keylength > 0)
        {
          int charCount = decode.GetCharCount(block, index, keylength);
          char[] ca = new char[charCount];
          decode.GetChars(block, index, keylength, ca, 0);
          key = new string(ca);
        }

        // 把键放入节点
        this._childKeys[keyIndex] = key;
        index += maxKeyPayload;

        // 获取存储的数据，这里与具体指定的存储类型有关系
        long seekValue = StorageHelper.RetrieveLong(block, index);

        // 如果是内部节点，检测键顺序问题
        if (!this.IsLeaf)
        {
          if (key == null && seekValue != StorageConstants.NullBlockNumber)
          {
            throw new BPlusTreeException(
              string.Format("Key is null but position is not {0}.", keyIndex));
          }
          else if (lastKey == null && key != null)
          {
            throw new BPlusTreeException(
              string.Format("Null key followed by non-null key {0}.", keyIndex));
          }
        }
        lastKey = key;

        // 把数据放入节点
        this._childValues[keyIndex + 1] = seekValue; // 第一个已经被获取了
        index += StorageConstants.LongLength;
      }
    }

    #endregion
  }
}
