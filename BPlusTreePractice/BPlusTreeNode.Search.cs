
namespace BPlusTreePractice
{
  public partial class BPlusTreeNode
  {
    /// <summary>
    /// 判断指定的键是否存在，如存在则返回对应的值
    /// </summary>
    /// <param name="compareKey">指定的键</param>
    /// <param name="valueFound">对应的值</param>
    /// <returns>指定的键是否存在</returns>
    public bool FindKey(string compareKey, out long valueFound)
    {
      valueFound = 0;
      BPlusTreeNode leaf;

      int position = this.FindAtOrNextPositionInLeaf(compareKey, out leaf, false);

      // 找到的位置如果超出了容量，则说明不存在
      if (position < leaf.Capacity)
      {
        string key = leaf._childKeys[position];
        if ((key != null) && this.Tree.CompareKey(key, compareKey) == 0)
        {
          valueFound = leaf._childValues[position];
          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// 找到指定键的下一个键
    /// </summary>
    /// <param name="compareKey">指定键</param>
    /// <returns>下一个键</returns>
    public string FindNextKey(string compareKey)
    {
      string foundKey = null;
      BPlusTreeNode leaf;

      int position = this.FindAtOrNextPositionInLeaf(compareKey, out leaf, true);

      if (position >= leaf.Capacity || leaf._childKeys[position] == null)
      {
        // 向右侧遍历
        BPlusTreeNode foundInLeaf;
        leaf.TraverseToFollowingKey(leaf.Capacity, out foundInLeaf, out foundKey);
      }
      else
      {
        foundKey = leaf._childKeys[position];
      }

      return foundKey;
    }

    /// <summary>
    /// 查找指定索引处的下一个键值，如果没有则遍历右子树，如果仍没找到则返回空
    /// </summary>
    /// <param name="atIndex">开始查找的索引</param>
    /// <param name="foundInLeaf">找到键值的叶节点</param>
    /// <param name="foundKey">找到的键值</param>
    private void TraverseToFollowingKey(int atIndex, out BPlusTreeNode foundInLeaf, out string foundKey)
    {
      foundInLeaf = null;
      foundKey = null;

      bool lookInParent = false;
      if (this.IsLeaf)
      {
        lookInParent = (atIndex >= this.Capacity) || (this._childKeys[atIndex] == null);
      }
      else
      {
        lookInParent = (atIndex > this.Capacity) || (atIndex > 0 && this._childKeys[atIndex - 1] == null);
      }

      if (lookInParent)
      {
        // 如果存在则一定在父节点的下一个孩子节点中
        if (this.Parent != null && this.PositionInParent >= 0)
        {
          this.Parent.TraverseToFollowingKey(this.PositionInParent + 1, out foundInLeaf, out foundKey);
          return;
        }
        else
        {
          // 键不存在
          return;
        }
      }

      if (this.IsLeaf)
      {
        // 在叶子节点中找到
        foundInLeaf = this;
        foundKey = this._childKeys[atIndex];
        return;
      }
      else
      {
        // 非叶节点，在子节点中找找
        if (atIndex == 0 || this._childKeys[atIndex - 1] != null)
        {
          BPlusTreeNode child = this.LoadNodeAtPosition(atIndex);
          child.TraverseToFollowingKey(0, out foundInLeaf, out foundKey);
        }
      }
    }

    /// <summary>
    /// 在节点中查找指定的键 
    /// </summary>
    /// <param name="compareKey">要查询的键</param>
    /// <param name="inLeaf">在这个叶节点中发现了</param>
    /// <param name="lookPastOnly">是否要找一个较大的</param>
    /// <returns>如果找到则返回在节点中的位置</returns>
    private int FindAtOrNextPositionInLeaf(string compareKey, out BPlusTreeNode inLeaf, bool lookPastOnly)
    {
      // 在当前节点中找到键应该在的位置
      int keyPosition = this.FindAtOrNextPosition(compareKey, lookPastOnly);

      // 如果自己是叶节点，那就好办了，直接就算找到了
      if (this.IsLeaf)
      {
        inLeaf = this;
        return keyPosition;
      }

      // 自己是内部节点，加载找到位置的子节点，在子节点中继续查找
      BPlusTreeNode child = this.LoadNodeAtPosition(keyPosition);
      return child.FindAtOrNextPositionInLeaf(compareKey, out inLeaf, lookPastOnly);
    }
  }
}
