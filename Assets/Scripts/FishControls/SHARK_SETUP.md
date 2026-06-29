# Shark (hunter) setup — Boids project (HDRP + Mirror)

The shark reuses the existing `FishMotor` + `FishController` + `FishOrbitCamera` for movement
and adds three scripts (all in this folder):

| Script | Role |
|---|---|
| `SharkAbilities` | Attack dash + bite, big lunge, catch/eat detection |
| `Edible` | Marker on prey fish; `Devour()` flips it to an eaten state |
| `SwimAnimatorLink` | Drives the shark's Animator (`Speed` float, `Bite` trigger) from the motor |

## Shark prefab hierarchy
```
Shark                 ← Rigidbody + Collider + FishMotor + FishController + SharkAbilities + FishPlayer (NetworkBehaviour)
└─ Model (Animator)   ← rigged shark; SwimAnimatorLink here
```
1. Same movement setup as the fish prefab (Rigidbody/Collider/FishMotor/FishController).
2. Animator on the model with: float `Speed` (0..1) blending slow→fast swim, trigger `Bite`.
   Add `SwimAnimatorLink` next to it.
3. On the **Shark root** add `SharkAbilities`. Select the shark — the **red gizmo sphere** is
   the eat range; line it up with the jaw via `Mouth Offset` / `Eat Radius`.
4. Make it fast-but-wide: raise `FishMotor → Max/Boost Speed`, lower `Turn Responsiveness`.

## Shark controls
| Input | Action |
|---|---|
| Left Click / RT | Attack: short dash + bite (small cooldown). Connects = eat. |
| Q / LT | Lunge: big fast dash. Catches a fish in range → stops and eats. |

## Prey
On each prey fish add `Edible`. Fill *Disable On Eaten* with its `FishController` + `FishMotor`
so it goes limp when caught. Hook `On Devoured` for score / respawn / VFX.

## Multiplayer integration (Mirror) — IMPORTANT

These scripts are netcode-agnostic on purpose. Two things to wire, matching your existing
`FishPlayer` pattern:

1. **Ownership gating.** Just like `FishPlayer` enables `FishController` only in
   `OnStartLocalPlayer`, also enable `SharkAbilities` there (and disable it in `Awake`).
   So a shark `FishPlayer` should do:
   ```csharp
   // Awake:   if (TryGetComponent(out SharkAbilities a)) a.enabled = false;
   // OnStartLocalPlayer:
   //   if (TryGetComponent(out SharkAbilities a)) { a.enabled = true; a.CaughtPrey += OnCaughtPrey; }
   ```

2. **Server-authoritative eating.** The owner detects the bite and raises
   `SharkAbilities.CaughtPrey(edible)`. Don't eat on the client — send a Command and let the
   SERVER validate range and devour:
   ```csharp
   void OnCaughtPrey(Edible prey)
   {
       var id = prey.GetComponent<NetworkIdentity>();
       if (id != null) CmdEat(id);
   }

   [Command]
   void CmdEat(NetworkIdentity preyId)
   {
       if (preyId == null) return;
       var prey = preyId.GetComponent<Edible>();
       // (optional) re-check distance here to reject cheats
       if (prey != null) prey.Devour(gameObject);   // runs on server; SyncVar/RPC fans out
   }
   ```
   Then make `Edible.IsEaten` a `SyncVar` (or trigger a `ClientRpc`) so every client disables
   the prey + plays the death anim, and swap the local `Destroy` for `NetworkServer.Destroy`.

Until you wire step 2, `SharkAbilities` falls back to eating locally so you can test offline.
