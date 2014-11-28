using System;

namespace BPlusTreePractice
{
  public partial class BPlusTreeNode
  {
    #region Delete

    /// <summary>
    /// 删除指定键的记录
    /// </summary>
    /// <param name="key">指定键</param>
    /// <param name="mergeMe">如果为真则意味着删除键后节点已经小于1/2满，需要合并</param>
    /// <returns>返回节点中最小的键，如无变化则返回空</returns>
    public string Delete(string key, out bool mergeMe)
    {
      // 如果是叶节点，则调用删除叶节点逻辑
      if (this.IsLeaf)
      {
        return DeleteLeaf(key, out mergeMe);
      }

      // 假设我是不需要被合并的
      mergeMe = false;

      // 我是内部节点，找到指定键的位置
      int deletePosition = this.FindAtOrNextPosition(key, false);

      // 加载要被删除位置的子节点
      BPlusTreeNode deleteChildNode = LoadNodeAtPosition(deletePosition);

      // 从子节点中删除键
      bool isChildNodeNeedMerge;
      string deletedChildKey = deleteChildNode.Delete(key, out isChildNodeNeedMerge);

      // 删除完毕，当前节点为脏节点
      this.Soil();

      // 发现子节点的最小键与被删除的键相同，则认为子节点已经没用了
      string result = null;
      if (deletedChildKey != null && this.Tree.CompareKey(deletedChildKey, key) == 0)
      {
        if (this.Capacity > 3)
        {
          throw new BPlusTreeException(
            string.Format("Deletion returned delete key for too large node size: {0}.",
            this.Capacity));
        }

        // 判断删除的是头或尾
        if (deletePosition == 0)
        {
          result = this._childKeys[deletePosition];
        }
        else if (deletePosition == this.Capacity)
        {
          this._childKeys[deletePosition - 1] = null;
        }
        else
        {
          this._childKeys[deletePosition - 1] = this._childKeys[deletePosition];
        }

        if (result != null && this.Tree.CompareKey(result, key) == 0)
        {
          this.LoadNodeAtPosition(1);
          result = this._childNodes[1].LeastKey();
        }

        // 这个子节点彻底没用了，释放其空间
        deleteChildNode.Free();

        // 节点中键位置右侧全部左移 1 位
        OverwriteDeletePosition(deletePosition);

        // 如果节点的使用率小于一半，则需要合并节点
        if (this.Count < this.Capacity / 2)
        {
          mergeMe = true;
        }

        // 重置子节点的父节点
        this.ResetAllChildrenParent();

        return result;
      }

      if (deletePosition == 0)
      {
        result = deletedChildKey;
      }
      else if (deletedChildKey != null && deletePosition > 0)
      {
        if (this.Tree.CompareKey(deletedChildKey, key) != 0)
        {
          this._childKeys[deletePosition - 1] = deletedChildKey;
        }
      }

      // 如果子节点需要合并
      if (isChildNodeNeedMerge)
      {
        int leftIndex, rightIndex;
        BPlusTreeNode leftNode, rightNode;
        string keyBetween;

        if (deletePosition == 0)
        {
          // 和右侧兄弟节点合并
          leftIndex = deletePosition;
          rightIndex = deletePosition + 1;
          leftNode = deleteChildNode;
          rightNode = this.LoadNodeAtPosition(rightIndex);
        }
        else
        {
          // 和左侧兄弟节点合并
          leftIndex = deletePosition - 1;
          rightIndex = deletePosition;
          leftNode = this.LoadNodeAtPosition(leftIndex);
          rightNode = deleteChildNode;
        }

        keyBetween = this._childKeys[leftIndex];

        // 合并节点
        string rightLeastKey;
        bool isDeleteRight;
        MergeInternal(leftNode, keyBetween, rightNode, out rightLeastKey, out isDeleteRight);

        // 是否需要删除右节点
        if (isDeleteRight)
        {
          for (int i = rightIndex; i < this.Capacity; i++)
          {
            this._childKeys[i - 1] = this._childKeys[i];
            this._childValues[i] = this._childValues[i + 1];
            this._childNodes[i] = this._childNodes[i + 1];
          }
          this._childKeys[this.Capacity - 1] = null;
          this._childValues[this.Capacity] = StorageConstants.NullBlockNumber;
          this._childNodes[this.Capacity] = null;

          this.ResetAllChildrenParent();

          rightNode.Free();

          // 当前节点还需要再合并吗
          if (this.Count < this.Capacity / 2)
          {
            mergeMe = true;
          }
        }
        else
        {
          this._childKeys[rightIndex - 1] = rightLeastKey;
        }
      }

      return result;
    }

