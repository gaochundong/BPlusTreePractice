using System;

namespace BPlusTreePractice
{
  public partial class BPlusTreeNode
  {
    #region Insert

    /// <summary>
    /// 在节点中插入键值对
    /// </summary>
    /// <param name="key">要插入的键</param>
    /// <param name="value">键对应的值</param>
    /// <param name="splitFirstKey">新分割节点的第一个键，也就是最小的键</param>
    /// <param name="splitNode">新分割节点</param>
    /// <returns>返回键序列中的最小值，如果无更改则返回空</returns>
    public string Insert(string key, long value, out string splitFirstKey, out BPlusTreeNode splitNode)
    {
      // 如果自己是叶节点，则调用叶节点插入逻辑
      if (this.IsLeaf)
      {
        return InsertLeaf(key, value, out splitFirstKey, out splitNode);
      }

      // 我不是叶，我是内部节点，所以我不仅包含键，还索引着子节点
      splitFirstKey = null;
      splitNode = null;

      // 查找第一个较大的键位置
      int insertPosition = FindAtOrNextPosition(key, false);

      // 从磁盘加载那个较大的子节点
      BPlusTreeNode insertChild = LoadNodeAtPosition(insertPosition);

      string childSplitFirstKey;
      BPlusTreeNode childSplitNode;

      // 让子节点插入新的键值对
      string childInsert = insertChild.Insert(key, value, out childSplitFirstKey, out childSplitNode);

      // 发现子节点已满，并已分割出右侧兄弟节点
      if (childSplitNode != null)
      {
        // 由于要插入新的子节点，所以标示为脏节点
        this.Soil();

        // 为新的子节点创建位置索引，即下一个
        int newChildPosition = insertPosition + 1;

        // 自己是否已满
        bool doSplit = false;

        // 如果我作为内部节点容量也满了，则内部节点也需要被分割
        if (this._childValues[this.Capacity] != StorageConstants.NullBlockNumber)
        {
          doSplit = true;
        }

        // 如果我作为内部节点容量也满了，则内部节点也需要被分割
        if (doSplit)
        {
          // 做分割准备
          this.PrepareBeforeSplit();

          // 将新键插入至数组中，将已存在的值向右移动
          InsertNewKeyInInternalNode(childSplitFirstKey, newChildPosition);

          // 被分割出的子节点的父节点为自己
          childSplitNode.ResetParent(this, newChildPosition);

          // 开始分割
          SplitInternal(ref splitFirstKey, ref splitNode);
        }
        else
        {
          // 将新键插入至数组中，将已存在的值向右移动
          InsertNewKeyInInternalNode(childSplitFirstKey, newChildPosition);

          // 被分割出的子节点的父节点为自己
          childSplitNode.ResetParent(this, newChildPosition);
        }

        // 重置节点中所有的子节点的父节点
        this.ResetAllChildrenParent();
      }

      // 返回键序列中的最小值，如果无更改则返回空
      if (insertPosition == 0) return childInsert;
      else return null;
    }

    /// <summary>
    /// 将新键插入至数组中，将已存在的值向右移动
    /// </summary>
    /// <param name="key">要插入的键</param>
    /// <param name="insertPosition">要插入的位置</param>
    private void InsertNewKeyInInternalNode(string key, int insertPosition)
    {
      // 新节点位置上及其右侧内容全部向右移动 1 位，为新节点空出位置
      for (int i = this._childKeys.Length - 2; i >= insertPosition - 1; i--)
      {
        int iPlus1 = i + 1;
        int iPlus2 = iPlus1 + 1;
        this._childKeys[iPlus1] = this._childKeys[i];
        this._childValues[iPlus2] = this._childValues[iPlus1];
        this._childNodes[iPlus2] = this._childNodes[iPlus1];
      }

      // 新节点的位置存放新节点的第一个键，也就是新节点中最小的键
      this._childKeys[insertPosition - 1] = key;
    }

