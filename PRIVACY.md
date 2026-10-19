# 隐私政策 / Privacy Policy

适用于：歌词 - WidBar 小组件（Lyrics Player）
Applies to: 歌词 - WidBar 小组件 (Lyrics Player)

最后更新 / Last updated: 2026-10-19

---

## 中文

**简而言之：本应用不收集、不存储、不上传任何个人数据。**

1. **读取正在播放的媒体信息（仅在本机）**
   应用通过 Windows 系统媒体传输控件（SMTC）读取当前播放歌曲的标题、歌手、专辑封面、播放进度和播放状态，用于在任务栏显示歌词和播放控制按钮。为了实现“只跟随指定应用”功能，应用会在本机列出正在发声的软件名称和图标。这些信息只在你的电脑上处理，不会发送给开发者或任何第三方（下面第 2 条所述的歌词查询除外）。

2. **查询歌词**
   为了获取歌词，应用会把当前歌曲的**标题、歌手（以及专辑名和时长）**发送给以下公开接口：
   - 网易云音乐（music.163.com）
   - LRCLIB（lrclib.net）

   请求中不包含任何账号、设备标识或其他个人信息。这些服务对请求的处理遵循其各自的隐私政策。你可以在设置中关闭任一歌词来源。

3. **本地存储**
   你的设置、单曲歌词偏移和已下载的歌词缓存保存在本机的应用数据目录中，卸载应用时会一并删除。

4. **不收集的内容**
   - 不需要注册或登录任何账号；
   - 没有统计分析、广告或崩溃上报 SDK；
   - 不收集姓名、邮箱、位置、联系人或任何其他个人数据；
   - 不与任何第三方共享数据。

5. **联系方式**
   如有问题，请在 GitHub 提交 issue：https://github.com/hq970123/LyricsPlayer/issues

---

## English

**In short: this app does not collect, store, or upload any personal data.**

1. **Reading what is playing (locally only)**
   The app uses the Windows System Media Transport Controls (SMTC) to read the title, artist, album art, playback position and playback state of the song currently playing, so it can show lyrics and playback controls on the taskbar. For the "follow only selected apps" setting it lists, on your PC, the names and icons of apps that are currently playing sound. This information is processed on your PC only and is never sent to the developer or any third party (except for the lyrics lookup described in point 2).

2. **Looking up lyrics**
   To fetch lyrics, the app sends the current song's **title and artist (plus album name and duration)** to these public services:
   - NetEase Cloud Music (music.163.com)
   - LRCLIB (lrclib.net)

   These requests contain no account, device identifier or other personal information. Each service handles requests under its own privacy policy. You can turn either lyrics source off in the settings.

3. **Local storage**
   Your settings, per-song lyric offsets and the downloaded lyrics cache are stored in the app's local data folder on your PC and are removed when you uninstall the app.

4. **What the app does not do**
   - No sign-up or account of any kind;
   - No analytics, advertising or crash-reporting SDKs;
   - No collection of names, email addresses, location, contacts or any other personal data;
   - No sharing of data with third parties.

5. **Contact**
   For questions, open an issue on GitHub: https://github.com/hq970123/LyricsPlayer/issues
