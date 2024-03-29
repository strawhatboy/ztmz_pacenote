# ztmz_pacenote 🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉

- [更新计划](#plan)。[待完成任务](https://gitee.com/ztmz/ztmz_pacenote/milestones/176244)。欢迎提PR和Issue。
- [github 镜像](https://github.com/strawhatboy/ztmz_pacenote)
- 注意：购买尘埃拉力赛2.0时请务必购买 “年度版”，不要买本体！！！本体缺了大量内容，补买DLC又巨贵无比！！！年度版steam__经常打折__只要30元左右，等不及打折可以去淘宝购买。

#### 演示视频
- [【8岁半小孩用手动H档驾驶B组赛车获全球纪录排行榜第25名，中国第1。DiRT Rally 2（尘埃拉力赛2），汉考克山反向赛道，用时2分53秒85。】 ](https://www.bilibili.com/video/BV13G4y1Y7Wk)
- [理查德伯恩斯拉力赛wrc2017世界纪录](https://www.bilibili.com/video/BV1mG4y1H7or)

#### 友情链接
- [simRallyCn](https://www.simrallycn.top/)：模拟拉力中文社区

#### 截图

![](Docs/UI.zh.png)
![](Docs/Settings.zh.png)

#### 简介

ZTMZ车队路书工具，可以录制和播放Dirt Rally 2.0游戏的路书，车队QQ群：207790761，路书交流群：697673264

#### 白嫖指南 (安装过程请保持网络处于连接状态)

1. 通过[下载地址](https://gitee.com/ztmz/ztmz_pacenote/releases)下载最新版的名为 __"ZTMZ Club路书工具x.y.z标准版(xxM).exe"__ 的安装文件双击安装
2. 安装期间如果检测到系统中未安装 `dotnet6`运行时，安装程序会自动下载并安装，可能会弹出UAC提示框，点击“是”即可
3. 打开游戏
4. 在游戏设置的语音选项(Audio)中关掉原版的路书声音(Speech选项拖到最左边)
5. 开始体验
6. 上述过程出了问题：[疑难解答](./QandA.md)

#### 如何和simhub一起使用

1. 在本工具右上角的设置界面里把UDP端口修改为20778
2. 打开 `simhub`，选择DR2游戏并点击右侧的 `游戏设置`
3. 在 `UDP Forwarding`(端口转发)那里前面的勾勾选上，并添加一条转发到 `127.0.0.1`的 `20778`端口的设置（如果已经存在，就只需要勾选上前面的勾即可）
4. 保持simhub处于开启状态，启动本工具即可
5. 本步骤对于想要同时使用其他的工具也适用，比如 `Dirt Rally Telemetry`工具，它默认监听 `10001`端口，只需在 `simhub`里再添加一条转发到 `127.0.0.1`的 `10001`端口的记录就行

#### 各种链接

* 下载地址：[releases](https://gitee.com/ztmz/ztmz_pacenote/releases)
* 路书录制进度：[【腾讯文档】尘埃拉力赛2.0地图路书录制进程](https://docs.qq.com/sheet/DVVljT3dMWkpYSWdH)
* 路书标记对照表：[【腾讯文档】路书对照表](https://docs.qq.com/sheet/DVVlVZFdCWldkdXBi)
* 视频
  - [版本通用使用教程](https://www.bilibili.com/video/BV1oq4y1u7ua/) (置顶评论有进度条)
  - [\[2021-08-29 直播录像\]\[尘埃拉力赛2.0\] 录制路书阿根廷第一个图Las Juntas并拆分](https://www.bilibili.com/video/BV1yQ4y1178R/) (置顶评论有进度条)
  - [2.x版本演示视频 by bigboxx](https://www.bilibili.com/video/BV1jv411J7aL)
  - [2.x版本录制和播放使用教学（脚本路书） by 草帽不是猫](https://www.bilibili.com/video/BV1a64y1i7vs)
  - [1.x版本演示视频 by bigboxx](https://www.bilibili.com/video/BV1Kh411r7PX)
  - [1.x版本录制和播放使用教学（纯语音路书） by 草帽不是猫](https://www.bilibili.com/video/BV1Ev411n7v9)
  - [1.x版本录制和播放使用说明文档 by bigboxx](https://www.bilibili.com/read/cv12176546)

#### 文件目录说明 (位于 `%userprofile%/Documents/My Games/ZTMZClub`)

* __codrivers__
  用来存放语音包，可以根据[【腾讯文档】路书对照表](https://docs.qq.com/sheet/DVVlVZFdCWldkdXBi)并对照其他语音包的格式，在该文件夹下新建文件夹来创建新的语音包
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

#### 各模块简介

* __OnlyR.Core__
  从github上直接“借鉴”的声音录制代码[AntonyCorbett/OnlyR](https://github.com/AntonyCorbett/OnlyR)
* __ZTMZ.PacenoteTool__
  主程序，包含界面显示和录制播放的主要逻辑
* __ZTMZ.PacenoteTool.Base__
  基础模块，目前只放了配置文件的加载与保存的逻辑
* __ZTMZ.PacenoteTool.ScriptEditor__
  脚本路书编辑器，用来编辑脚本路书
* __ZTMZ.PacenoteTool.AudioBatchProcessor__
  批量音频文件处理工具，可以用来批量压缩音频文件，批量调整纯语音路书的播放点，批量对音频文件进行掐头去尾的操作。
* __ZTMZ.PacenoteTool.AudioPackageManager__
  语音包管理工具，可以用来创建新的语音包、检查语音包内容完整性、试听语音包内容

#### 项目依赖

* [Newtonsoft.Json](https://www.newtonsoft.com/json)
* [NAudio](https://github.com/naudio/NAudio)
* [PromptDialog](https://github.com/manuelcanepa/wpf-prompt-dialog)
* [AvalonEdit](http://avalonedit.net/)
* [WindowsAPICodePack-Shell](https://github.com/aybe/Windows-API-Code-Pack-1.1)
* [Vosk](https://alphacephei.com/vosk/)
* [GameOverlay.Net](https://github.com/michel-pi/GameOverlay.Net)
* [CoDriver-Splitter](https://github.com/CookiePLMonster/CoDriver-Splitter)
* [Material Design Xaml Toolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit)
* [Inno Setup](https://jrsoftware.org/isinfo.php)
* [Inno Setup Chinese Simplified Translation](https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation)
* [NLog](https://github.com/NLog/NLog)

#### 本项目参考的项目

* [CrewChiefV4](https://gitlab.com/mr_belowski/CrewChiefV4)
* [dr2_logger](https://github.com/ErlerPhilipp/dr2_logger)
* [Dirt Telemetry Tool](https://forums.codemasters.com/topic/9721-dirt-telemetry-tool-cortextuals-version/)
* [NGPCarMenu](https://github.com/mika-n/NGPCarMenu)

#### 如何贡献代码

1. Fork本项目并使用git下载源码
2. 安装[.net 6.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-6.0.300-windows-x64-installer)
3. 使用visual studio 2022或JetBrains Rider通过根目录的ZTMZ.PacenoteTool.sln文件打开本项目，或者直接用VSCode打开本项目文件夹
4. 运行`.\build.bat`命令编译项目，运行`.\builddebug.bat`编译调试版本
5. 将之前最新版本的路书工具安装后，从 `%userprofile%\My Games\ZTMZClub\` 目录中，将`codrivers`和`profiles`目录拷贝到项目的`bin\Release\net6.0-windows\`目录下
6. 下载安装[Inno Setup](https://jrsoftware.org/download.php/is.exe)，并设置系统的Path环境变量，增加`ICSS.exe`文件所在的目录，默认安装应该是`C:\Program Files (x86)\Inno Setup 6`
7. 从[GitHub](https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation)上安装Inno Setup的中文语言包。具体为下载`ChineseSimplified.isl`文件放到`C:\Program Files (x86)\Inno Setup 6\Languages`目录下
8. 运行`.\package.bat`进行打包，可以在`Output`目录找到打包好的安装包
9. 做出修改调试运行无误后，打包项目为exe包并本地安装测试通过后，将代码推送至gitee，再创建PR到本项目

#### 脚本路书编写小技巧

- 在英文输入法状态下输入 `,`(逗号)或者 `/`(左斜杠)，即可展开候选路书标记框
- 对于用来修饰弯道的标记，比如 `/dont_cut`(不要切弯)，可以使用 `/`(左斜杠)而不是 `,`(逗号)作为起始，虽然使用 `,`不影响实际使用，但使用 `/`可以提升阅读体验
- 在候选路书标记框展开后可以继续输入路书标记，来过滤框里的候选标记。
- 在候选路书标记框展开时可以用方向键 `↑`和 `↓`来对候选标记进行选中
- 选中后按 `Enter`键或者 `Tab`键，即可自动补全标记
- 如果想要选择的标记处于第一的位置，无需选择，直接按 `Enter`或 `Tab`即可

> **例如**：想要输入 `5_left`(左五)，只需依次键入 `,` `5` `Enter`三个按键即可完成左五的输入；
> 如果想要输入 `5_right`(右五)，只需依次键入 `,` `5` `↓` `Enter`即可（因为左5是第二个候选），或者键入 `,` `5` `_` `r` `Enter`也可以

#### 如何贡献脚本路书（方法1）

1. 加入路书录制群
2. 选定要录制的路书，并在[【腾讯文档】尘埃拉力赛2.0地图路书录制进程](https://docs.qq.com/sheet/DVVljT3dMWkpYSWdH)文档中对应的赛道标记好自己的名字，标记好录制状态（进行中）
3. 录制完成后上传到群文件，或在脚本路书工具中点击分享至ftp，并修改上述文档中对应的赛道录制状态为已完成

#### 如何贡献脚本路书（方法2）

1. Fork脚本路书项目：[dr2_pacenote_scripts](https://gitee.com/ztmz/dr2_pacenote_scripts)
2. 创建issue申请录制某(几)条赛道的路书
3. 使用本工具录制路书完成后提交PR到路书项目

#### 如何贡献脚本路书语音包

1. 在[【腾讯文档】路书对照表](https://docs.qq.com/sheet/DVVlVZFdCWldkdXBi)中，对corner, detail, number 三张表中的每个标记都录制一段语音，以标记名称作为文件名，录制内容可以自由发挥
2. 参考工具中现有的语音包的格式，default语音包是个比较好的例子，使用工具主界面左下角的语音包管理工具对语音包完整性进行检查
3. 如果对有个标记希望有多个语音随机播放，可以在以标记为名的文件夹中放置多个语音文件，参考 __圣沙蒙VK__ 语音包中的detail_start_stage的形式即可
4. 录制好语音后联系群内的 __bigboxx__ 大佬
5. 非常建议使用ogg格式声音文件，而不是mp3！！！！mp3有bug！！！连wmv格式都比mp3好！！！！

#### 项目计划

<a name='plan'></a>

| 版本号               | 时间段              | 更新内容                                                                                                                                                                                                         |
| -------------------- | ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2.3.2                | 已发布              | 丰富紫藤语音包的替代方案<br />修复过低的播放设备缓冲延迟导致的部分设备播放卡顿的问题                                                                                                                             |
| ~~2.3.3~~ <br />2.4 | 已发布              | 收录全部路书<br />增加路书工具和脚本工具通信协作<br />对游戏内语音进行语音识别成文字<br />再自动生成脚本路书到脚本工具中                                                                                         |
| ~~2.4~~ <br />2.5   | 已发布              | 使用 `gameoverlay.net`库实现在游戏内右上角显示相关信息<br />1. 当前地图路书加载状态<br />2. 路书类型<br />3. 路书作者<br />4. 所使用的语音包(在脚本路书类型时)<br />船深的用户界面<br />可以自动保存的用户设置 |
| 2.5.1                | 已发布              | 特别多，见[更新记录](https://gitee.com/ztmz/ztmz_pacenote/raw/master/ZTMZ.PacenoteTool/更新记录.txt)                                                                                                                |
| ~~2.5~~             | ~~2022-01-14之后~~ | ~~增加路书工具和脚本工具通信协作<br />对游戏内语音进行语音识别成文字<br />再自动生成脚本路书到脚本工具中~~                                                                                                      |
| 2.6                  | 已发布              | 增加语音包制作工具<br />增加了动态语速和动态紧张感特效                                                                                                                                                           |
| 2.6.1-2.6.6          | 已发布 (2022-05)             | 修复部分bug，增加部分语音包<br />增加了悬浮窗的仪表盘功能<br />增加了用于分析和改进工具使用的Google Analytics<br />增加了mesa的英文语音包<br />修复了很多赛道路书的错误                                          |
| 2.7                  | 2022-08               | 增加对多种游戏的支持（Dirt Rally 1.0，RBR/Richard Burns Rally - RSF版） |
| 3.0                  | 2023                  | 增加对EA Sports WRC/Rally 游戏的支持，重置简洁版界面

#### 鸣谢

* [__小贤少少__](https://space.bilibili.com/253480317) 为车队的付出
* [__Meeke777__](https://space.bilibili.com/55088592) 的路书实现思路
* [__Greened U幻想最初__](https://space.bilibili.com/254447657) 的初始路书标记整理
* [__Bigboxx__](https://space.bilibili.com/13133308) 对路书工具的建议和语音包的制作
* [__圣沙蒙VK__](https://space.bilibili.com/6174297) 的语音包和语音路书的录制
* [__紫藤林沫__](https://space.bilibili.com/3712553) 的语音包录制
* [__拉稀车手老王__](https://space.bilibili.com/495490435) 的语音包录制
* [__权威Authority__](https://space.bilibili.com/24297171) 的语音包录制
* [__wha1ing__](https://space.bilibili.com/49581921) 的语音包录制
* [__Hippopo__](https://space.bilibili.com/626685) 的语音包录制和路书校对
* __大李子小妖__ 的语音包录制
* [__mesa__](https://www.racedepartment.com/members/mesa.7580) 的英文tts语音包录制
* [__南沢いずみ__](https://space.bilibili.com/3351506) 的天津话语音包录制
* **左衛門** 的路书校对
* 以及各位大佬的路书录制：（按字母顺序）
  * [HanXu](https://space.bilibili.com/1534349264)
  * [Hippopo](https://space.bilibili.com/626685)
  * 回家的誘いをかける
  * 栗悟饭とカメハメ波
  * [Meeke777](https://space.bilibili.com/55088592)
  * [O.Z. (Gliese-436b)](https://space.bilibili.com/509694621)
  * Silenig
  * smoke (DR2略略略)
  * [Zexx](https://space.bilibili.com/147075875)