    /// <summary>
    /// 删除指定键的记录，当前节点是叶节点的情况。
    /// </summary>
    /// <param name="key">指定键</param>
    /// <param name="mergeMe">如果为真则意味着删除键后节点已经小于1/2满，需要合并</param>
    /// <returns>返回节点中最小的键，如无变化则返回空</returns>
    public string DeleteLeaf(string key, out bool mergeMe)
    {
      // 假设我是不需要被合并的
      mergeMe = false;

      // 如果自己不是叶节点，那一定是用错了方法
      if (!this.IsLeaf)
      {
        throw new BPlusTreeException("Bad call to delete leaf, this is not a leaf.");
      }

      // 先找到指定的键的位置
      bool isKeyFound = false;
      int deletePosition = 0;
      foreach (string childKey in this._childKeys)
      {
        if (childKey != null && this.Tree.CompareKey(childKey, key) == 0)
        {
          isKeyFound = true;
          break;
        }
        deletePosition++;
      }
      if (!isKeyFound)
      {
        throw new BPlusTreeKeyNotFoundException(
          string.Format("Cannot delete missing key: {0}.", key));
      }

      // 这个节点即将被修改，先标记为脏节点
      this.Soil();

      // 将指定键位置右侧的数据左移 1 位，覆盖掉指定键，也就算删除了
      for (int i = deletePosition; i < this.Capacity - 1; i++)
      {
        this._childKeys[i] = this._childKeys[i + 1];
        this._childValues[i] = this._childValues[i + 1];
      }
      this._childKeys[this.Capacity - 1] = null;

      // 如果节点的使用率小于一半，小于 1/2 满，则需要合并节点
      if (this.Count < this.Capacity / 2)
      {
        mergeMe = true;
      }

      // 返回被删后的最小键
      string result = null;
      if (deletePosition == 0)
      {
        result = this._childKeys[0];
        if (result == null)
        {
          result = key; // 被删除的键
        }
      }

      return result;
    }

    /// <summary>
    /// 节点中键位置右侧全部左移 1 位
    /// </summary>
    /// <param name="deletePosition">被删除的键位置</param>
    private void OverwriteDeletePosition(int deletePosition)
    {
      // 节点中键位置右侧全部左移 1 位
      for (int i = deletePosition; i < this.Capacity - 1; i++)
      {
        this._childKeys[i] = this._childKeys[i + 1];
        this._childValues[i] = this._childValues[i + 1];
        this._childNodes[i] = this._childNodes[i + 1];
      }
      this._childKeys[this.Capacity - 1] = null;

      if (deletePosition < this.Capacity)
      {
        this._childValues[this.Capacity - 1] = this._childValues[this.Capacity];
        this._childNodes[this.Capacity - 1] = this._childNodes[this.Capacity];
      }
      this._childNodes[this.Capacity] = null;
      this._childValues[this.Capacity] = StorageConstants.NullBlockNumber;
    }

    /// <summary>
    /// 获取最小的键
    /// </summary>
    /// <returns>最小的键</returns>
    private string LeastKey()
    {
      string key = null;
      if (this.IsLeaf)
      {
        key = this._childKeys[0];
      }
      else
      {
        this.LoadNodeAtPosition(0);
        key = this._childNodes[0].LeastKey();
      }

      if (key == null)
      {
        throw new BPlusTreeException("No least key found.");
      }

      return key;
    }

    #endregion

    #region Merge

