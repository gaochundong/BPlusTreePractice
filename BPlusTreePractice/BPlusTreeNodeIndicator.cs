
namespace BPlusTreePractice
{
  /// <summary>
  /// B+ 树节点类型（内部节点、叶节点、空闲节点）
  /// </summary>
  internal enum BPlusTreeNodeIndicator : byte
  {
    /// <summary>
    /// 内部节点
    /// </summary>
    Internal = 0,

    /// <summary>
    /// 叶节点
    /// </summary>
    Leaf = 1,

    /// <summary>
    /// 空闲节点
    /// </summary>
    Free = 2
  }
}
