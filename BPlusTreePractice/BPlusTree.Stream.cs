using System;
using System.IO;

namespace BPlusTreePractice
{
  public partial class BPlusTree
  {
    /// <summary>
    /// 从指定的流初始化树
    /// </summary>
    /// <param name="fromFile">指定的流</param>
    /// <param name="seekStart">流起始查询点</param>
    /// <param name="keyLength">键长度</param>
    /// <param name="nodeCapacity">节点容量</param>
    /// <returns>树</returns>
    public static BPlusTree InitializeInStream(Stream fromFile, long seekStart, int keyLength, int nodeCapacity)
    {
      if (fromFile == null)
        throw new ArgumentNullException("fromFile");

      if (fromFile.Length > seekStart)
      {
        throw new BPlusTreeException("Cannot initialize tree inside written area of stream.");
      }

      BPlusTree tree = new BPlusTree(fromFile, seekStart, keyLength, nodeCapacity, (byte)1);
      tree.WriteHeader();
      tree.BlockFile = BlockFile.InitializeInStream(
        fromFile, seekStart + HeaderSize, StorageConstants.BlockFileHeaderPrefix, tree.BlockSize);

      return tree;
    }

    /// <summary>
    /// 从指定的流初始化树
    /// </summary>
    /// <param name="fromFile">指定的流</param>
    /// <param name="seekStart">流起始查询点</param>
    /// <returns>树</returns>
    public static BPlusTree SetupFromExistingStream(Stream fromFile, long seekStart)
    {
      if (fromFile == null)
        throw new ArgumentNullException("fromFile");

      BPlusTree tree = new BPlusTree(fromFile, seekStart, 100, 7, (byte)1);
      tree.ReadHeader();
      tree.BlockFile = BlockFile.SetupFromExistingStream(
        fromFile, seekStart + HeaderSize, StorageConstants.BlockFileHeaderPrefix);

      if (tree.BlockFile.BlockSize != tree.BlockSize)
      {
        throw new BPlusTreeException("Inner and outer block sizes should match.");
      }

      if (tree.RootNodeBlockNumber != StorageConstants.NullBlockNumber)
      {
        tree.RootNode = BPlusTreeNode.MakeRoot(tree, true);
        tree.RootNode.LoadFromBlock(tree.RootNodeBlockNumber);
      }

      return tree;
    }
  }
}
