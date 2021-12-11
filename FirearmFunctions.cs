using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ModularFirearms.Projectiles;
using ModularFirearms.Shared;
using ModularFirearms.Weapons;
using RainyReignGames.RevealMask;
using ThunderRoad;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ModularFirearms {
	/// <summary>
	///     Represents a function that:
	///     1) Attempts to shoot a projectile and play particle/haptic effects
	///     2) Returns a bool representing if that attempt is successful or not (i.e. no ammo, etc)
	/// </summary>
	/// <returns></returns>
	public delegate bool TrackFiredDelegate();

	/// <summary>
	///     Represents a function that determines if the trigger is currently pressed.
	/// </summary>
	/// <returns></returns>
	public delegate bool TriggerPressedDelegate();

	/// <summary>
	///     Represents a function that determines if weapon is firing
	/// </summary>
	/// <returns></returns>
	public delegate void IsFiringDelegate(bool status);

	/// <summary>
	///     Represents a function that determines if a projectile is currently spawning
	/// </summary>
	/// <returns></returns>
	public delegate bool IsSpawningDelegate();

	/// <summary>
	///     Represents a function that sets the projectile spawning flag
	/// </summary>
	/// <param name="status"></param>
	public delegate void SetSpawningStatusDelegate(bool status);

	/// <summary>
	///     Core Framework Functions, meant to be shared across multiple classes
	/// </summary>
	public static class FirearmFunctions {
		public enum AmmoType {
			Pouch = 0,
			Magazine = 1,
			AmmoLoader = 2,
			SemiAuto = 3,
			ShotgunShell = 4,
			Revolver = 5,
			Battery = 6,
			Sniper = 7,
			Explosive = 8,
			Generic = 9
		}

		public enum AttachmentType {
			SecondaryFire = 0,
			Flashlight = 1,
			Laser = 2,
			GrenadeLauncher = 3,
			AmmoCounter = 4,
			Compass = 5,
			FireModeSwitch = 6
		}

		/// <summary>
		///     Defines which behaviour should be produced at runtime
		/// </summary>
		public enum FireMode {
			/// <summary>
			///     Used for when the weapon is in safe-mode or is unable to fire for other reasons
			/// </summary>
			Safe = 0,

			/// <summary>
			///     Used for a single-shot, semi-auto weapon behaviour
			/// </summary>
			Single = 1,

			/// <summary>
			///     Used for x-Round burst weapon behaviour
			/// </summary>
			Burst = 2,

			/// <summary>
			///     Used for full automatic weapon behaviour
			/// </summary>
			Auto = 3
		}

		public enum ProjectileType {
			notype = 0,
			Pierce = 1,
			Explosive = 2,
			Energy = 3,
			Blunt = 4,
			HitScan = 5,
			Sniper = 6
		}

		public enum WeaponType {
			AutoMag = 0,
			SemiAuto = 1,
			Shotgun = 2,
			BoltAction = 3,
			Revolver = 4,
			Sniper = 5,
			HighYield = 6,
			Energy = 7,
			TestWeapon = 8,
			SemiAutoLegacy = 9
		}

		/// <summary>
		///     Provide static points for 2D cartesian blend tree, to be used for firemode selection states.
		///     Indicies match the corresponding FireMode enum, i.e. Misfire, Single, Burst, Auto
		/// </summary>
		private static readonly float[,] blendTreePositions = new float[4, 2]
			{{0.0f, 0.0f}, {0.0f, 1.0f}, {1.0f, 0.0f}, {1.0f, 1.0f}};

		private static readonly Vector3[] buckshotOffsetPosiitions = new Vector3[5] {
			Vector3.zero, new Vector3(0.05f, 0.05f, 0.0f), new Vector3(-0.05f, -0.05f, 0.0f),
			new Vector3(0.05f, -0.05f, 0.0f), new Vector3(0.07f, 0.07f, 0.0f)
		};

		public static string projectileColliderReference = "BodyCollider";

		public static Array weaponTypeEnums = Enum.GetValues(typeof(WeaponType));

		public static Array ammoTypeEnums = Enum.GetValues(typeof(AmmoType));

		public static Array projectileTypeEnums = Enum.GetValues(typeof(ProjectileType));

		public static Array attachmentTypeEnums = Enum.GetValues(typeof(AttachmentType));

		/// <summary>
		///     A static array useful for accessing FireMode enums by index
		/// </summary>
		private static readonly Array fireModeEnums = Enum.GetValues(typeof(FireMode));

		/// <summary>
		///     A static array useful for accessing ForceMode enums by index
		/// </summary>
		public static Array forceModeEnums = Enum.GetValues(typeof(ForceMode));

		private static readonly EffectData bloodHitData = Catalog.GetData<EffectData>("HitRagdollOnFlesh");
		private static readonly EffectData data = Catalog.GetData<EffectData>("HitBladeDecalFlesh");
		public static bool isShootCoroutineRunning;

		/// <summary>
		///     Take a given FireMode and return an increment/loop to the next enum value
		/// </summary>
		/// <param name="currentSelection"></param>
		/// <param name="allowedFireModes"></param>
		/// <returns></returns>
		public static FireMode CycleFireMode(FireMode currentSelection, List<int> allowedFireModes = null) {
			var selectionIndex = (int) currentSelection;
			selectionIndex++;
			if (allowedFireModes != null) {
				foreach (var _ in Enumerable.Range(0, fireModeEnums.Length)) {
					if (allowedFireModes.Contains(selectionIndex))
						return (FireMode) fireModeEnums.GetValue(selectionIndex);
					selectionIndex++;
					if (selectionIndex >= fireModeEnums.Length) selectionIndex = 0;
				}

				return currentSelection;
			}

			if (selectionIndex < fireModeEnums.Length) return (FireMode) fireModeEnums.GetValue(selectionIndex);
			return (FireMode) fireModeEnums.GetValue(0);
		}

		/// <summary>
		///     Wrapper method for taking an animator and playing an animation if it exists
		/// </summary>
		/// <param name="animator"></param>
		/// <param name="animationName"></param>
		/// <returns></returns>
		public static bool Animate(Animator animator, string animationName) {
			if (animator == null || string.IsNullOrEmpty(animationName)) return false;
			animator.Play(animationName);
			return true;
		}

		/// <summary>
		///     Apply positional recoil to a rigid body. Optionally, apply haptic force to the player controllers.
		/// </summary>
		/// <param name="itemRB"></param>
		/// <param name="recoilForces"></param>
		/// <param name="recoilMult"></param>
		/// <param name="leftHandHaptic"></param>
		/// <param name="rightHandHaptic"></param>
		/// <param name="hapticForce"></param>
		public static void ApplyRecoil(Rigidbody itemRB, float[] recoilForces, float recoilMult = 1.0f,
			bool leftHandHaptic = false, bool rightHandHaptic = false,
			float hapticForce = 1.0f) {            if (rightHandHaptic) {
				PlayerControl.handRight.HapticShort(hapticForce);
			}

			if (leftHandHaptic) {
				PlayerControl.handLeft.HapticShort(hapticForce);
			}

			if (recoilForces == null) return;
			itemRB.AddRelativeForce(new Vector3(
				UnityEngine.Random.Range(recoilForces[0], recoilForces[1]) * recoilMult,
				UnityEngine.Random.Range(recoilForces[2], recoilForces[3]) * recoilMult,
				UnityEngine.Random.Range(recoilForces[4], recoilForces[5]) * recoilMult));

			if (rightHandHaptic) {
				Player.currentCreature.handRight.rb.AddRelativeForce(new Vector3(
					UnityEngine.Random.Range(recoilForces[0], recoilForces[1]) * recoilMult,
					UnityEngine.Random.Range(recoilForces[2], recoilForces[3]) * recoilMult,
					UnityEngine.Random.Range(recoilForces[4], recoilForces[5]) * recoilMult));
			}

			if (leftHandHaptic) {
				Player.currentCreature.handLeft.rb.AddRelativeForce(new Vector3(
					UnityEngine.Random.Range(recoilForces[0], recoilForces[1]) * recoilMult,
					UnityEngine.Random.Range(recoilForces[2], recoilForces[3]) * recoilMult,
					UnityEngine.Random.Range(recoilForces[4], recoilForces[5]) * recoilMult));
			}
		}

		/// <summary>
		///     Use the scene collections of Items and Creatures to apply a normalized force to all rigid bodies within range.
		///     Additionally, apply logic for killing Creatures in range.
		/// </summary>
		/// <param name="origin"></param>
		/// <param name="force"></param>
		/// <param name="blastRadius"></param>
		/// <param name="liftMult"></param>
		/// <param name="forceMode"></param>
		public static void HitscanExplosion(Vector3 origin, float force, float blastRadius, float liftMult,
			ForceMode forceMode = ForceMode.Impulse) {
			try {
				foreach (var item in Item.all.Where(item => Math.Abs(Vector3.Distance(item.transform.position, origin)) <= blastRadius)) {
					//Debug.Log("[F-L42-HitscanExplosion] Hit Item: " + item.name);
					item.rb.AddExplosionForce(force * item.rb.mass, origin, blastRadius, liftMult, forceMode);
					item.rb.AddForce(Vector3.up * liftMult * item.rb.mass, forceMode);
				}

				foreach (var creature in Creature.all.Where(creature => creature != Player.currentCreature).Where(creature => Math.Abs(Vector3.Distance(creature.transform.position, origin)) <= blastRadius)) {
					// Kill Creatures in Range
					//Debug.Log("[F-L42-HitscanExplosion] Hit Creature: " + creature.name);
					if (!creature
						.isKilled) //Debug.Log("[F-L42-HitscanExplosion] Damaging Creature: " + creature.name);
						creature.Damage(new CollisionInstance(new DamageStruct(DamageType.Energy, 9999f)));
					// Apply Forces to Creature Main Body
					creature.locomotion.rb.AddExplosionForce(force * creature.locomotion.rb.mass, origin,
						blastRadius, liftMult, forceMode);
					creature.locomotion.rb.AddForce(Vector3.up * liftMult * creature.locomotion.rb.mass, forceMode);

					//// Dismember Creature Parts
					creature.ragdoll.headPart.Slice();
					creature.ragdoll.GetPart(RagdollPart.Type.LeftLeg).Slice();
					creature.ragdoll.GetPart(RagdollPart.Type.RightLeg).Slice();
					creature.ragdoll.GetPart(RagdollPart.Type.RightArm).Slice();
					creature.ragdoll.GetPart(RagdollPart.Type.LeftArm).Slice();


					// Apply Forces to Creature Parts
					foreach (var part in creature.ragdoll.parts) {
						//Debug.Log("[F-L42-HitscanExplosion] Appyling Force to RD-part " + part.name);
						part.rb.AddExplosionForce(force * part.rb.mass, origin, blastRadius, liftMult, forceMode);
						part.rb.AddForce(Vector3.up * liftMult * part.rb.mass, forceMode);
					}
				}
			}
			catch (Exception e) {
				Debug.LogError("[F-L42-HitscanExplosion][EXCEPTION] " + e.Message + " \n " + e.StackTrace);
			}
		}

		/// <summary>
		///     Sets floats on an Animator, assuming these floats correspong to 2D cartesian coordinates on a blend tree attached
		///     to that animator.
		///     See reference for 'blendTreePositions' for more details.
		/// </summary>
		/// <param name="animator"></param>
		/// <param name="selection"></param>
		/// <param name="paramFloat1"></param>
		/// <param name="paramFloat2"></param>
		public static void SetFireSelectionAnimator(Animator animator, FireMode selection, string paramFloat1 = "x",
			string paramFloat2 = "y") {
			if (animator == null) return;
			try {
				animator.SetFloat(paramFloat1, blendTreePositions[(int) selection, 0]);
				animator.SetFloat(paramFloat2, blendTreePositions[(int) selection, 1]);
			}
			catch {
				Debug.LogError(
					"[FL42-FirearmFunctions][SetSwitchAnimation] Exception in setting Animator floats 'x' and 'y'");
			}
		}


		/// <summary>
		///     Dynamically sets Unity Collision Handling to ignore collisions between firearms and projectiles
		/// </summary>
		/// <param name="shooter"></param>
		/// <param name="i"></param>
		/// <param name="ignore"></param>
		private static void IgnoreProjectile(Item shooter, Item i, bool ignore = true) {
			foreach (var colliderGroup in shooter.colliderGroups)
				foreach (var collider in colliderGroup.colliders)
					foreach (var colliderProjectile in i.colliderGroups.SelectMany(colliderGroupProjectile => colliderGroupProjectile.colliders))
						Physics.IgnoreCollision(collider, colliderProjectile, ignore);
			//Physics.IgnoreLayerCollision(collider.gameObject.layer, GameManager.GetLayer(LayerName.MovingObject));
		}

		/// <summary>
		///     Spawn a projectile from the item catalog, optionally imbue it and propel it forward with a given force
		/// </summary>
		/// <param name="shooterItem"></param>
		/// <param name="projectileID"></param>
		/// <param name="spawnPoint"></param>
		/// <param name="imbueSpell"></param>
		/// <param name="forceMult"></param>
		/// <param name="throwMult"></param>
		/// <param name="pooled"></param>
		/// <param name="IgnoreArg1"></param>
		/// <param name="SetSpawnStatus"></param>
		public static void ShootProjectile(Item shooterItem, string projectileID, Transform spawnPoint,
			string imbueSpell = null, float forceMult = 1.0f, float throwMult = 1.0f,
			bool pooled = false,
			Collider IgnoreArg1 = null,
			SetSpawningStatusDelegate SetSpawnStatus = null) {
			if (spawnPoint == null || string.IsNullOrEmpty(projectileID)) return;

			var projectileData = Catalog.GetData<ItemData>(projectileID);
			if (projectileData == null) {
				Debug.LogError("[Fisher-Firearms][ERROR] No projectile named " + projectileID);
				return;
			}

			SetSpawnStatus?.Invoke(true);
			projectileData.SpawnAsync(i => {
					try {
						i.Throw(throwMult, Item.FlyDetection.Forced);
						shooterItem.IgnoreObjectCollision(i);
						i.IgnoreObjectCollision(shooterItem);
						i.IgnoreRagdollCollision(Player.local.creature.ragdoll);
						if (IgnoreArg1 != null)
							try {
								i.IgnoreColliderCollision(IgnoreArg1);
								foreach (var C in shooterItem.colliderGroups.SelectMany(CG => CG.colliders))
									Physics.IgnoreCollision(i.colliderGroups[0].colliders[0], C);
								// i.IgnoreColliderCollision(shooterItem.colliderGroups[0].colliders[0]);
								//Physics.IgnoreCollision(IgnoreArg1, projectile.definition.GetCustomReference(projectileColliderReference).GetComponent<Collider>());
							}
							catch {
								// ignored
							}

						IgnoreProjectile(shooterItem, i);

						i.transform.position = spawnPoint.position;
						i.transform.rotation = Quaternion.Euler(spawnPoint.rotation.eulerAngles);
						i.rb.velocity = shooterItem.rb.velocity;
						i.rb.AddForce(i.rb.transform.forward * 1000.0f * forceMult);

						var projectileController = i.gameObject.GetComponent<BasicProjectile>();
						if (projectileController != null) projectileController.SetShooterItem(shooterItem);

						//-- Optional Switches --//
						//i.rb.useGravity = false;
						//i.SetColliderAndMeshLayer(GameManager.GetLayer(LayerName.Default));
						//i.SetColliderLayer(GameManager.GetLayer(LayerName.None));
						//i.ignoredItem = shooterItem;
						//shooterItem.IgnoreObjectCollision(i);
						//Physics.IgnoreLayerCollision(GameManager.GetLayer(LayerName.None), GameManager.GetLayer(LayerName.Default));

						SetSpawnStatus?.Invoke(false);
						if (string.IsNullOrEmpty(
							imbueSpell)) return;
						if (projectileController != null)
							projectileController.AddChargeToQueue(imbueSpell);
					}
					catch (Exception ex) {
						Debug.Log("[Fisher-Firearms] EXCEPTION IN SPAWNING " + ex.Message + " \n " + ex.StackTrace);
					}
				},
				Vector3.zero,
				Quaternion.Euler(Vector3.zero),
				null,
				false);
		}

		// New Stuff
		public static IEnumerator ShootCoroutine(Item item, FirearmModule module, BaseFirearmGenerator gun,
			TrackFiredDelegate TrackedFire, IsFiringDelegate WeaponIsFiring,
			TriggerPressedDelegate TriggerPressed,
			FireMode fireSelector) {
			WeaponIsFiring?.Invoke(true);
			var fireDelay = 60.0f / module.fireRate;

			switch (fireSelector) {
				case FireMode.Safe:
					gun.PlayEmptySound();
					yield return null;
					break;
				case FireMode.Single: {
					if (!TrackedFire()) {
						gun.PlayEmptySound();
						yield return null;
					}
					gun.PlayTrailSound();

					yield return new WaitForSeconds(fireDelay);
					gun.PlayTrailSound();
					break;
				}
				case FireMode.Burst: {
					for (var i = 0; i < module.burstNumber; i++) {
						if (!TrackedFire()) {
							gun.PlayEmptySound();
							yield return null;
							break;
						}
						gun.PlayTrailSound();

						yield return new WaitForSeconds(fireDelay);
						gun.PlayTrailSound();
					}
					yield return null;
					break;
				}
				case FireMode.Auto: {
					while (TriggerPressed()) {
						if (!TrackedFire()) {
							gun.PlayEmptySound();
							yield return null;
							break;
						}
						gun.PlayTrailSound();

						yield return new WaitForSeconds(fireDelay);
						gun.PlayTrailSound();
					}

					break;
				}
				default:
					throw new ArgumentOutOfRangeException(nameof(fireSelector), fireSelector, "I don't even know what went wrong here...");
			}

			WeaponIsFiring?.Invoke(false);
		}

		//TODO: Somethings wrong with the range, not sure if getting the property from the module isn't working or the actual raycasting?
		public static void DoRayCast(Item item, Transform raycastPoint, FirearmModule module, float range,
			float force) {
			NoiseManager.AddNoise(item.transform.position, 1000f, Player.currentCreature);
			foreach (var creatureInRange in Creature.all.Where(c =>
				Vector3.Distance(c.transform.position, item.transform.position) < range)) {
				creatureInRange.ragdoll.physicTogglePlayerRadius = 1000f;
				creatureInRange.ragdoll.physicToggleRagdollRadius = 1000f;
			}
			var Transform = raycastPoint.transform;
			var ray = new Ray(Transform.position, Transform.forward);
			LayerMask mask = LayerMask.GetMask("NPC", "Ragdoll", "Dropped Object");
			Physics.Raycast(ray, out var raycastHit, range, mask);

			if (raycastHit.rigidbody == null) return;
			raycastHit.rigidbody.AddForce(force * ray.direction, ForceMode.Impulse);
			var ragdollPart = raycastHit.rigidbody.GetComponentInParent<RagdollPart>();
			if (ragdollPart == null) return;

			var creature = ragdollPart.ragdoll.creature;
			float damage;
			switch (ragdollPart.type) {
				case RagdollPart.Type.Head:
					damage = creature.maxHealth;
					creature.brain.instance.GetModule<BrainModuleSpeak>().Unload();
					Debug.Log($"Shot creature {ragdollPart.type.ToString()}");
					break;
				case RagdollPart.Type.Neck:
					damage = creature.maxHealth;
					Debug.Log($"Shot creature {ragdollPart.type.ToString()}");
					break;
				case RagdollPart.Type.Torso:
					damage = creature.maxHealth / 3;
					Debug.Log($"Shot creature {ragdollPart.type.ToString()}");
					break;
				case RagdollPart.Type.LeftArm:
					damage = creature.maxHealth / 10;
					Debug.Log($"Shot creature {ragdollPart.type.ToString()}");
					ragdollPart.ragdoll.creature.TryPush(Creature.PushType.Hit, ray.direction, 1, ragdollPart.type);
					if(!creature.isKilled)
						creature.handLeft.TryRelease();
					break;
				case RagdollPart.Type.RightArm:
					damage = creature.maxHealth / 10;
					Debug.Log($"Shot creature {ragdollPart.type.ToString()}");
					ragdollPart.ragdoll.creature.TryPush(Creature.PushType.Hit, ray.direction, 1, ragdollPart.type);
					if(!creature.isKilled)
						creature.handRight.TryRelease();
					break;
				case RagdollPart.Type.LeftHand:
					damage = creature.maxHealth / 15;
					Debug.Log($"Shot creature {ragdollPart.type.ToString()}");
					if(!creature.isKilled)
						creature.handLeft.TryRelease();
					break;
				case RagdollPart.Type.RightHand:
					damage = creature.maxHealth / 15;
					Debug.Log($"Shot creature {ragdollPart.type.ToString()}");
					if(!creature.isKilled)
						creature.handRight.TryRelease();
					break;
				case RagdollPart.Type.LeftLeg:
					damage = creature.maxHealth / 10;
					Debug.Log($"Shot creature {ragdollPart.type.ToString()}");
					if (!creature.isKilled)
						creature.ragdoll.SetState(Ragdoll.State.Destabilized);
					break;
				case RagdollPart.Type.RightLeg:
					damage = creature.maxHealth / 10;
					Debug.Log($"Shot creature {ragdollPart.type.ToString()}");
					if (!creature.isKilled)
						creature.ragdoll.SetState(Ragdoll.State.Destabilized);
					break;
				case RagdollPart.Type.LeftFoot:
					damage = creature.maxHealth / 10;
					Debug.Log($"Shot creature {ragdollPart.type.ToString()}");
					if (!creature.isKilled)
						creature.ragdoll.SetState(Ragdoll.State.Destabilized);
					break;
				case RagdollPart.Type.RightFoot:
					damage = creature.maxHealth / 10;
					Debug.Log($"Shot creature {ragdollPart.type.ToString()}");
					if (!creature.isKilled)
						creature.ragdoll.SetState(Ragdoll.State.Destabilized);
					break;
				default:
					damage = creature.maxHealth;
					Debug.Log("WTF");
					throw new ArgumentOutOfRangeException();
			}
			
			var coll = new CollisionInstance(new DamageStruct(DamageType.Pierce, damage));
			coll.damageStruct.damage = damage;
			coll.damageStruct.damageType = DamageType.Pierce;
			coll.sourceMaterial = Catalog.GetData<MaterialData>("Blade");
			coll.targetMaterial = Catalog.GetData<MaterialData>("Flesh");
			coll.targetColliderGroup = ragdollPart.colliderGroup;
			coll.sourceColliderGroup = item.colliderGroups[0];
			coll.contactPoint = raycastHit.point;
			coll.contactNormal = raycastHit.normal;
			
			var penPoint = new GameObject().transform;
			penPoint.position = raycastHit.point;
			penPoint.rotation = Quaternion.LookRotation(raycastHit.normal);
			penPoint.parent = raycastHit.transform;

			coll.damageStruct.penetration = DamageStruct.Penetration.Hit;
			coll.damageStruct.penetrationPoint = penPoint;
			coll.damageStruct.penetrationDepth = 10;
			coll.damageStruct.hitRagdollPart = ragdollPart;

			coll.intensity = damage;
			coll.pressureRelativeVelocity = Vector3.one;

			SpawnBulletHole(raycastHit, ragdollPart, coll, item);

			ragdollPart.ragdoll.creature.Damage(coll);

			if (ragdollPart.type != RagdollPart.Type.Head) return;
			creature.brain.instance.GetModule<BrainModuleDeath>().StopDying();
		}

		// New
		private static void SpawnBulletHole(RaycastHit raycastHit, RagdollPart ragdollPart,
			CollisionInstance collisionInstance, Item item) {
			var effectModuleReveal = data.modules[3] as EffectModuleReveal;
			var revealMaterialControllers = new List<RevealMaterialController>();
			foreach (var renderer in ragdollPart.renderers.Where(renderer => effectModuleReveal != null && renderer.revealDecal && (renderer.revealDecal.type == RevealDecal.Type.Default &&
				effectModuleReveal.typeFilter.HasFlag(EffectModuleReveal.TypeFilter.Default) ||
				renderer.revealDecal.type == RevealDecal.Type.Body &&
				effectModuleReveal.typeFilter.HasFlag(EffectModuleReveal.TypeFilter.Body) ||
				renderer.revealDecal.type == RevealDecal.Type.Outfit &&
				effectModuleReveal.typeFilter.HasFlag(EffectModuleReveal.TypeFilter.Outfit)))) {
				
				revealMaterialControllers.Add(renderer.revealDecal.revealMaterialController);
				if (renderer.splitRenderer)
					revealMaterialControllers.Add(renderer.splitRenderer.GetComponent<RevealMaterialController>());
			}

			var reveal = new GameObject {
				transform = {
					position = raycastHit.point,
					rotation = Quaternion.LookRotation(raycastHit.normal)
				}
			};
			var position = reveal.transform.position;
			var bulletHitEffect = bloodHitData.Spawn(position, reveal.transform.rotation);
			bulletHitEffect.SetIntensity(10f);
			bulletHitEffect.Play();
			var direction = -reveal.transform.forward;
			if (effectModuleReveal != null)
				GameManager.local.StartCoroutine(RevealMaskProjection.ProjectAsync(
					position + -direction * effectModuleReveal.offsetDistance, direction,
					reveal.transform.up, effectModuleReveal.depth, effectModuleReveal.maxSize,
					effectModuleReveal.maskTexture, effectModuleReveal.maxChannelMultiplier, revealMaterialControllers,
					effectModuleReveal.revealData, null));
			TriggerRealisticBleed(ragdollPart, raycastHit.collider, reveal.transform, collisionInstance);
			
			collisionInstance.damageStruct.penetration = DamageStruct.Penetration.Skewer;
			var physicMaterialHash = collisionInstance.targetMaterial.physicMaterialHash;
			var PenetrationPointPosition = collisionInstance.damageStruct.penetrationPoint.position;
			var contactPoint = PenetrationPointPosition + -collisionInstance.damageStruct.penetrationPoint.forward;
			item.mainCollisionHandler.MeshRaycast(collisionInstance.targetColliderGroup, contactPoint, collisionInstance.damageStruct.penetrationPoint.forward, -collisionInstance.damageStruct.penetrationPoint.forward, ref physicMaterialHash);

			var raycastPoint = new GameObject {
				transform = {
					position = contactPoint,
					rotation = Quaternion.LookRotation(raycastHit.normal)
				}
			};
			var ray = new Ray(raycastPoint.transform.position, raycastPoint.transform.forward);
			LayerMask mask = LayerMask.GetMask("NPC", "Ragdoll", "Dropped Object");
			Physics.Raycast(ray, out var raycastHitForExitWound, 100, mask);
			
			var revealSkewed = new GameObject {
				transform = {
					position = raycastHitForExitWound.point,
					rotation = Quaternion.LookRotation(raycastHitForExitWound.normal)
				}
			};
			var positionSkewed = revealSkewed.transform.position;
			var exitEffect = bloodHitData.Spawn(positionSkewed, revealSkewed.transform.rotation);
			exitEffect.SetIntensity(10f);
			exitEffect.Play();
			var directionSkewed = -revealSkewed.transform.forward;
			if (effectModuleReveal != null)
				GameManager.local.StartCoroutine(RevealMaskProjection.ProjectAsync(
					positionSkewed + -directionSkewed * effectModuleReveal.offsetDistance, directionSkewed,
					revealSkewed.transform.up, effectModuleReveal.depth, effectModuleReveal.maxSize,
					effectModuleReveal.maskTexture, effectModuleReveal.maxChannelMultiplier, revealMaterialControllers,
					effectModuleReveal.revealData, null));
			TriggerRealisticBleed(ragdollPart, raycastHitForExitWound.collider, revealSkewed.transform, collisionInstance);
			
			Debug.Log($"Hit position: ({raycastHit.point.x}, {raycastHit.point.y}, {raycastHit.point.z}) \n Exit position: ({revealSkewed.transform.position.x}, {revealSkewed.transform.position.y}, {revealSkewed.transform.position.z})");
			Effect effect;
			effectModuleReveal.Spawn(data, revealSkewed.transform.position, revealSkewed.transform.rotation, out effect, 0f, 0f, revealSkewed.transform, collisionInstance);
			effectModuleReveal.Spawn(data, reveal.transform.position, reveal.transform.rotation, out effect, 0f, 0f, reveal.transform, collisionInstance);
			foreach (var c in Creature.all) {
				c.ragdoll.physicTogglePlayerRadius = 5f;
				c.ragdoll.physicToggleRagdollRadius = 3f;
			}
		}

		// New
		private static void TriggerRealisticBleed(RagdollPart ragdollPart, Collider collider, Transform location,
			CollisionInstance collisionInstance) {
			var effectInstance = new EffectInstance(data, location.position, location.rotation, 0f, 0f, location, collisionInstance, true, Array.Empty<Type>());
			effectInstance.Play();
			
			collisionInstance.targetCollider = collider;
		}

		public static void ProjectileBurst(Item shooterItem, string projectileID, Transform spawnPoint,
			string imbueSpell = null, float forceMult = 1.0f, float throwMult = 1.0f,
			bool pooled = false,
			Collider IgnoreArg1 = null) {
			var projectileData = Catalog.GetData<ItemData>(projectileID);
			if (spawnPoint == null || string.IsNullOrEmpty(projectileID)) return;
			if (projectileData == null) {
				Debug.LogError("[Fisher-Firearms][ERROR] No projectile named " + projectileID);
				return;
			}

			foreach (var offsetVec in buckshotOffsetPosiitions)
				projectileData.SpawnAsync(i => {
						try {
							i.transform.position = spawnPoint.position + offsetVec;
							i.transform.rotation = Quaternion.Euler(spawnPoint.rotation.eulerAngles);
							i.rb.velocity = shooterItem.rb.velocity;
							i.rb.AddForce(i.rb.transform.forward * 1000.0f * forceMult);
							shooterItem.IgnoreObjectCollision(i);
							i.IgnoreObjectCollision(shooterItem);
							i.IgnoreRagdollCollision(Player.local.creature.ragdoll);

							if (IgnoreArg1 != null)
								try {
									i.IgnoreColliderCollision(IgnoreArg1);
									foreach (var C in shooterItem.colliderGroups.SelectMany(CG => CG.colliders))
										Physics.IgnoreCollision(i.colliderGroups[0].colliders[0], C);
									// i.IgnoreColliderCollision(shooterItem.colliderGroups[0].colliders[0]);
									//Physics.IgnoreCollision(IgnoreArg1, projectile.definition.GetCustomReference(projectileColliderReference).GetComponent<Collider>());
								}
								catch {
									// ignored
								}

							var projectileController = i.gameObject.GetComponent<BasicProjectile>();
							if (projectileController != null) projectileController.SetShooterItem(shooterItem);

							//-- Optional Switches --//
							//i.rb.useGravity = false;
							//i.Throw(throwMult, Item.FlyDetection.CheckAngle);
							//i.SetColliderAndMeshLayer(GameManager.GetLayer(LayerName.Default));
							//i.SetColliderLayer(GameManager.GetLayer(LayerName.None));
							//i.ignoredItem = shooterItem;
							//shooterItem.IgnoreObjectCollision(i);
							//Physics.IgnoreLayerCollision(GameManager.GetLayer(LayerName.None), GameManager.GetLayer(LayerName.Default));

							if (string.IsNullOrEmpty(
								imbueSpell)) return;
							if (projectileController != null)
								projectileController.AddChargeToQueue(imbueSpell);
						}
						catch (Exception ex) {
							Debug.Log("[Fisher-Firearms] EXCEPTION IN SPAWNING " + ex.Message + " \n " + ex.StackTrace);
						}
					},
					spawnPoint.position,
					Quaternion.Euler(spawnPoint.rotation.eulerAngles),
					null,
					false);
		}

		public static void ShotgunBlast(Item shooterItem, string projectileID, Transform spawnPoint, float distance,
			float force, float forceMult, string imbueSpell = null, float throwMult = 1.0f,
			bool pooled = false,
			Collider IgnoreArg1 = null) {
			if (Physics.Raycast(spawnPoint.position, spawnPoint.forward, out var hit, distance)) {
				var hitCreature = hit.collider.transform.root.GetComponentInParent<Creature>();
				if (hitCreature != null) {
					if (hitCreature == Player.currentCreature) return;
					//Debug.Log("[FL42 - FirearmFunctions][hitCreature] Hit creature!");
					hitCreature.locomotion.rb.AddExplosionForce(force, hit.point, 1.0f, 1.0f, ForceMode.VelocityChange);
					//hitCreature.ragdoll.SetState(Creature.State.Destabilized);
					foreach (var part in hitCreature.ragdoll.parts) {
						part.rb.AddExplosionForce(force, hit.point, 1.0f, 1.0f, ForceMode.VelocityChange);
						part.rb.AddForce(spawnPoint.forward * force, ForceMode.Impulse);
					}
				}
				else {
					try {
						//Debug.Log("[FL42 - FirearmFunctions][hitCreature] Hit item");
						hit.collider.attachedRigidbody.AddExplosionForce(force, hit.point, 0.5f, 1.0f,
							ForceMode.VelocityChange);
						hit.collider.attachedRigidbody.AddForce(spawnPoint.forward * force, ForceMode.Impulse);
					}
					catch {
						// ignored
					}
				}
			}

			var projectileData = Catalog.GetData<ItemData>(projectileID);
			if (projectileData == null) {
				Debug.LogError("[Fisher-Firearms][ERROR] No projectile named " + projectileID);
				return;
			}

			foreach (var offsetVec in buckshotOffsetPosiitions)
				projectileData.SpawnAsync(i => {
						try {
							i.transform.position = spawnPoint.position + offsetVec;
							i.transform.rotation = Quaternion.Euler(spawnPoint.rotation.eulerAngles);
							i.rb.velocity = shooterItem.rb.velocity;
							i.rb.AddForce(i.rb.transform.forward * 1000.0f * forceMult);
							shooterItem.IgnoreObjectCollision(i);
							i.IgnoreObjectCollision(shooterItem);
							i.IgnoreRagdollCollision(Player.local.creature.ragdoll);

							if (IgnoreArg1 != null)
								try {
									i.IgnoreColliderCollision(IgnoreArg1);
									foreach (var C in shooterItem.colliderGroups.SelectMany(CG => CG.colliders))
										Physics.IgnoreCollision(i.colliderGroups[0].colliders[0], C);
									// i.IgnoreColliderCollision(shooterItem.colliderGroups[0].colliders[0]);
									//Physics.IgnoreCollision(IgnoreArg1, projectile.definition.GetCustomReference(projectileColliderReference).GetComponent<Collider>());
								}
								catch { 
									// ignored
								}

							var projectileController = i.gameObject.GetComponent<BasicProjectile>();
							if (projectileController != null) projectileController.SetShooterItem(shooterItem);

							//-- Optional Switches --//
							//i.rb.useGravity = false;
							//i.Throw(throwMult, Item.FlyDetection.CheckAngle);
							//i.SetColliderAndMeshLayer(GameManager.GetLayer(LayerName.Default));
							//i.SetColliderLayer(GameManager.GetLayer(LayerName.None));
							//i.ignoredItem = shooterItem;
							//shooterItem.IgnoreObjectCollision(i);
							//Physics.IgnoreLayerCollision(GameManager.GetLayer(LayerName.None), GameManager.GetLayer(LayerName.Default));

							if (string.IsNullOrEmpty(
								imbueSpell)) return;
							if (projectileController != null)
								projectileController.AddChargeToQueue(imbueSpell);
						}
						catch (Exception ex) {
							Debug.Log("[Fisher-Firearms] EXCEPTION IN SPAWNING " + ex.Message + " \n " + ex.StackTrace);
						}
					},
					spawnPoint.position,
					Quaternion.Euler(spawnPoint.rotation.eulerAngles),
					null,
					false);
		}

		/// <summary>
		///     Iterate through the Imbues on an Item and return the first charged SpellID found.
		/// </summary>
		/// <param name="interactiveObject">Item class, representing an interactive game object</param>
		/// <returns></returns>
		public static string GetItemSpellChargeID(Item interactiveObject) {
			try {
				return (from itemImbue in interactiveObject.imbues where itemImbue.spellCastBase != null select itemImbue.spellCastBase.id).FirstOrDefault();
			}
			catch {
				return null;
			}
		}

		/// <summary>
		///     Returns the first Imbue component for a given item, if it exists
		/// </summary>
		/// <param name="imbueTarget">Item class, representing an interactive game object</param>
		/// <returns>Imbue class, which can be used to transfer spells to the object</returns>
		public static Imbue GetFirstImbue(Item imbueTarget) {
			try {
				if (imbueTarget.imbues.Count > 0) return imbueTarget.imbues[0];
			}
			catch {
				return null;
			}

			return null;
		}

		/// <summary>
		///     Determines the accuracy of an NPC, based on brain settings
		/// </summary>
		/// <param name="NPCBrain"></param>
		/// <param name="initial"></param>
		/// <param name="npcDistanceToFire"></param>
		/// <returns></returns>
		public static Vector3 NpcAimingAngle(Brain NPCBrain, Vector3 initial, float npcDistanceToFire = 10.0f) {
			if (NPCBrain == null) return initial;
			var inaccuracyMult = 0.2f * (NPCBrain.instance.GetModule<BrainModuleBow>().aimSpreadAngle / npcDistanceToFire);
			return new Vector3(
				initial.x + Random.Range(-inaccuracyMult, inaccuracyMult),
				initial.y + Random.Range(-inaccuracyMult, inaccuracyMult),
				initial.z);
		}

		/// <summary>
		///     Transfer energy to a weapon Imbue, over given energy/step deltas and a fixed time delta.
		/// </summary>
		/// <param name="itemImbue">Target Imbue that will accept the SpellCastCharge</param>
		/// <param name="activeSpell">SpellCastCharge that will be transfered to Imbue</param>
		/// <param name="energyDelta">Units of energy transfered each step</param>
		/// <param name="counts">Number of steps</param>
		/// <returns></returns>
		public static IEnumerator TransferDeltaEnergy(Imbue itemImbue, SpellCastCharge activeSpell,
			float energyDelta = 20.0f, int counts = 5) {
			if (activeSpell != null)
				for (var i = 0; i < counts; i++) {
					try {
						itemImbue.Transfer(activeSpell, energyDelta);
					}
					catch {
						// ignored
					}

					yield return new WaitForSeconds(0.01f);
				}

			yield return null;
		}

		/// <summary>
		///     Based on FireMode enum, perform the expected behaviours.
		///     Assuming fireRate as Rate-Per-Minute, convert to adequate deylay between shots, given by fD = 1/(fR/60)
		/// </summary>
		/// <param name="TrackedFire">
		///     A function delegated from the weapon to be called for each "shot". This function is expected
		///     to return a bool representing if the shot was successful.
		/// </param>
		/// <param name="TriggerPressed">
		///     A function delegated from the weapon to return a bool representing if the weapon trigger
		///     is currently pressed
		/// </param>
		/// <param name="fireSelector">FireMode enum, used to determine the behaviour of the method</param>
		/// <param name="fireRate">Determines the delay between calls to `TrackedFire`, given as a Rounds-Per-Minunte value</param>
		/// <param name="burstNumber">The number of  calls  made to `TrackedFire`, if `fireSelector` is set to `FireMode.Burst`</param>
		/// <param name="emptySoundDriver">If `TrackedFire` returns a false, this AudioSource is played</param>
		/// <param name="WeaponIsFiring">A function delegated from the weapon to determine if the coroutine is running</param>
		/// <param name="ProjectileIsSpawning">A function delegated from the weapon to determine if the coroutine is running</param>
		/// <returns></returns>
		public static IEnumerator GeneralFire(TrackFiredDelegate TrackedFire, TriggerPressedDelegate TriggerPressed,
			FireMode fireSelector = FireMode.Single, int fireRate = 60,
			int burstNumber = 3,
			AudioSource emptySoundDriver = null,
			IsFiringDelegate WeaponIsFiring = null,
			IsSpawningDelegate ProjectileIsSpawning = null) {
			WeaponIsFiring?.Invoke(true);
			var fireDelay = 60.0f / fireRate;

			switch (fireSelector) {
				case FireMode.Safe: {
					if (emptySoundDriver != null) emptySoundDriver.Play();
					yield return null;
					break;
				}
				case FireMode.Single: {
					if (ProjectileIsSpawning != null)
						do {
							yield return null;
						} while (ProjectileIsSpawning());

					if (!TrackedFire()) {
						if (emptySoundDriver != null) emptySoundDriver.Play();
						yield return null;
					}

					yield return new WaitForSeconds(fireDelay);
					break;
				}
				case FireMode.Burst: {
					for (var i = 0; i < burstNumber; i++) {
						if (ProjectileIsSpawning != null)
							do {
								yield return null;
							} while (ProjectileIsSpawning());

						if (!TrackedFire()) {
							if (emptySoundDriver != null) emptySoundDriver.Play();
							yield return null;
							break;
						}

						yield return new WaitForSeconds(fireDelay);
					}

					yield return null;
					break;
				}
				case FireMode.Auto: {
					// triggerPressed is handled in OnHeldAction(), so stop firing once the trigger/weapon is released
					while (TriggerPressed()) {
						if (ProjectileIsSpawning != null)
							do {
								yield return null;
							} while (ProjectileIsSpawning());

						if (!TrackedFire()) {
							if (emptySoundDriver != null) emptySoundDriver.Play();
							yield return null;
							break;
						}

						yield return new WaitForSeconds(fireDelay);
					}

					break;
				}
				default:
					throw new ArgumentOutOfRangeException(nameof(fireSelector), fireSelector, "ArgumentOutOfRangeException in GeneralFire() method.");
			}

			WeaponIsFiring?.Invoke(false);
			yield return null;
		}


		public static void DamageCreatureCustom(Creature triggerCreature, float damageApplied, Vector3 hitPoint) {
			try {
				if (!(triggerCreature.currentHealth > 0)) return;
				//Debug.Log("[F-L42-RayCast] Damaging enemy: " + triggerCreature.name);
				//Debug.Log("[F-L42-RayCast] Setting MaterialData... ");
				var sourceMaterial = Catalog.GetData<MaterialData>("Metal");
				var targetMaterial = Catalog.GetData<MaterialData>("Flesh");

				var damageStruct = new DamageStruct(DamageType.Pierce, damageApplied);

				//Debug.Log("[F-L42-RayCast] Defining CollisionStruct... ");
				var collisionStruct = new CollisionInstance(damageStruct, sourceMaterial, targetMaterial) {
					contactPoint = hitPoint
				};
				//Debug.Log("[F-L42-RayCast] Applying Damage to creature... ");
				triggerCreature.Damage(collisionStruct);
				//Debug.Log("[F-L42-RayCastFire] Damage Applied: " + damageApplied);

				//Debug.Log("[F-L42-RayCast] SpawnEffect... ");
				if (collisionStruct.SpawnEffect(sourceMaterial, targetMaterial, false, out var effectInstance))
					effectInstance.Play();
				//Debug.Log("[F-L42-RayCastFire] Damage Applied: " + damageApplied);
			}
			catch {
				//Debug.Log("[F-L42-RayCast][ERROR] Unable to damage enemy!");
			}
		}

		public static void DumpRigidbodyToLog(Rigidbody rb) {
			Debug.LogWarning("[Fisher-Firearms][RB-DUMP] " + rb.name + ": " + rb);
			Debug.LogWarning("[Fisher-Firearms][RB-DUMP] Name: " + rb.name + "| Mass: " + rb.mass + "| Kinematic: " +
				rb.isKinematic + "| Gravity: " + rb.useGravity + "| Interpolation: " + rb.interpolation +
				"| Detection: " + rb.collisionDetectionMode);
		}
	}
}