    /// <summary>
    /// 合并内部节点，当节点的使用率不足 50% 时，则需要合并
    /// </summary>
    /// <param name="left">左节点</param>
    /// <param name="keyBetween">左右节点的中间键</param>
    /// <param name="right">右节点</param>
    /// <param name="rightLeastKey">合并后的键的最小值</param>
    /// <param name="canDeleteRightNode">是否可以删除右节点</param>
    public static void MergeInternal(BPlusTreeNode left, string keyBetween, BPlusTreeNode right, out string rightLeastKey, out bool canDeleteRightNode)
    {
      if (left == null)
        throw new ArgumentNullException("left");
      if (right == null)
        throw new ArgumentNullException("right");

      rightLeastKey = null; // only if DeleteRight

      // 合并叶节点
      if (left.IsLeaf || right.IsLeaf)
      {
        if (!(left.IsLeaf && right.IsLeaf))
        {
          throw new BPlusTreeException("Cannot merge leaf with non-leaf.");
        }

        // 合并子节点
        MergeLeaves(left, right, out canDeleteRightNode);

        rightLeastKey = right._childKeys[0];

        return;
      }

      // 合并非叶节点
      canDeleteRightNode = false;

      if (left._childValues[0] == StorageConstants.NullBlockNumber || right._childValues[0] == StorageConstants.NullBlockNumber)
      {
        throw new BPlusTreeException("Cannot merge empty non-leaf with non-leaf.");
      }

      string[] allKeys = new string[left.Capacity * 2 + 1];
      long[] allValues = new long[left.Capacity * 2 + 2];
      BPlusTreeNode[] allNodes = new BPlusTreeNode[left.Capacity * 2 + 2];

      // 拷贝左节点的数据
      int index = 0;
      allValues[0] = left._childValues[0];
      allNodes[0] = left._childNodes[0];
      for (int i = 0; i < left.Capacity; i++)
      {
        if (left._childKeys[i] == null)
        {
          break;
        }

        allKeys[index] = left._childKeys[i];
        allValues[index + 1] = left._childValues[i + 1];
        allNodes[index + 1] = left._childNodes[i + 1];

        index++;
      }

      // 拷贝中间键
      allKeys[index] = keyBetween;
      index++;

      // 拷贝右节点的数据
      allValues[index] = right._childValues[0];
      allNodes[index] = right._childNodes[0];
      int rightCount = 0;
      for (int i = 0; i < right.Capacity; i++)
      {
        if (right._childKeys[i] == null)
        {
          break;
        }

        allKeys[index] = right._childKeys[i];
        allValues[index + 1] = right._childValues[i + 1];
        allNodes[index + 1] = right._childNodes[i + 1];
        index++;

        rightCount++;
      }

      // 如果数量小于左节点的能力，则右节点可以删除掉
      if (index <= left.Capacity)
      {
        // it will all fit in one node
        canDeleteRightNode = true;

        for (int i = 0; i < index; i++)
        {
          left._childKeys[i] = allKeys[i];
          left._childValues[i] = allValues[i];
          left._childNodes[i] = allNodes[i];
        }

        left._childValues[index] = allValues[index];
        left._childNodes[index] = allNodes[index];

        left.ResetAllChildrenParent();
        left.Soil();

        right.Free();

        return;
      }

      // otherwise split the content between the nodes
      left.Clear();
      right.Clear();
      left.Soil();
      right.Soil();

      int leftContent = index / 2;
      int rightContent = index - leftContent - 1;

      rightLeastKey = allKeys[leftContent];

      int outputIndex = 0;
      for (int i = 0; i < leftContent; i++)
      {
        left._childKeys[i] = allKeys[outputIndex];
        left._childValues[i] = allValues[outputIndex];
        left._childNodes[i] = allNodes[outputIndex];
        outputIndex++;
      }

      rightLeastKey = allKeys[outputIndex];

      left._childValues[outputIndex] = allValues[outputIndex];
      left._childNodes[outputIndex] = allNodes[outputIndex];
      outputIndex++;

      rightCount = 0;
      for (int i = 0; i < rightContent; i++)
      {
        right._childKeys[i] = allKeys[outputIndex];
        right._childValues[i] = allValues[outputIndex];
        right._childNodes[i] = allNodes[outputIndex];
        outputIndex++;

        rightCount++;
      }

      right._childValues[rightCount] = allValues[outputIndex];
      right._childNodes[rightCount] = allNodes[outputIndex];

      left.ResetAllChildrenParent();
      right.ResetAllChildrenParent();
    }

    /// <summary>
    /// 合并叶节点，当节点的使用率不足 50% 时，则需要合并
    /// </summary>
    /// <param name="left">左节点</param>
    /// <param name="right">右节点</param>
    /// <param name="canDeleteRightNode">是否可以删除右节点</param>
    public static void MergeLeaves(BPlusTreeNode left, BPlusTreeNode right, out bool canDeleteRightNode)
    {
      if (left == null)
        throw new ArgumentNullException("left");
      if (right == null)
        throw new ArgumentNullException("right");

      canDeleteRightNode = false;

      string[] allKeys = new string[left.Capacity * 2];
      long[] allValues = new long[left.Capacity * 2];

      int index = 0;
      for (int i = 0; i < left.Capacity; i++)
      {
        if (left._childKeys[i] == null)
        {
          break;
        }
        allKeys[index] = left._childKeys[i];
        allValues[index] = left._childValues[i];
        index++;
      }

      for (int i = 0; i < right.Capacity; i++)
      {
        if (right._childKeys[i] == null)
        {
          break;
        }
        allKeys[index] = right._childKeys[i];
        allValues[index] = right._childValues[i];
        index++;
      }

      // 如果左节点的容量足够，则可删除右节点
      if (index <= left.Capacity)
      {
        canDeleteRightNode = true;

        left.Clear();

        for (int i = 0; i < index; i++)
        {
          left._childKeys[i] = allKeys[i];
          left._childValues[i] = allValues[i];
        }

        left.Soil();
        right.Free();

        return;
      }

      // 左节点的容量不够了
      left.Clear();
      right.Clear();
      left.Soil();
      right.Soil();

      int rightContent = index / 2;
      int leftContent = index - rightContent;
      int newIndex = 0;
      for (int i = 0; i < leftContent; i++)
      {
        left._childKeys[i] = allKeys[newIndex];
        left._childValues[i] = allValues[newIndex];
        newIndex++;
      }
      for (int i = 0; i < rightContent; i++)
      {
        right._childKeys[i] = allKeys[newIndex];
        right._childValues[i] = allValues[newIndex];
        newIndex++;
      }
    }

    #endregion
  }
}
