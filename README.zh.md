# ztmz_pacenote 🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉

- [更新计划](#plan)。[待完成任务](https://gitee.com/ztmz/ztmz_pacenote/milestones)。欢迎提PR和Issue。
- [github 镜像](https://github.com/strawhatboy/ztmz_pacenote)
- 注意：购买尘埃拉力赛2.0时请务必购买 “年度版”，不要买本体！！！本体缺了大量内容，补买DLC又巨贵无比！！！年度版steam**经常打折**只要30元左右，等不及打折可以去淘宝购买。购买EA Sports™ WRC时也尽量买包含所有dlc的完整版，打折时甚至比买原版+DLC要便宜得多（背刺老玩家

#### 演示视频

- [【8岁半小孩用手动H档驾驶B组赛车获全球纪录排行榜第25名，中国第1。DiRT Rally 2（尘埃拉力赛2），汉考克山反向赛道，用时2分53秒85。】 ](https://www.bilibili.com/video/BV13G4y1Y7Wk)
- [理查德伯恩斯拉力赛wrc2017世界纪录](https://www.bilibili.com/video/BV1mG4y1H7or)

#### 友情链接

- [simRallyCn](https://www.simrallycn.top/)：模拟拉力中文社区

#### 截图

#### 简介

ZTMZ车队路书工具，可以播放Dirt Rally 1.0/2.0，EA Sports™ WRC，Richard Burns Rally(RallySimFans)游戏的路书，车队QQ群：207790761，路书交流群：697673264

#### 白嫖指南 (安装过程请保持网络处于连接状态)

1. 通过[下载地址](https://gitee.com/ztmz/ztmz_pacenote/releases)下载最新版的 `.exe`后缀的安装文件双击安装，注意另外两个 `Source code`的 `zip`包或者 `tar.gz`包是代码源文件，无需下载忽视即可；
2. 安装期间如果检测到系统中未安装 `dotnet8`运行时，安装程序会自动下载并安装，下载完毕可能会弹出UAC提示框，点击“是”即可，注意需要有人值守；
3. 安装完毕打开路书工具，选择对应的游戏，此时如果是第一次运行，一般会弹窗提示需要打开端口修改配置，点击“帮我打开”，然后**重启游戏**（一般重启游戏就行，保险起见可以连带路书工具一起重启）
4. 在游戏设置的语音选项(Audio)中关掉原版的路书声音
   1. DR2中是Speech选项拖到最左边，其他游戏也有对应的设置，找到修改
5. 开始体验

#### 各种链接

* 下载地址：[releases](https://gitee.com/ztmz/ztmz_pacenote/releases)
* 视频
  - [版本通用使用教程](https://www.bilibili.com/video/BV1oq4y1u7ua/) (置顶评论有进度条)
  - [\[2021-08-29 直播录像\]\[尘埃拉力赛2.0\] 录制路书阿根廷第一个图Las Juntas并拆分](https://www.bilibili.com/video/BV1yQ4y1178R/) (置顶评论有进度条)
  - [2.x版本演示视频 by bigboxx](https://www.bilibili.com/video/BV1jv411J7aL)
  - [2.x版本录制和播放使用教学（脚本路书） by 草帽不是猫](https://www.bilibili.com/video/BV1a64y1i7vs)
  - [1.x版本演示视频 by bigboxx](https://www.bilibili.com/video/BV1Kh411r7PX)
  - [1.x版本录制和播放使用教学（纯语音路书） by 草帽不是猫](https://www.bilibili.com/video/BV1Ev411n7v9)
  - [1.x版本录制和播放使用说明文档 by bigboxx](https://www.bilibili.com/read/cv12176546)

#### 文件目录说明 (默认位于 `%userprofile%/Documents/My Games/ZTMZClub_nextgen`)

* __codrivers__
  用来存放语音包，在该文件夹下新建文件夹来创建新的语音包，可参考默认语音包的格式
* __lang__
  多语言文件，可以在此处对照其他文件，新建新的语言支持
* __profiles__
  用来存放路书，里面默认有个 `default`文件夹，可以新建其他文件夹用来存放另一个版本的路书，`default`文件夹中的 `pacenote`文件为路书脚本，以地图名命名的一些文件夹中存放的是纯语音路书
* __games__
  用来存放各个游戏的配置以及多语言文件
* __Python38__ (仅开发版)
  Python38的运行环境
* __speech_model__  (仅开发版)
  Vosk语音识别模型
* **dashboards**
  仪表盘文件，可以通过lua脚本创建仪表盘
* **fonts**
  字体文件，存放可被仪表盘使用的字体
* **logs**
  按天存放日志文件，工具出现问题时可通过日志文件中的错误定位到具体问题

#### 各模块简介(src目录下)

* __OnlyR.Core__
  从github上直接“借鉴”的声音录制代码[AntonyCorbett/OnlyR](https://github.com/AntonyCorbett/OnlyR)
* __ZTMZ.PacenoteTool.WpfGUI__
  主程序入口，整个WPF界面的绘制逻辑
* __ZTMZ.PacenoteTool.Base__
  基础模块：配置文件的加载与保存，语音包定义，路书定义，日志管理，语音特效等
* **ZTMZ.PacenoteTool.Base.UI**
  基础UI模块：悬浮窗，通用UI模块，主题颜色样式等
* **ZTMZ.PacenoteTool.Core**
  核心模块：语音包加载，路书播放逻辑等
* **ZTMZ.PacenoteTool.Console**
  无UI版本主程序入口：用于启动无UI版本的路书工具
* **ZTMZ.PacenoteTool.I18N**
  多语言模块：存放多语言文件
* **ZTMZ.PacenoteTool.Codemasters**
  CM相关游戏定义：尘埃拉力赛1.0/2.0，EA Sports™ WRC
* **ZTMZ.PacenoteTool.RBR**
  RBR游戏定义，含RBR路书定义和ZTMZ的映射表

#### 如何贡献代码

1. Fork本项目并使用git下载源码
2. 安装[dotnet8 SDK](https://dotnet.microsoft.com/en-us/download)
3. 使用Visual Studio 2022或JetBrains Rider通过src目录的ZTMZ.PacenoteTool.sln文件打开本项目，或者直接用VSCode打开本项目文件夹
4. 运行 `.\build\build.bat`命令编译项目，运行 `.\build\builddebug.bat`编译调试版本
5. 将之前最新版本的路书工具安装后，从 `%userprofile%\My Games\ZTMZClub_nextgen\` 目录中，将 `codrivers`和 `profiles`目录拷贝到项目的 `bin\Release\net8.0-windows\`目录下
6. 下载安装[Inno Setup](https://jrsoftware.org/download.php/is.exe)，并设置系统的Path环境变量，增加 `ICSS.exe`文件所在的目录，默认安装应该是 `C:\Program Files (x86)\Inno Setup 6`
7. 从[GitHub](https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation)上安装Inno Setup的中文语言包。具体为下载 `ChineseSimplified.isl`文件放到 `C:\Program Files (x86)\Inno Setup 6\Languages`目录下
8. 运行 `.\package.bat`进行打包，可以在 `Output`目录找到打包好的安装包
9. 做出修改调试运行无误后，打包项目为exe包并本地安装测试通过后，将代码推送至gitee，再创建PR到本项目

#### 如何贡献语音包

1. 在主界面语音包选项卡中，点击默认语音包，查看所需的所有路书标记，并参考默认语音包的文件结构创建语音包
2. 非常建议使用ogg格式声音文件，而不是mp3！！！！mp3有bug！！！连wmv格式都比mp3好！！！！

#### 项目计划

`<a name='plan'></a>`

| 版本号               | 时间段              | 更新内容                                                                                                                                                                                                         |
| -------------------- | ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2.3.2                | 已发布              | 丰富紫藤语音包的替代方案<br />修复过低的播放设备缓冲延迟导致的部分设备播放卡顿的问题                                                                                                                             |
| ~~2.3.3~~ <br />2.4 | 已发布              | 收录全部路书<br />增加路书工具和脚本工具通信协作<br />对游戏内语音进行语音识别成文字<br />再自动生成脚本路书到脚本工具中                                                                                         |
| ~~2.4~~ <br />2.5   | 已发布              | 使用 `gameoverlay.net`库实现在游戏内右上角显示相关信息<br />1. 当前地图路书加载状态<br />2. 路书类型<br />3. 路书作者<br />4. 所使用的语音包(在脚本路书类型时)<br />船深的用户界面<br />可以自动保存的用户设置 |
| 2.5.1                | 已发布              | 特别多，见[更新记录](https://gitee.com/ztmz/ztmz_pacenote/raw/master/ZTMZ.PacenoteTool/更新记录.txt)                                                                                                                |
| ~~2.5~~             | ~~2022-01-14之后~~ | ~~增加路书工具和脚本工具通信协作<br />对游戏内语音进行语音识别成文字<br />再自动生成脚本路书到脚本工具中~~                                                                                                      |
| 2.6                  | 已发布              | 增加语音包制作工具<br />增加了动态语速和动态紧张感特效                                                                                                                                                           |
| 2.6.1-2.6.6          | 已发布 (2022-05)    | 修复部分bug，增加部分语音包<br />增加了悬浮窗的仪表盘功能<br />增加了用于分析和改进工具使用的Google Analytics<br />增加了mesa的英文语音包<br />修复了很多赛道路书的错误                                          |
| 2.7                  | 2022-08             | 增加对多种游戏的支持（Dirt Rally 1.0，RBR/Richard Burns Rally - RSF版）                                                                                                                                          |
| 3.0                  | 2024-11             | 增加对EA Sports WRC/Rally 游戏的支持，重置简洁版界面                                                                                                                                                             |
