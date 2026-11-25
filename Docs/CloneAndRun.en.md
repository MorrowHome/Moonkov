# Run Moonkov in the Unity Editor on Windows

1. Install Git with Git LFS. Install Unity Editor **6000.5.10f1** through Unity Hub. Choose a drive with room for the large assets and the generated Unity Library. [Git for Windows](https://git-scm.com/install/windows) / [Unity release](https://unity.com/releases/editor/whats-new/6000.5.10f1)
2. Open PowerShell in the folder where you want the project. Download the integration branch and its large assets.

   ```powershell
   git lfs install
   git clone --branch develop https://github.com/MorrowHome/Moonkov.git
   cd Moonkov
   git lfs pull
   ```

3. In Unity Hub, add the downloaded `Moonkov` folder and open it with the pinned Editor version. Let the first import finish.
4. Open `Assets/Scenes/MainMenu.unity` and press **Play**. Select **Singleplayer** to use a local profile. Follow the tutorial or open the stash, prepare a loadout and start an expedition. The Editor runs both halves of the local game. [Single-player details](SinglePlayer.md)

## Common issues

- **Models or textures import as text.** Run `git lfs checkout` from the project folder. If an object is missing locally, run `git lfs pull` and let Unity reimport it.
- **Classic mode reports a missing map.** Dust2 is an optional local asset. The standard expedition is included in the repository.
- **Getting later changes.** With your own edits saved, run `git pull --ff-only` on `develop`, then `git lfs pull`.
