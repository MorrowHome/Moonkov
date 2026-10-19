# Moonkov: clone the project and run it in Unity (Windows)

This guide assumes you have never used Git. It takes you from a fresh Windows setup to running the game locally, while keeping the large downloads and caches off your C: drive.

Project details checked on **20 October 2026**:

- Repository: [MorrowHome/Moonkov](https://github.com/MorrowHome/Moonkov).
- Branch used by this guide: **`feat/aliens`**. A branch is a named version of the project; copy this name exactly.
- Required Unity Editor: **`6000.5.10f1`**.
- Start scene: **`Assets/Scenes/MainMenu.unity`**.
- For your first run, use **SINGLEPLAYER**. The project has a local save and local raid mode; you do not need to set up an account server or a dedicated game server.

The instructions were checked against the repository and official installation documentation. A fresh clone and first import on another computer have not been tested as part of writing this guide.

## 1. Choose your disk before installing anything

All examples use **D:**. If your large drive is E: or another letter, replace **every `D:` below** with that letter. Use a local SSD if possible. Avoid Desktop, Documents, OneDrive, and other cloud-synced folders.

Open File Explorer, select **This PC**, and check the free space on both drives.

- A practical starting budget is **80–100 GB free on the project drive** for the Editor, project, downloads, and generated caches. This is a planning allowance, not a measured installation size or a maximum.
- Keep roughly **20 GB free on C:** as a working buffer. Windows, installers, graphics drivers, logs, and some temporary files can still use it. Putting Unity on D: does not make C: usage zero.

The checked project revision has approximately **4.2 GB of LFS-tracked file contents**. The downloaded LFS objects and the usable asset files can occupy space separately. Unity then generates its own `Library` folder, which can grow to many gigabytes. The initial download size is therefore much smaller than the space you should reserve.

Use these locations:

| Item | Example location | Where you choose it |
| --- | --- | --- |
| Project, including its `Library` and `.git/lfs` folders | `D:\Projects\Moonkov` | Git clone command below |
| Unity Editor | `D:\Unity\Editors` | Unity Hub Settings, Installs |
| Unity installer downloads | `D:\Unity\Downloads` | Unity Hub Settings, Installs |
| Unity package cache | `D:\UnityCache\UPM` | Step 3, before opening the project |
| Unity lighting cache | `D:\UnityCache\GI` | Editor Preferences, GI Cache |

The Hub application itself and Git are much smaller than the Editor and project. You can also choose a D: installation folder for them if their installers offer it. Save their downloaded installers to D: using your browser's **Save as** option.

## 2. Install Git and check Git LFS

**Git** downloads the project's versioned files. **Git LFS** downloads large assets such as models, textures, terrain, fonts, and audio. Without LFS, some files are only tiny text placeholders and Unity cannot use them.

1. Download the Windows installer from the [official Git download page](https://git-scm.com/install/windows).
2. Install it. Keep **Git LFS** selected if it is offered, and choose the option that lets Git run from the command line and third-party software. The other defaults are normally sufficient.
3. Close any old terminal windows. Open **PowerShell** from the Start menu. You do not need an Administrator terminal.
4. Run the following commands, one line at a time. Copy only the text inside the code block.

```powershell
git --version
git lfs version
```

Both commands should print a version. If the second command says LFS is unavailable, install it from [git-lfs.com](https://git-lfs.com/), reopen PowerShell, and try again. Follow [GitHub's LFS installation instructions](https://docs.github.com/en/repositories/working-with-files/managing-large-files/installing-git-large-file-storage?platform=windows) if needed.

Then run:

```powershell
git lfs install
git config --global core.longpaths true
```

The second command lets Git handle long Windows file paths. You do not need to configure a Git author name or email just to download and run the project.

**If a command fails, stop at that step and resolve the error before continuing.**

## 3. Install Unity without filling C:

1. Download [Unity Hub](https://unity.com/download) and install it.
2. Sign in with your Unity account. Activate the appropriate Editor license when prompted; Unity Personal is available if you meet its eligibility requirements. See [Unity's license instructions](https://docs.unity.com/en-us/hub/manage-license).
3. Before installing an Editor, open **Hub Settings > Installs**. Set **Installs location** to `D:\Unity\Editors` and **Downloads location** to `D:\Unity\Downloads`. Also set the project location to `D:\Projects` under **Settings > Projects**. These are separate settings; changing the project location alone does not move the Editor or downloads. See the [Hub settings reference](https://docs.unity.com/en-us/hub/settings).
4. Install **6000.5.10f1**. If Hub does not list it, open its [official release page](https://unity.com/releases/editor/whats-new/6000.5.10f1) or the [Unity download archive](https://unity.com/releases/editor/archive), find that exact version, and choose the Unity Hub installation option.
5. For playing inside the Windows Editor, leave optional platforms and tools unselected. You do not need Android SDK/NDK, WebGL, Linux, Dedicated Server support, Windows IL2CPP support, offline documentation, or Visual Studio for this first run. Modules can be added later if you want to build the game or edit code.
6. Confirm the installation destination is on D: before starting the download. Do not substitute a newer Editor or upgrade the project when prompted.

**Move the package cache before the first project import.** Unity downloads packages separately from the Editor. To keep those downloads off C:, completely quit Unity Hub, including its system-tray icon, and close any Unity Editor windows. Run this in PowerShell:

```powershell
New-Item -ItemType Directory -Force -Path 'D:\UnityCache\UPM' | Out-Null
[Environment]::SetEnvironmentVariable('UPM_CACHE_ROOT', 'D:\UnityCache\UPM', 'User')
```

This sets Unity's package cache location for your Windows user, including other Unity projects. **Restart Windows before launching Hub again**, so new processes inherit the setting. This does not move previously downloaded caches. Unity documents this variable in [Customize the global cache](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-config-cache.html).

After restarting, open a new PowerShell window and check:

```powershell
$env:UPM_CACHE_ROOT
```

It should print `D:\UnityCache\UPM`. If you already had custom Unity cache settings, check those too; individual cache overrides can take precedence.

## 4. Clone the correct branch

First open the [repository page](https://github.com/MorrowHome/Moonkov) in your browser. If access is restricted, ask the project owner to invite your GitHub account and accept the invitation before cloning.

In PowerShell, run:

```powershell
New-Item -ItemType Directory -Force -Path 'D:\Projects' | Out-Null
$env:GIT_LFS_FORCE_PROGRESS = '1'
git clone --depth 1 --single-branch --branch feat/aliens --progress https://github.com/MorrowHome/Moonkov.git 'D:\Projects\Moonkov'
```

This uses HTTPS, so you do not need to set up SSH keys. If Git asks you to sign in, follow its browser sign-in prompt with the GitHub account that has repository access. Your GitHub password is not a Git command-line password.

`--depth 1 --single-branch` limits the initial Git history to save space and download time. It still downloads the current branch's required assets. **Do not use GitHub's Download ZIP button for this walkthrough.**

Downloading and checking out LFS assets can take much longer than downloading the code. Leave the terminal open and wait until the PowerShell prompt returns. Do not open Unity while the download is still running.

After cloning finishes successfully:

```powershell
Set-Location 'D:\Projects\Moonkov'
git config lfs.forceprogress true
git lfs pull
git branch --show-current
```

`git lfs pull` makes sure the large assets for your current checkout have been downloaded and placed in the project. If clone already downloaded everything, it may finish quickly. The last command should print **`feat/aliens`**.

As a quick check for assets still represented by LFS placeholders, run:

```powershell
git lfs ls-files | Select-String ' - '
```

Normally this prints nothing: LFS lists downloaded working files with `*`, and placeholders with `-`. If it prints file entries, finish `git lfs pull` before opening Unity. This is a download check, not a test that the game works.

## 5. Add the downloaded project to Unity Hub

1. Open **Hub > Projects**.
2. Choose **Add > Add project from disk**, or the equivalent **Open** option in your Hub version.
3. Select **`D:\Projects\Moonkov`**. This is the folder containing **Assets**, **Packages**, and **ProjectSettings**. Do not select just the Assets folder, and do not create a new project.
4. Confirm the assigned Editor is **6000.5.10f1**, then open the project.
5. If asked about Unity Cloud or organization access, ask the project owner if necessary. Do not create or relink a cloud project merely to try single-player mode.

The first opening downloads packages, imports assets, compiles scripts and shaders, and generates the local `Library` folder on D:. It can take a while. Wait for importing and compilation to finish before pressing Play. Git must remain installed and available: the package manifest also includes a package fetched from GitHub.

Once the Editor is responsive, open **Edit > Preferences > GI Cache**. Enable **Custom cache location** and set it to `D:\UnityCache\GI`. A **5 GB** cache budget is a reasonable starting point for trying the game; it is not a hard limit on all Unity storage. See [GI Cache preferences](https://docs.unity3d.com/6000.0/Documentation/Manual/preferences-gi-cache.html).

Under **Preferences > Package Manager**, verify that the package cache points to D:. If you later download Asset Store packages, set **My Assets > Cache Location** to a D: folder too; that is a [separate cache](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-config-cache-as.html). You do not need to download extra Asset Store content for this walkthrough.

## 6. Run the game locally

1. In Unity's **Project** window, open **Assets > Scenes** and double-click **MainMenu** (`MainMenu.unity`).
2. Press the triangular **Play** button at the top of the Editor. Open the **Game** tab if necessary.
3. On the account screen, click **SINGLEPLAYER**. If the interface is Chinese, look for **单机模式**. This opens an independent local profile; online login or registration is not required.
4. Use the ship/home menu's deployment action and follow the operation preparation screens to start a raid. The configured gameplay map is **TheophilusGameScene**.
5. To change the interface language, open the in-game **SETTINGS** page and choose **English** in **LANGUAGE**. In Chinese, look for **设置** and **语言**.
6. Click inside the Game view to give it keyboard and mouse focus. Press Play again when you want to stop. Finish or exit a raid through the game UI first if you want it to settle normally.

Use **MainMenu** as the entry point. Opening the large terrain scene directly and pressing Play bypasses the normal startup and profile flow. You do not need the Moon Demo or DollSinger Demo scenes to start the main game.

Local single-player saves are stored separately from the checkout, under Unity's persistent data directory (normally `%USERPROFILE%\AppData\LocalLow\DefaultCompany\FPS_Template\SinglePlayer` on Windows). These small save files can remain on C: even when the project is on D:. Back them up if you care about your progress.

## 7. Download future updates

Close Unity before updating. Open PowerShell and run these commands one at a time:

```powershell
Set-Location 'D:\Projects\Moonkov'
git status --short
```

If the status command prints nothing, continue:

```powershell
git pull --ff-only
git lfs pull
```

Only reopen Unity after both commands succeed. `--ff-only` stops instead of making an unexpected merge. You do not need a new clone for normal updates.

If status lists changed or new files, or pull reports a conflict/diverged branch, send that output to the project owner first. Unity may have saved local scene or settings changes. Do not use `git reset --hard`, `git clean`, or force commands to make an error disappear; they can discard your work. If `ProjectVersion.txt` changes in an update, install the newly specified Editor version before reopening.

## Troubleshooting

| Problem | What to do |
| --- | --- |
| `git` is not recognized | Reopen PowerShell after installation. If needed, reinstall Git with command-line/PATH support enabled. |
| `git: 'lfs' is not a git command` | Install Git LFS, reopen PowerShell, then run `git lfs install` and `git lfs pull` inside the project. |
| Repository not found / 403 / authentication error | Check the URL, accept any repository invitation, and sign in with the authorized GitHub account. |
| Download appears quiet | LFS may still be downloading. Check network/disk activity and free space. Do not start a second clone into the same folder or delete `.git/index.lock` while Git is running. |
| LFS download was interrupted | Wait for the old command to exit, go to the existing project folder, and retry `git lfs pull`. If clone reported a checkout failure but the folder contains `.git`, `Assets`, `Packages`, and `ProjectSettings`, try this before cloning again. |
| LFS says quota/bandwidth exceeded or an object is missing | Send the exact error to the owner. They must resolve storage/billing/access or upload the missing object; repeated clones will not fix it. |
| Destination already exists | Use the existing clone if it completed. Do not keep creating extra copies; each copy can consume another full set of assets and caches. |
| Models/textures fail to import, or a binary asset contains `version https://git-lfs.github.com/spec/v1` | Close Unity and complete `git lfs pull`. That text is an LFS placeholder. |
| Red Console errors / Play is blocked | Wait for import to finish, confirm the exact Editor version and successful LFS download, then send the first red error and the output of `git rev-parse --short HEAD` to the owner. Do not upgrade packages at random. |
| Online account service is unavailable | For a local first run, choose SINGLEPLAYER. Online accounts and multiplayer require separately configured services. |
| C: is still growing | Recheck Hub's download location, `UPM_CACHE_ROOT`, and GI cache location. Existing caches are not automatically moved. Windows temporary files and graphics-driver caches can still use C:. Stop imports/installations if space is running low. |

Avoid `git lfs fetch --all`: it downloads historical LFS content you do not need for this first run. Do not delete `.git`, project assets, `.meta` files, or baked lighting data to free space. Do not delete `Library` as routine maintenance either: Unity would rebuild it on the next opening, costing time and disk space again.
