+ 使用方法
	- 具体使用
		* ObjectSaveRead Save Read 		对object存储读取
		* ByteSaveRead Save Read   		对byte[]存储读取
		* FileEncrypt Encrypt Decrypt 	对文件加密，Progress完成多线程中，在主线程中进行工作进度汇报。汇报频率100毫秒或者1mb完成或者阶段完成
		* FileArchive Compress Extract 	压缩与解压文件，Progress完成多线程中，在主线程中尽心工作进度汇报。汇报频率100毫秒或者1mb完成或者阶段完成
	- 底层加密校验
		* ProtectedFile Save Read 底层流式加密校验，安全，控制内存消耗

+ 原理
	- 序列化
		* 使用Newtonsoft.Json，unity只支持这个版本。 

			string content = JsonConvert.SerializeObject(data); // 这种写法会在内存里留content
			JsonConvert.DeserializeObject<T>(content); // 这种写法也会在内存里残留content
		
	- File
		* 直接读写全部

	- Stream
		* 提供流式读写的能力，1、文件  2、文件指针  3、内存缓存区间指针
		* 需要注意的是，读写一次，文件指针会自动移动
		* 在操作系统底层上，读写一段和读写全部，没有任何区别，反正都是给的操作系统地址其实位置

	- FileStream
		* 只能处理byte
		* FileStream fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1024 * 64)
		* 写入缓存
		* 提前设置文件大小，使文件碎片化性减小

				fs.SetLength(finalSize);
				fs.Position = 0;

		* dispose close 效果一样，调用其中任意一个都可以释放资源
		* 推荐使用这种方式调用 using(FileStream fs = new FileStream()){fs.write()}，好处是报错也会释放文件资源
		* 初始化 

				new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1024 * 64)
				+ FileMode
				+ FileAccess定义自己的权限
				+ FileShare 定义其他程序使用这个文件的权限
				+ 1024 * 64 定义缓存大小，小的写入调用，先进这个缓存，缓存满了再调用操作系统写入，如果一次写入的大于这个缓存就不进入这个缓存了，可以减少io调用次数

		* 小文件，通常64kb就好。 大文件几十兆以上，通常1MB就好，比1MB大也没啥用，io操作系统也会分块写入
		* 缓存使用ArrayPool<byte>.Shared.Rent()方式创建

	- SteamWrite/StreamReader
		* 在FileStream基础上，只能处理文字
		* 也有buffSize, 这个buffSize是字符串的长度,攒够了再调用底层的Stream，而底层的Stream又有buffSize

	- byte数组，分配内存地址方案
		* 64kb以下，低频，则由托管管理，托管大文件判定阈值是 85kb
		* 64kb以上，或者高频， 使用ArrayPool<byte>.Shared.Rent()

	- 加密
		* 流式加密

	- HMAC校验
		* 目的防止文件篡改
