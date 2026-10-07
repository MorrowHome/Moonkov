# Medical items

The M-40 injector restores up to 40 HP and consumes one item. It occupies one
inventory cell, stacks to four and weighs 0.15 kg per injector. Buy it for eight
moon dust on the ship, or find it in every third supply cache. Temporary sessions
start with two; persistent players bring their own inventory.

Move injectors to pockets or the equipped chest rig. Press **4** in gameplay or
right-click an accessible injector in the raid inventory and choose **Use**.
Backpack and nested-container supplies must be moved to accessible storage first.

The match server resolves the source connection's living character, checks the
raid, inventory version and request ID, caps health at MaxHealth and consumes the
stack atomically. Full health, death, inaccessible storage and stale/replayed
requests do not consume an item. Healing and inventory return through the existing
ghost state and inventory snapshot channels. Account APIs never modify raid health.

Targeted validation covers capped healing, full-health/death rejection, inaccessible
storage and stale replay. Actual two-client use and visual feel need in-game acceptance.
