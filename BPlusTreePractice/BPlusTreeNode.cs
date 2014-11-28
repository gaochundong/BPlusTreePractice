using System;
using System.Collections;
using System.Globalization;
using System.Text;

namespace BPlusTreePractice
{
  /// <summary>
  /// B+ 树节点
  /// </summary>
  public partial class BPlusTreeNode
  {
    #region Properties

    /// <summary>
    /// 包含该节点的树
    /// </summary>
    internal BPlusTree Tree { get; private set; }

    /// <summary>
    /// 节点的父节点
    /// </summary>
    internal BPlusTreeNode Parent { get; private set; }

    /// <summary>
    /// 节点在父节点中的位置
    /// </summary>
    internal int PositionInParent { get; private set; }

    /// <summary>
    /// 是否为叶节点
    /// </summary>
    internal bool IsLeaf { get; private set; }

    /// <summary>
    /// 节点可包含键的最大数量
    /// </summary>
    internal int Capacity { get; private set; }

    /// <summary>
    /// 节点关联的块序号
    /// </summary>
    internal long BlockNumber { get; private set; }

    /// <summary>
    /// 是否为脏节点，如果为真则节点需要被持久化。
    /// </summary>
    internal bool IsDirty { get; private set; }

    /// <summary>
    /// 节点已包含键的数量
    /// </summary>
    internal int Count
    {
      get
      {
        int inUsing = 0;

        for (int i = 0; i < this.Capacity; i++)
        {
          if (this._childKeys[i] == null)
          {
            break;
          }
          inUsing++;
        }

        return inUsing;
      }
    }

    #endregion

    #region Fields

    /// <summary>
    /// 子节点数组
    /// </summary>
    private BPlusTreeNode[] _childNodes;

    /// <summary>
    /// 节点包含的键数组
    /// </summary>
    private string[] _childKeys;

    /// <summary>
    /// 节点包含的值数组
    /// </summary>
    private long[] _childValues;

    #endregion

    #region Ctors

    /// <summary>
    /// B+ 树节点
    /// </summary>
    /// <param name="tree">包含该节点的树</param>
    /// <param name="parent">节点的父节点</param>
    /// <param name="positionInParent">在父节点中的位置</param>
    /// <param name="isLeaf">是否为叶节点</param>
    public BPlusTreeNode(BPlusTree tree, BPlusTreeNode parent, int positionInParent, bool isLeaf)
    {
      if (tree == null)
        throw new ArgumentNullException("tree");

      this.Tree = tree;
      this.Parent = parent;
      this.IsLeaf = isLeaf;

      this.PositionInParent = -1;
      this.BlockNumber = StorageConstants.NullBlockNumber;
      this.Capacity = tree.NodeCapacity;
      this.IsDirty = true;

      this.Initialize();

      // 存在父节点，只有根节点没有父节点
      if (parent != null && positionInParent >= 0)
      {
        // B+ 树 父节点中键值数 + 1 = 子节点数量
        if (positionInParent > this.Capacity)
        {
          throw new BPlusTreeException("The position in parent is beyond the limit of node capacity.");
        }

        // 建立与父节点的关系
        this.Parent._childNodes[positionInParent] = this;
        this.BlockNumber = this.Parent._childValues[positionInParent];
        this.PositionInParent = positionInParent;
      }
    }

    #endregion

    #region Initialize

    /// <summary>
    /// 初始化节点中数据
    /// </summary>
    private void Initialize()
    {
      Clear();
    }

    /// <summary>
    /// 清理节点中存储的数据
    /// </summary>
    private void Clear()
    {
      this._childKeys = new string[this.Capacity];
      this._childValues = new long[this.Capacity + 1];
      this._childNodes = new BPlusTreeNode[this.Capacity + 1];

      for (int i = 0; i < this.Capacity; i++)
      {
        this._childKeys[i] = null;
        this._childValues[i] = StorageConstants.NullBlockNumber;
        this._childNodes[i] = null;
      }

      this._childValues[this.Capacity] = StorageConstants.NullBlockNumber;
      this._childNodes[this.Capacity] = null;

      // this is now a terminal node
      CheckIfTerminal();
    }