    /// <summary>
    /// 在叶节点中插入键值对
    /// </summary>
    /// <param name="key">要插入的键</param>
    /// <param name="value">键对应的值</param>
    /// <param name="splitFirstKey">新分割节点的第一个键，也就是最小的键</param>
    /// <param name="splitNode">新分割节点</param>
    /// <returns>返回键序列中的最小值，如果无更改则返回空</returns>
    public string InsertLeaf(string key, long value, out string splitFirstKey, out BPlusTreeNode splitNode)
    {
      splitFirstKey = null;
      splitNode = null;

      // 如果自己不是叶节点，那一定是用错了方法
      if (!this.IsLeaf)
      {
        throw new BPlusTreeException("Bad call to insert leaf, this is not a leaf.");
      }

      // 标示节点已被更改，自己现在是脏节点了
      this.Soil();

      // 查找新键的位置，如果键不存在则查找第一个较大的键位置
      int insertPosition = FindAtOrNextPosition(key, false);

      // 自己是否已满
      bool doSplit = false;

      // 节点未满
      if (insertPosition < this.Capacity)
      {
        // 要插入位置的键为空，或者与当前键相同
        if (this._childKeys[insertPosition] == null
          || this.Tree.CompareKey(this._childKeys[insertPosition], key) == 0)
        {
          // 更改键对应关联附属数据及位置，不支持重复的关联附属数据
          this._childKeys[insertPosition] = key;
          this._childValues[insertPosition] = value;

          // 返回键序列中的最小值，如果无更改则返回空
          if (insertPosition == 0) return key;
          else return null;
        }
        else
        {
          // 如果键不存在，找到的点为比指定键稍大的键
          // 需要将稍大的键向右移动，挪出放新键的位置
        }
      }
      else
      {
        // 节点已满，准备分割节点
        doSplit = true;
      }

      // 查看是否还有空位置
      int emptyIndex = insertPosition;
      while (emptyIndex < this._childKeys.Length && this._childKeys[emptyIndex] != null)
      {
        emptyIndex++;
      }

      // 已经没有空位置了，需要分割节点
      if (emptyIndex >= this._childKeys.Length)
      {
        doSplit = true;
      }

      // 如果需要分割
      if (doSplit)
      {
        // 将当前节点的容量扩大(+1)，为插入和分割做准备
        PrepareBeforeSplit();

        // 将新键插入至数组中，将已存在的值向右移动
        InsertNewKeyInLeafNode(key, value, insertPosition);

        // 开始分割
        SplitLeaf(ref splitFirstKey, ref splitNode);
      }
      else
      {
        // 还有容量，不需要分割，将新键插入至数组中，将已存在的值向右移动
        InsertNewKeyInLeafNode(key, value, insertPosition);
      }

      // 返回键序列中的最小值，如果无更改则返回空
      if (insertPosition == 0) return key;
      else return null;
    }

    /// <summary>
    /// 将新键插入至数组中，将已存在的值向右移动
    /// </summary>
    /// <param name="key">要插入的键</param>
    /// <param name="value">要插入的值</param>
    /// <param name="insertPosition">要插入的位置</param>
    private void InsertNewKeyInLeafNode(string key, long value, int insertPosition)
    {
      string nextKey = this._childKeys[insertPosition];
      long nextValue = this._childValues[insertPosition];

      this._childKeys[insertPosition] = key;
      this._childValues[insertPosition] = value;

      string tempKey = nextKey;
      long tempValue = nextValue;
      int tempInsertPosition = insertPosition;

      while (nextKey != null)
      {
        tempKey = nextKey;
        tempValue = nextValue;

        tempInsertPosition++;

        nextKey = this._childKeys[tempInsertPosition];
        nextValue = this._childValues[tempInsertPosition];

        this._childKeys[tempInsertPosition] = tempKey;
        this._childValues[tempInsertPosition] = tempValue;
      }
    }

    /// <summary>
    /// 在节点中查找指定键的索引，如果无此键则查找第一个较大的键索引
    /// </summary>
    /// <param name="compareKey">要被插入的键</param>
    /// <param name="lookPastOnly">如果节点为叶节点，并且此参数为真，则查找一个较大的键值</param>
    /// <returns>在节点中查找指定键的索引，如果无此键则查找第一个较大的键索引</returns>
    private int FindAtOrNextPosition(string compareKey, bool lookPastOnly)
    {
      int insertPosition = 0;

      // 如果节点为叶节点，并且 lookPastOnly 为真，则查找一个较大的键值
      if (this.IsLeaf && !lookPastOnly)
      {
        // 从左到右对已存放的键进行比较，找到大于等于新键的位置
        while (insertPosition < this.Capacity
          && this._childKeys[insertPosition] != null
          && this.Tree.CompareKey(this._childKeys[insertPosition], compareKey) < 0)
        {
          insertPosition++;
        }
      }
      else
      {
        // 从左到右对已存放的键进行比较，找到大于新键的位置
        while (insertPosition < this.Capacity
          && this._childKeys[insertPosition] != null
          && this.Tree.CompareKey(this._childKeys[insertPosition], compareKey) <= 0)
        {
          insertPosition++;
        }
      }

      return insertPosition;
    }

    #endregion

    #region Split

    /// <summary>
    /// 将当前节点的容量扩大(+1)，为插入和分割做准备。
    /// </summary>
    private void PrepareBeforeSplit()
    {
      int superSize = this.Capacity + 1;

      string[] keys = new string[superSize];
      long[] positions = new long[superSize + 1];
      BPlusTreeNode[] materialized = new BPlusTreeNode[superSize + 1];

      Array.Copy(this._childKeys, 0, keys, 0, this.Capacity);
      keys[this.Capacity] = null;
      Array.Copy(this._childValues, 0, positions, 0, this.Capacity + 1);
      positions[this.Capacity + 1] = StorageConstants.NullBlockNumber;
      Array.Copy(this._childNodes, 0, materialized, 0, this.Capacity + 1);
      materialized[this.Capacity + 1] = null;

      this._childValues = positions;
      this._childKeys = keys;
      this._childNodes = materialized;
    }

