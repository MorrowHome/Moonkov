# Quaternius melee motion sources

Official packs: [Universal Animation Library](https://quaternius.com/packs/universalanimationlibrary.html) and [Universal Animation Library 2](https://quaternius.com/packs/universalanimationlibrary2.html), downloaded from the creator's itch.io pages on 2026-10-24.

Author: Quaternius. License: CC0 1.0; the original license is included in `License.txt`. These sources permit personal and commercial use and redistribution. No Mixamo account or third-party reupload is used.

`Animation/UAL1_Standard.fbx` and `UAL2_Standard.fbx` are the original non-root-motion Unity exports, stored through Git LFS. Only the required takes are imported as Humanoid clips. They are authoring sources, outside Resources; the player uses the generated exact-skeleton JSON libraries.

`QuaterniusMeleeSetup.Import()` configures the source avatars. `QuaterniusMeleeSetup.BakeAll()` retargets both production avatars in isolated preview scenes without entering Play or saving the game scene.

- Fists: Punch_Jab, Punch_Cross, Melee_Hook, Melee_Hook_Rec.
- Dagger and energy sword: Sword_Idle, Sword_Regular_A, Sword_Regular_B, Sword_Attack, the first committed strike of Sword_Heavy_Combo, Sword_Regular_A_Rec.
- The 60 Hz bake includes -70°, 0° and +70° aim poses, preserves character-specific bones and grip metadata, and changes rotations rather than bone scale.
- Runtime retimes authored windup, contact and recovery to the shared server melee rules. Damage, stamina, cancellation and recovery remain server-owned; imported animation events and root motion never apply damage or move the player.

Survival actions also import Chest_Open, Consume, Interact and PickUp_Table. `RaidActionAnimationSetup.BakeAll()` only bakes the legacy seed and refuses to overwrite reviewed Blender libraries. Final playback comes from `SurvivalActions.blend` through `export_survival_runtime.py` and `build_survival_runtime.py`. Consume and PickUp_Table are mirrored for right-hand inventory props. Existing melee trims are retained when importing these additional takes. See `Docs/Development/SurvivalActions.md` for contact, foley and validation.

Validation is limited to compilation, exact-rig sampling, and native previews. Packed-game feel, first-person composition and combat balance require a new build acceptance pass.
