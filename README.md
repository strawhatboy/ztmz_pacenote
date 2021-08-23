# ztmz_pacenote

## WE WANT YOU!
录制过程较为耗时，尘埃拉力赛2.0一张长赛道可以拆分成两张短赛道，只需录制一张长赛道，之后再通过 __脚本路书编辑器__ 或者 __批量音频文件处理工具__ 即可把 __长赛道__ 拆分成 __两张短赛道__，目前录制进度缓慢，希望更多大佬加入一起用爱发电。😂

#### 简介
ZTMZ车队路书工具，可以录制和播放Dirt Rally 2.0游戏的路书，交流QQ群：207790761

#### 各种链接
* 下载地址：[releases](https://gitee.com/ztmz/ztmz_pacenote/releases)
* 路书录制进度：[【腾讯文档】尘埃拉力赛2.0地图路书录制进程](https://docs.qq.com/sheet/DVVljT3dMWkpYSWdH)
* 路书标记对照表：[【腾讯文档】路书对照表](https://docs.qq.com/sheet/DVVlVZFdCWldkdXBi)
* 视频
    - [2.x版本演示视频 by bigboxx](https://www.bilibili.com/video/BV1jv411J7aL)
    - [2.x版本录制和播放使用教学（脚本路书） by 草帽不是猫](https://www.bilibili.com/video/BV1a64y1i7vs)
    - [1.x版本演示视频 by bigboxx](https://www.bilibili.com/video/BV1Kh411r7PX)
    - [1.x版本录制和播放使用教学（纯语音路书） by 草帽不是猫](https://www.bilibili.com/video/BV1Ev411n7v9)
    - [1.x版本录制和播放使用说明文档 by bigboxx](https://www.bilibili.com/read/cv12176546)

#### 白嫖指南
1. 安装[.net 5.0运行时](https://dotnet.microsoft.com/download/dotnet/thank-you/runtime-desktop-5.0.9-windows-x64-installer)
2. 通过[下载地址](https://gitee.com/ztmz/ztmz_pacenote/releases)下载最新版的名为 __"ZTMZ Club路书工具x.y.zip"__ 的压缩包并解压
3. 执行其中的ZTMZ.PacenoteTool.exe
4. 打开游戏
5. 在游戏设置的语音选项(Audio)中关掉原版的路书声音(Speech选项拖到最左边)
6. 进入计时赛选择目前支持的地图(参见【腾讯文档】尘埃拉力赛2.0地图路书录制进程](https://docs.qq.com/sheet/DVVljT3dMWkpYSWdH)
7. 开始体验

#### 各模块简介
* OnlyR.Core
从github上直接“借鉴”的声音录制代码[AntonyCorbett/OnlyR](https://github.com/AntonyCorbett/OnlyRhttps://github.com/AntonyCorbett/OnlyR)
* ZTMZ.PacenoteTool
主程序，包含界面显示和录制播放的主要逻辑
* ZTMZ.PacenoteTool.Base
基础模块，目前只放了配置文件的加载与保存的逻辑
* ZTMZ.PacenoteTool.ScriptEditor
脚本路书编辑器，用来编辑脚本路书
* ZTMZ.PacenoteTool.AudioBatchProcessor
批量音频文件处理工具，可以用来批量压缩音频文件，批量调整纯语音路书的播放点，批量对音频文件进行掐头去尾的操作。

#### 项目依赖
* [Newtonsoft.Json](https://www.newtonsoft.com/json)
* [NAudio](https://github.com/naudio/NAudio)
* [PromptDialog](https://github.com/manuelcanepa/wpf-prompt-dialog)
* [AvalonEdit](http://avalonedit.net/)
* [WindowsAPICodePack-Shell](https://github.com/aybe/Windows-API-Code-Pack-1.1)
#### 本项目参考的项目
* [CrewChiefV4](https://gitlab.com/mr_belowski/CrewChiefV4)
* [dr2_logger](https://github.com/ErlerPhilipp/dr2_logger)

#### 如何贡献代码
1. Fork本项目并使用git下载源码
2. 安装[.net 5.0 SDK](https://dotnet.microsoft.com/download/dotnet/thank-you/sdk-5.0.303-windows-x64-installer)
3. 使用visual studio 2019或JetBrains Rider通过根目录的ZTMZ.PacenoteTool.sln文件打开本项目
4. 做出修改调试运行无误后推送至gitee，再创建PR到本项目

#### 如何贡献脚本路书
1. Fork脚本路书项目：[dr2_pacenote_scripts](https://gitee.com/ztmz/dr2_pacenote_scripts)
2. 创建issue申请录制某(几)条赛道的路书
3. 使用本工具录制路书完成后提交PR到路书项目

直接在群里表明路书录制意愿也可