    #endregion

    #region Root

    /// <summary>
    /// 将当前节点置为根节点
    /// </summary>
    /// <returns>节点的块序号</returns>
    public long MakeAsRoot()
    {
      this.Parent = null;
      this.PositionInParent = -1;
      if (this.BlockNumber == StorageConstants.NullBlockNumber)
      {
        throw new BPlusTreeException("No root seek allocated to new root.");
      }
      return this.BlockNumber;
    }

    /// <summary>
    /// 为树构造一个根节点
    /// </summary>
    /// <param name="tree">指定树</param>
    /// <param name="isLeaf">是否为叶节点</param>
    /// <returns>根节点</returns>
    public static BPlusTreeNode MakeRoot(BPlusTree tree, bool isLeaf)
    {
      return new BPlusTreeNode(tree, null, -1, isLeaf);
    }

    /// <summary>
    /// 将旧的根节点分割，新的根节点将有两个子节点
    /// </summary>
    /// <param name="oldRoot">原根节点</param>
    /// <param name="splitFirstKey">新根节点的第一个键</param>
    /// <param name="splitNode">新分割出的节点</param>
    /// <param name="tree">指定的树</param>
    /// <returns>新根节点</returns>
    public static BPlusTreeNode BinaryRoot(
      BPlusTreeNode oldRoot, string splitFirstKey,
      BPlusTreeNode splitNode, BPlusTree tree)
    {
      if (oldRoot == null)
        throw new ArgumentNullException("oldRoot");
      if (splitNode == null)
        throw new ArgumentNullException("splitNode");

      // 已不是叶节点
      BPlusTreeNode newRoot = MakeRoot(tree, false);

      // 新的跟记录分割节点的第一个键
      newRoot._childKeys[0] = splitFirstKey;

      // 新旧节点分别为新的根节点的索引 0 1 位置
      oldRoot.ResetParent(newRoot, 0);
      splitNode.ResetParent(newRoot, 1);

      return newRoot;
    }

    #endregion

    #region Parent

    /// <summary>
    /// 重置节点的父节点
    /// </summary>
    /// <param name="newParent">父节点</param>
    /// <param name="positionInParent">在父节点中的位置</param>
    private void ResetParent(BPlusTreeNode newParent, int positionInParent)
    {
      this.Parent = newParent;
      this.PositionInParent = positionInParent;

      // 父节点存储的值为子节点的块序号
      newParent._childValues[positionInParent] = this.BlockNumber;
      newParent._childNodes[positionInParent] = this;

      // 父节点已经有了一个序列化的子节点，父节点不再是终端节点 // TODO
      this.Tree.ForgetTerminalNode(this.Parent);
    }

    /// <summary>
    /// 重置所有子节点的父节点
    /// </summary>
    private void ResetAllChildrenParent()
    {
      for (int i = 0; i <= this.Capacity; i++)
      {
        BPlusTreeNode node = this._childNodes[i];
        if (node != null)
        {
          node.ResetParent(this, i);
        }
      }
    }

    #endregion

    #region Dirty

    /// <summary>
    /// 获取节点的第一个子节点
    /// </summary>
    /// <returns>节点的第一个子节点</returns>
    public BPlusTreeNode FirstChild()
    {
      BPlusTreeNode firstNode = this.LoadNodeAtPosition(0);
      if (firstNode == null)
      {
        throw new BPlusTreeException("No first child.");
      }
      return firstNode;
    }