    /// <summary>
    /// 节点分割后，恢复节点容量(-1)
    /// </summary>
    /// <param name="splitPoint">分割点</param>
    private void RepairAfterSplit(int splitPoint)
    {
      // 临时存放下数据引用
      string[] keys = this._childKeys;
      long[] values = this._childValues;
      BPlusTreeNode[] nodes = this._childNodes;

      // 恢复当前节点容量
      this._childKeys = new string[this.Capacity];
      this._childValues = new long[this.Capacity + 1];
      this._childNodes = new BPlusTreeNode[this.Capacity + 1];

      // 保留分割点左侧数据
      Array.Copy(keys, 0, this._childKeys, 0, splitPoint);
      Array.Copy(values, 0, this._childValues, 0, splitPoint);
      Array.Copy(nodes, 0, this._childNodes, 0, splitPoint);

      // 右侧数据都标记为空
      for (int i = splitPoint; i < this._childKeys.Length; i++)
      {
        this._childKeys[i] = null;
        this._childValues[i] = StorageConstants.NullBlockNumber;
        this._childNodes[i] = null;
      }
    }

    /// <summary>
    /// 分割当前节点
    /// </summary>
    /// <param name="splitFirstKey">新分割节点的第一个键，也就是最小的键</param>
    /// <param name="splitNode">新分割节点</param>
    private void SplitInternal(ref string splitFirstKey, ref BPlusTreeNode splitNode)
    {
      // 从中间开始分割
      int splitPoint = this._childNodes.Length / 2 - 1;

      // 分割出的新节点的第一个键
      splitFirstKey = this._childKeys[splitPoint];

      // 新建节点，包含分割点右侧所有数据
      splitNode = new BPlusTreeNode(this.Tree, this.Parent, -1, this.IsLeaf);
      splitNode.Clear(); // redundant.

      // 记录已经扩充的数据结构         
      long[] values = this._childValues;
      string[] keys = this._childKeys;
      BPlusTreeNode[] nodes = this._childNodes;

      // 重置和清空数据
      this.Clear();

      // 将分割点左侧的数据拷贝至此节点
      Array.Copy(keys, 0, this._childKeys, 0, splitPoint);
      Array.Copy(values, 0, this._childValues, 0, splitPoint + 1);
      Array.Copy(nodes, 0, this._childNodes, 0, splitPoint + 1);

      // 将分割点右侧的数据拷贝至新的分割节点
      int remainingKeys = this.Capacity - splitPoint;
      Array.Copy(keys, splitPoint + 1, splitNode._childKeys, 0, remainingKeys);
      Array.Copy(values, splitPoint + 1, splitNode._childValues, 0, remainingKeys + 1);
      Array.Copy(nodes, splitPoint + 1, splitNode._childNodes, 0, remainingKeys + 1);

      // 重置新节点中所有的子节点的父节点
      splitNode.ResetAllChildrenParent();

      // 存储新节点
      splitNode.DumpToNewBlock();
      splitNode.CheckIfTerminal();
      splitNode.Soil();

      this.CheckIfTerminal();
    }

    /// <summary>
    /// 分割当前节点
    /// </summary>
    /// <param name="splitFirstKey">新分割节点的第一个键，也就是最小的键</param>
    /// <param name="splitNode">新分割节点</param>
    private void SplitLeaf(ref string splitFirstKey, ref BPlusTreeNode splitNode)
    {
      // 从中间开始分割
      int splitPoint = this._childKeys.Length / 2;
      int splitLength = this._childKeys.Length - splitPoint;

      // 新创建的分割出的节点
      splitNode = new BPlusTreeNode(this.Tree, this.Parent, -1, this.IsLeaf);

      // 将指定分割点右侧的数据拷贝至新的节点，新节点为当前节点的右兄弟节点
      Array.Copy(this._childKeys, splitPoint, splitNode._childKeys, 0, splitLength);
      Array.Copy(this._childValues, splitPoint, splitNode._childValues, 0, splitLength);
      Array.Copy(this._childNodes, splitPoint, splitNode._childNodes, 0, splitLength);

      // 记录分割节点的第一个键
      splitFirstKey = splitNode._childKeys[0];

      // 存储新节点至块文件
      splitNode.DumpToNewBlock();

      // 分割完毕，当前节点恢复之前的扩容，处理分割点左侧数据
      RepairAfterSplit(splitPoint);

      // 我可以被释放掉
      this.Tree.RecordTerminalNode(splitNode);

      // 新节点及其父节点需要标记为脏节点
      splitNode.Soil();
    }

    #endregion
  }
}
