 **注意：** 一般情况下 **只有遇到问题时才参考本疑难解答** ，没有遇到问题不要随意尝试本文的各种解决方案，可能会适得其反。

**Q:** 工具在安装的时候卡在下载所需组件那里不动了怎么回事？
**A:** 可能网络环境和微软的服务器不通畅，先 **手动下载安装运行库** （[https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-6.0.5-windows-x64-installer](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-6.0.5-windows-x64-installer)），再运行工具的安装程序即可。如果手动也无法下载，那么可以到路书群里从群文件里下载。
如果你使用的是**Windows 7 SP1 x64操作系统**，那么在安装完上面的东西之后，还需要再安装[https://aka.ms/vs/16/release/vc_redist.x64.exe](https://aka.ms/vs/16/release/vc_redist.x64.exe)和[https://www.microsoft.com/zh-CN/download/details.aspx?id=47442](https://www.microsoft.com/zh-CN/download/details.aspx?id=47442)这两个东西之后，才应该可以正常安装路书工具。

**Q:** 我在使用Windows 7 SP1 x64 操作系统，貌似无法安装怎么办？
**A:** 参见上一个回答。

**Q:** 工具在下载时浏览器提示文件不安全怎么办？
**A:** 浏览器中在下载的地方看看文件右键点击或者其他有没有菜单可以选择 **“保留”** 的，保留该文件即可

**Q:** 下载的文件被杀毒软件报毒（勒索病毒）怎么回事？
**A:** 哪家的杀毒软件 **竟敢报毒** ？？？（工具源代码公布在gitee上，100%无毒）

**Q:** 下载的压缩包解压之后，双击了“双击运行.bat”怎么没反应？
**A:** 可能下载错了，下载的是 **源代码的压缩包** ，需要在[https://gitee.com/ztmz/ztmz_pacenote/releases](https://gitee.com/ztmz/ztmz_pacenote/releases) **下载标准版的exe文件**双击运行才能安装

**Q:** 工具下载下来运行的时候为什么出来了一个提示框，只有一个 “关闭” 按钮，关了之后就没反应了？
**A:** 在提示框上找找看可有 **“更多信息” “详细信息”** 之类的蓝色小字，点击之后会多出一个 “仍要运行” 的按钮，点击即可

**Q:** 工具安装时提示"安装程序不能创建目录xxxxxxxxx，错误2：系统找不到指定的文件"怎么回事？
**A:** 离了大谱，可能系统的文件夹权限出了问题。尝试在 windows 自带的安全中心 (Windows Security)->病毒与防护设置 (Virus & threat protection settings)->文件夹限制访问 (Controlled folder access)，查看是否开启了文件夹限制访问，如果开启尝试关闭后再重新安装

**Q:** 工具安装完成后，在运行的时候提示 "To run this application, you must install .NET Desktop Runtime 6.0.5 (x64), Would you like to download it now? 是(Y) | 否(N)"是怎么回事？
**A:** 离了大谱，不可能发生的事发生了。简单快捷的方法是**重新安装**。原因可能是在安装了工具之后又安装了其他低版本的dotnet6 运行时，导致当前工具失效。重新安装也不行？？？手动下载安装运行库（[https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-6.0.5-windows-x64-installer](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-6.0.5-windows-x64-installer)）。还不行？？？是不是下载错文件了？

**Q:** 为什么工具没反应？
**A:** 工具只有在**游戏中拉了手刹倒计时开始时及之后**才会开始有反应

**Q:** 为什么有的地图没有路书？
**A:** 目前DirtRally2.0中只有拉力地图有路书，**RX（RALLY CROSS）地图是没有路书**的

**Q:** 为什么录视频或者直播的时候很卡（帧率很低）？
**A:** 因为万恶的 **悬浮窗，所用的技术会引起录视频和直播时帧率大幅下降** ，目前的解决办法是在主界面的最下面的“开启悬浮窗”右边的勾给勾掉（关闭悬浮窗）

**Q:** 为什么工具上的各种数据在开始游戏之后没有反应？
**A:** 这基本上就是端口配置的问题。如果是第一次启动工具和游戏，可能未能使游戏的udp端口修改生效，这种情况重新启动一下游戏就行；如果是使用了和游戏不同的端口号，重启工具时会有提示框提醒，这时应当有在同时使用simhub并作出正确的配置。

**Q:** 我在使用速魔软件，需要哪些配置？
**A:** 可以询问速魔客服，可以在客服的指导下进行 SIMHUB 相关的配置。在 SIMHUB 配置完毕后，只需在 SIMHUB 转发中新加一条转发规则到任意端口大于 20000 的端口，如 20999，然后再把路书工具中的监听端口改为相同的即可（此例子为 20999）

**Q:** 为什么窗口模式下屏幕左边会多出一堆跳动的英文和数字？
**A:** 可能是在设置里不小心打开了最下面一个 **调试** 选项，关掉就可以了

**Q:** 为什么工具上的游戏状态还有各种数据在游戏的时候有变化，但是却没有声音？
**A:** 首先点击工具上的**播放示例声音**的按钮，检查是否有声音，顺带查看播放设备是否是当前可以发出声音的设备。如果播放设备列表里没有你的设备（比如蓝牙耳机），可能是因为先启动的工具，后连接的耳机，所以工具没能够意识到这个新连接的耳机。解决办法是保持耳机处于连接状态重新启动工具

**Q:** 为什么工具的语音和游戏的语音同时出现混在一起？
**A:** 把游戏的设置->声音->**演讲** （Audio-> **Speech** ）拉到最左侧就可以关闭游戏内的原声路书

**Q:** 工具可以用在WRC/DR1/D4上吗？
**A:** WRC目前的版本不太可能支持；DR1（尘埃拉力赛1.0）可以支持但是需要对代码做重构，有一定的工作量，所以目前也不支持；dirt1~5由于玩家数量较少且偏向娱乐，所以也不支持。未来**EA Sports 2023年的WRC**游戏可能会提供支持

**Q:** 虽然是中文了，但是仍然听不懂或者听不清在说什么怎么办？
**A:** 多加练习，b站搜索拉力路书有些教程可以学习到一些路书的知识

**Q:** 2.7.2 版本程序在启动时提示UnknownException，其中详细信息中包含Could find a part of the path: ... Tracks.ini 是怎么回事？
**A:** 因为曾经安装了RBR RSF，但是后来又通过删除文件夹的形式删除了，但注册表没有删除，诱发的加载rbr失败的bug，可以通过删除注册表中的 HKEY_LOCAL_MACHINE\Software\Wow6432Node\Rallysimfans RBR 文件夹解决。详情见：[issue#I5PGBW](https://gitee.com/ztmz/ztmz_pacenote/issues/I5PGBW)

未完待续。。。