    /// <summary>
    /// 使节点作废，同时作废所有子节点，并从父节点中移除关系
    /// </summary>
    /// <param name="isDestroy">是否销毁掉节点</param>
    /// <returns>节点新的块序号</returns>
    public long Invalidate(bool isDestroy)
    {
      long blockNumber = this.BlockNumber;

      // 非叶节点
      if (!this.IsLeaf)
      {
        // need to invalidate kids
        for (int i = 0; i < this.Capacity + 1; i++)
        {
          if (this._childNodes[i] != null)
          {
            // new block numbers are recorded automatically
            this._childValues[i] = this._childNodes[i].Invalidate(true);
          }
        }
      }

      // store if dirty
      if (this.IsDirty)
      {
        blockNumber = this.DumpToNewBlock();
      }

      // remove from owner archives if present
      this.Tree.ForgetTerminalNode(this); // 我已经有了一个序列化的子节点，我不再是终端节点

      // remove from parent
      if (this.Parent != null && this.PositionInParent >= 0)
      {
        this.Parent._childNodes[this.PositionInParent] = null;
        this.Parent._childValues[this.PositionInParent] = blockNumber; // should be redundant
        this.Parent.CheckIfTerminal();
        this.PositionInParent = -1;
      }

      // render all structures useless, just in case...
      if (isDestroy)
      {
        this.Destroy();
      }

      return blockNumber;
    }

    /// <summary>
    /// 销毁节点
    /// </summary>
    private void Destroy()
    {
      // make sure the structure is useless, it should no longer be used.
      this.Tree = null;
      this.Parent = null;
      this.Capacity = -100;
      this._childValues = null;
      this._childKeys = null;
      this._childNodes = null;
      this.BlockNumber = StorageConstants.NullBlockNumber;
      this.PositionInParent = -100;
      this.IsDirty = false;
    }

    /// <summary>
    /// 当节点被删除后释放节点及块，块即被回收
    /// </summary>
    public void Free()
    {
      if (this.BlockNumber != StorageConstants.NullBlockNumber)
      {
        if (this.Tree._freeBlocksOnAbort.ContainsKey(this.BlockNumber))
        {
          // free it now
          this.Tree._freeBlocksOnAbort.Remove(this.BlockNumber);
          this.Tree.ReclaimBlock(this.BlockNumber);
        }
        else
        {
          // free on commit
          this.Tree._freeBlocksOnCommit[this.BlockNumber] = this.BlockNumber;
        }
      }
      this.BlockNumber = StorageConstants.NullBlockNumber; // don't do it twice...
    }

    /// <summary>
    /// 合理性检查
    /// </summary>
    /// <param name="visited"></param>
    /// <returns></returns>
    public string SanityCheck(Hashtable visited)
    {
      string result = null;

      if (visited == null)
      {
        visited = new Hashtable();
      }
      if (visited.ContainsKey(this))
      {
        throw new BPlusTreeException(
          string.Format("Node visited twice {0}.", this.BlockNumber));
      }

      visited[this] = this.BlockNumber;
      if (this.BlockNumber != StorageConstants.NullBlockNumber)
      {
        if (visited.ContainsKey(this.BlockNumber))
        {
          throw new BPlusTreeException(
            string.Format("Block number seen twice {0}.", this.BlockNumber));
        }
        visited[this.BlockNumber] = this;
      }

      if (this.Parent != null)
      {
        if (this.Parent.IsLeaf)
        {
          throw new BPlusTreeException("Parent is leaf.");
        }

        this.Parent.LoadNodeAtPosition(this.PositionInParent);
        if (this.Parent._childNodes[this.PositionInParent] != this)
        {
          throw new BPlusTreeException("Incorrect index in parent.");
        }

        // since not at root there should be at least size/2 keys
        int limit = this.Capacity / 2;
        if (this.IsLeaf)
        {
          limit--;
        }
        for (int i = 0; i < limit; i++)
        {
          if (this._childKeys[i] == null)
          {
            throw new BPlusTreeException("Null child in first half.");
          }
        }
      }

      result = this._childKeys[0]; // for leaf
      if (!this.IsLeaf)
      {
        this.LoadNodeAtPosition(0);
        result = this._childNodes[0].SanityCheck(visited);

        for (int i = 0; i < this.Capacity; i++)
        {
          if (this._childKeys[i] == null)
          {
            break;
          }

          this.LoadNodeAtPosition(i + 1);
          string least = this._childNodes[i + 1].SanityCheck(visited);
          if (least == null)
          {
            throw new BPlusTreeException(
              string.Format("Null least in child doesn't match node entry {0}.", this._childKeys[i]));
          }
          if (!least.Equals(this._childKeys[i]))
          {
            throw new BPlusTreeException(
              string.Format("Least in child {0} doesn't match node entry {1}.",
              least, this._childKeys[i]));
          }
        }
      }

      // 查询重复的键
      string lastkey = this._childKeys[0];
      for (int i = 1; i < this.Capacity; i++)
      {
        if (this._childKeys[i] == null)
        {
          break;
        }
        if (lastkey.Equals(this._childKeys[i]))
        {
          throw new BPlusTreeException(
            string.Format("Duplicate key in node {0}.", lastkey));
        }
        lastkey = this._childKeys[i];
      }

      return result;
    }

