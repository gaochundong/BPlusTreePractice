using System;
using System.IO;

namespace BPlusTreePractice
{
  class Program
  {
    static void Main(string[] args)
    {
      // 指定磁盘文件位置
      string treeFileName = @"E:\BPlusTree_" + DateTime.Now.ToString(@"yyyyMMddHHmmssffffff") + ".data";
      Stream treeFileStream = new FileStream(treeFileName, FileMode.CreateNew, FileAccess.ReadWrite);

      // 初始化 B+ 树，固定长度字符串为键，映射至长整形
      int keyLength = 64;
      int nodeCapacity = 2;
      BPlusTree tree = BPlusTree.InitializeInStream(treeFileStream, 0L, keyLength, nodeCapacity);

      // 插入 0 到 7 共 8 个键值对
      for (int i = 0; i < 8; i++)
      {
        tree.Set(i.ToString(), (long)(i * 1000)); // Key 是字符串，Value 是 long 类型
      }

      // 将 B+ 树输出到命令行
      Console.WriteLine(tree.ToText());

      // 获取指定的键值对
      Console.WriteLine();
      Console.WriteLine(string.Format("Tree's first key is {0}.", tree.FirstKey()));
      Console.WriteLine(string.Format("Check key {0} exists {1}.", "3", tree.ContainsKey("3")));
      Console.WriteLine(string.Format("{0}'s next key is {1}.", "6", tree.NextKey("6")));
      Console.WriteLine(string.Format("Get key {0} with value {1}.", "2", tree.Get("2")));
      Console.WriteLine(string.Format("Index key {0} with value {1}.", "4", tree["4"]));
      Console.WriteLine();

      // 删除键值对
      tree.RemoveKey("6");
      Console.WriteLine(tree.ToText());

      Console.ReadKey();
    }
  }
}
