using System;
using System.Collections;
using System.Diagnostics;

namespace BPlusTreePractice
{
  public partial class BPlusTree
  {
    /// <summary>
    /// 获取或设置指定的键值对
    /// </summary>
    public long this[string key]
    {
      get
      {
        return (long)this.Get(key);
      }
      set
      {
        this.Set(key, value);
      }
    }

    /// <summary>
    /// 保存键值对
    /// </summary>
    /// <param name="key">键</param>
    /// <param name="value">值</param>
    public void Set(string key, long value)
    {
      // 验证给定的键是否合法
      if (!ValidateKey(key, this))
      {
        throw new BPlusTreeBadKeyValueException(
          string.Format("Null or too large key cannot be inserted into tree: {0}.", key));
      }

      // 指定根节点
      bool isRootInitialized = false;
      if (this.RootNode == null)
      {
        this.RootNode = BPlusTreeNode.MakeRoot(this, true);
        isRootInitialized = true;
      }

      // 每个新键值均由根节点开始插入
      string splitFirstKey;
      BPlusTreeNode splitNode;
      this.RootNode.Insert(key, (long)value, out splitFirstKey, out splitNode);

      // 发现根节点需要分割
      if (splitNode != null)
      {
        // 分割根节点，并二分构造一个新的根节点
        BPlusTreeNode oldRoot = this.RootNode;
        this.RootNode = BPlusTreeNode.BinaryRoot(oldRoot, splitFirstKey, splitNode, this);
        isRootInitialized = true;
      }

      // 是否需要将根节点写入新的块
      if (isRootInitialized)
      {
        this.RootNodeBlockNumber = this.RootNode.DumpToNewBlock();
      }

      // 检测在内存中的大小 // TODO
      this.ShrinkFootprint();
    }

    /// <summary>
    /// 检索键值对
    /// </summary>
    /// <param name="key">键</param>
    /// <returns>值</returns>
    public object Get(string key)
    {
      if (this.ContainsKey(key))
      {
        long valueFound = (long)0;
        this.RootNode.FindKey(key, out valueFound);
        return valueFound;
      }
      throw new BPlusTreeKeyNotFoundException(
        string.Format("Key not found {0}.", key));
    }

    /// <summary>
    /// 由树决定键的比较规则。
    /// 结果小于 0 则 left 小于 right，等于 0 则 left 等于 right，大于 0 则 left 大于 right。
    /// </summary>
    /// <param name="left">节点的键</param>
    /// <param name="right">节点的键</param>
    /// <returns>节点的键比较结果</returns>
    public int CompareKey(string left, string right)
    {
      return string.Compare(left, right, StringComparison.Ordinal);
    }

    /// <summary>
    /// 获取树中第一个键，也就是最小的键。
    /// </summary>
    /// <returns>树中第一个键，也就是最小的键</returns>
    public string FirstKey()
    {
      string firstKey = null;

      if (this.RootNode != null)
      {
        // 空是树中最小的键
        if (this.ContainsKey(""))
        {
          firstKey = "";
        }
        else
        {
          return this.RootNode.FindNextKey("");
        }

        this.ShrinkFootprint();
      }

      return firstKey;
    }

    /// <summary>
    /// 获取比指定的键稍大的键。如果无此键则返回空。
    /// </summary>
    /// <param name="afterThisKey">指定的键</param>
    /// <returns>比指定的键稍大的键</returns>
    public string NextKey(string afterThisKey)
    {
      if (afterThisKey == null)
      {
        throw new BPlusTreeBadKeyValueException("Cannot search null key.");
      }

      string nextKey = this.RootNode.FindNextKey(afterThisKey);

      this.ShrinkFootprint();

      return nextKey;
    }

    /// <summary>
    /// 判断树中是否存在此键。
    /// </summary>
    /// <param name="key">被查找的键</param>
    /// <returns>如果键存在则返回真，否则为假。</returns>
    public bool ContainsKey(string key)
    {
      if (key == null)
      {
        throw new BPlusTreeBadKeyValueException("Cannot search null key.");
      }

      bool isContainsKey = false;
      long valueFound = (long)0;
      if (this.RootNode != null)
      {
        isContainsKey = this.RootNode.FindKey(key, out valueFound);
      }

      this.ShrinkFootprint();

      return isContainsKey;
    }

    /// <summary>
    /// 删除指定的键和关联的数据项。如果键未找到则抛出异常。
    /// </summary>
    /// <param name="key">被删除的键</param>
    public void RemoveKey(string key)
    {
      if (this.RootNode == null)
      {
        throw new BPlusTreeKeyNotFoundException("Tree is empty, cannot remove.");
      }

      bool mergeMe;
      BPlusTreeNode root = this.RootNode;

      // 从根节点开始遍历删除
      root.Delete(key, out mergeMe);

      // 如果根节点不是一个叶节点，并且仅还有一个子节点，则重新设置根节点
      if (mergeMe && !this.RootNode.IsLeaf && this.RootNode.Count == 0)
      {
        this.RootNode = this.RootNode.FirstChild();
        this.RootNodeBlockNumber = this.RootNode.MakeAsRoot();
        root.Free();
      }
    }