    /// <summary>
    /// 检查节点是否可以被优化释放掉
    /// </summary>
    private void CheckIfTerminal()
    {
      // 如果我不是叶节点，我是内部节点
      if (!this.IsLeaf)
      {
        for (int i = 0; i < this.Capacity + 1; i++)
        {
          // 存在一个已加载的子节点
          if (this._childNodes[i] != null)
          {
            // 则我不能被优化掉
            this.Tree.ForgetTerminalNode(this);
            return;
          }
        }
      }

      // 我可以被释放掉
      this.Tree.RecordTerminalNode(this);
    }

    /// <summary>
    /// 标示节点及其父节点均为脏节点
    /// </summary>
    private void Soil()
    {
      if (!this.IsDirty)
      {
        this.IsDirty = true;

        // 自己是脏节点，则父节点也是脏节点
        if (this.Parent != null)
        {
          this.Parent.Soil();
        }
      }
    }

    #endregion

    #region ToString

    /// <summary>
    /// 将节点转成字符串描述
    /// </summary>
    /// <param name="indent">缩进</param>
    /// <returns>节点的字符串描述</returns>
    public string ToText(string indent)
    {
      StringBuilder sb = new StringBuilder();

      string indentPlus = indent + "\t";

      sb.AppendLine(indent + "Node{");

      sb.Append(indentPlus + "IsLeaf = " + this.IsLeaf);
      sb.Append(", Capacity = " + this.Capacity);
      sb.Append(", Count = " + this.Count);
      sb.Append(", Dirty = " + this.IsDirty);
      sb.Append(", BlockNumber = " + this.BlockNumber);
      sb.Append(", ParentBlockNumber = " + (this.Parent == null ? "NULL" : this.Parent.BlockNumber.ToString(CultureInfo.InvariantCulture)));
      sb.Append(", PositionInParent = " + this.PositionInParent);
      sb.AppendLine();

      if (this.IsLeaf) // 如果是叶节点
      {
        for (int i = 0; i < this.Capacity; i++)
        {
          string key = this._childKeys[i];
          long value = this._childValues[i];
          if (key != null)
          {
            key = string.IsNullOrEmpty(key) ? "NULL" : key;
            sb.AppendLine(indentPlus + "[Key : " + key + ", Value : " + value + "]");
          }
        }
      }
      else // 如果是非叶节点
      {
        int count = 0;
        for (int i = 0; i < this.Capacity; i++)
        {
          string key = this._childKeys[i];
          long value = this._childValues[i];
          if (key != null)
          {
            key = string.IsNullOrEmpty(key) ? "NULL" : key;
            sb.AppendLine(indentPlus + "[Key : " + key + ", Value : " + value + "]");

            count++;
          }
        }

        for (int i = 0; i <= count; i++)
        {
          try
          {
            this.LoadNodeAtPosition(i);
            sb.Append(this._childNodes[i].ToText(indentPlus));
          }
          catch (BPlusTreeException ex)
          {
            sb.AppendLine(ex.Message);
          }
        }
      }

      sb.AppendLine(indent + "}");

      return sb.ToString();
    }

    public override string ToString()
    {
      return string.Format("PositionInParent[{0}], IsLeaf[{1}], Capacity[{2}], Count[{3}], BlockNumber[{4}], IsDirty[{5}]",
        PositionInParent, IsLeaf, Capacity, Count, BlockNumber, IsDirty);
    }

    #endregion
  }
}