    /// <summary>
    /// 设置一个参数，用于决定何时释放内存映射的块。
    /// </summary>
    /// <param name="limit">未序列化的叶节点的数量</param>
    public void SetFootprintLimit(int limit)
    {
      if (limit < 5)
      {
        throw new BPlusTreeException("Footprint limit less than 5 is too small.");
      }
      this.FootprintLimit = limit;
    }

    /// <summary>
    /// 提交更改，自上次提交后的所有更改持久化。
    /// </summary>
    public void Commit()
    {
      // 存储所有的更改
      if (this.RootNode != null)
      {
        this.RootNodeBlockNumber = this.RootNode.Invalidate(false);
      }

      // 提交新的根节点
      this.Stream.Flush();
      this.WriteHeader();
      this.Stream.Flush();

      // 更改已经被提交，但空间还没释放，现在释放空间
      ArrayList toFree = new ArrayList();
      foreach (DictionaryEntry d in this._freeBlocksOnCommit)
      {
        toFree.Add(d.Key);
      }
      if (toFree.Count > 0)
      {
        toFree.Sort();
        toFree.Reverse();
        foreach (object thing in toFree)
        {
          long blockNumber = (long)thing;
          this.ReclaimBlock(blockNumber);
        }
      }

      // 记载空闲列表头
      this.WriteHeader();
      this.Stream.Flush();

      this.ResetBookkeeping();
    }

    /// <summary>
    /// 丢弃自上次提交后的所有更改，恢复状态值上次提交的点。
    /// </summary>
    public void Abort()
    {
      // 释放已分配的资源
      ArrayList toFree = new ArrayList();
      foreach (DictionaryEntry d in this._freeBlocksOnAbort)
      {
        toFree.Add(d.Key);
      }
      if (toFree.Count > 0)
      {
        toFree.Sort();
        toFree.Reverse();
        foreach (object thing in toFree)
        {
          long blockNumber = (long)thing;
          this.ReclaimBlock(blockNumber);
        }
      }

      long freeHead = this.FreeBlockHeadNumber;

      // 重新读取头部
      this.ReadHeader();

      // 重新存储根节点
      if (this.RootNodeBlockNumber == StorageConstants.NullBlockNumber)
      {
        this.RootNode = null; // 什么也没提交
      }
      else
      {
        this.RootNode.LoadFromBlock(this.RootNodeBlockNumber);
      }

      this.ResetBookkeeping();
      this.FreeBlockHeadNumber = freeHead;

      // 记载空闲列表头
      this.WriteHeader();
      this.Stream.Flush();
    }

    /// <summary>
    /// 关闭并刷新流，而未进行 Commit 或 Abort。和 Abort 的区别是未使用空间将不可达。
    /// </summary>
    public void Shutdown()
    {
      if (this.Stream != null)
      {
        this.Stream.Flush();
        this.Stream.Close();
      }
    }

    /// <summary>
    /// 检测并尝试回收再利用不可达的空间。当一块空间被修改后未进行 Commit 或 Abort 操作可能导致空间不可达。
    /// </summary>
    /// <param name="correctErrors">如果为真则确认异常为可预知的，如果为假则当发生错误是抛出异常</param>
    public void Recover(bool correctErrors)
    {
      Hashtable visited = new Hashtable();

      if (this.RootNode != null)
      {
        // 查找所有可达的节点
        this.RootNode.SanityCheck(visited);
      }

      // 遍历空闲列表
      long freeBlockNumber = this.FreeBlockHeadNumber;
      while (freeBlockNumber != StorageConstants.NullBlockNumber)
      {
        if (visited.ContainsKey(freeBlockNumber))
        {
          throw new BPlusTreeException(
            string.Format("Free block visited twice {0}.", freeBlockNumber));
        }
        visited[freeBlockNumber] = (byte)BPlusTreeNodeIndicator.Free;
        freeBlockNumber = this.ParseFreeBlock(freeBlockNumber);
      }

      // 找出丢失的记录
      Hashtable missing = new Hashtable();
      long maxBlockNumber = this.BlockFile.NextBlockNumber();
      for (long i = 0; i < maxBlockNumber; i++)
      {
        if (!visited.ContainsKey(i))
        {
          missing[i] = i;
        }
      }

      // remove from missing any free-on-commit blocks
      foreach (DictionaryEntry thing in this._freeBlocksOnCommit)
      {
        long toBeFreed = (long)thing.Key;
        missing.Remove(toBeFreed);
      }

      // add the missing values to the free list
      if (correctErrors)
      {
        if (missing.Count > 0)
        {
          Debug.WriteLine("correcting " + missing.Count + " unreachable blocks");
        }

        ArrayList missingKeyList = new ArrayList();
        foreach (DictionaryEntry d in missing)
        {
          missingKeyList.Add(d.Key);
        }
        missingKeyList.Sort();
        missingKeyList.Reverse();

        foreach (object thing in missingKeyList)
        {
          long blockNumber = (long)thing;
          this.ReclaimBlock(blockNumber);
        }
      }
      else if (missing.Count > 0)
      {
        string blocks = "";
        foreach (DictionaryEntry thing in missing)
        {
          blocks += " " + thing.Key;
        }
        throw new BPlusTreeException(
          string.Format("Found {0} unreachable blocks : {1}.",
          missing.Count, blocks));
      }
    }
  }
}
