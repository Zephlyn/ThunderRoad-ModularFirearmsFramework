﻿using UnityEngine;
using ThunderRoad;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;

namespace ModularFirearms.Items
{
    public class InteractiveMagazine : MonoBehaviour
    {
        protected Item item;
        protected Shared.AmmoModule module;
        protected Holder holder;
        protected Handle magazineHandle;
        protected GameObject bulletMesh;
        protected int ammoCount;
        protected bool insertedIntoObject = false;

        protected void Awake()
        {
            item = this.GetComponent<Item>();
            module = item.data.GetModule<Shared.AmmoModule>();

            holder = item.GetComponentInChildren<Holder>();
            holder.Snapped += new Holder.HolderDelegate(this.OnAmmoItemInserted);

            magazineHandle = item.GetCustomReference(module.handleRef).GetComponent<Handle>();
            bulletMesh = item.GetCustomReference(module.bulletMeshRef).gameObject;
            RefillAll();
        }

        public void OnAmmoItemInserted(Item interactiveObject)
        {
            try
            {
                InteractiveAmmo addedAmmo = interactiveObject.GetComponent<InteractiveAmmo>();
                if (addedAmmo != null)
                {
                    if (addedAmmo.GetAmmoType() == module.GetAcceptedType())
                    {
                        RefillOne();
                        holder.UnSnap(interactiveObject);
                        interactiveObject.Despawn();
                        return;
                    }
                    else
                    {
                        holder.UnSnap(interactiveObject);
                        Debug.LogWarning("[Fisher-Firearms][WARNING] Inserted object has wrong AmmoType, and will be popped out");
                    }
                }
                else
                {
                    holder.UnSnap(interactiveObject);
                    Debug.LogWarning("[Fisher-Firearms][WARNING] Inserted object has no ItemAmmo component, and will be popped out");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Fisher-Firearms][ERROR] Exception in Adding Ammo.");
                Debug.LogError(e.ToString());
            }
            return;
        }

        public void Insert()
        {
            insertedIntoObject = true;
            if (Player.currentCreature is null) return;
            NoiseManager.AddNoise(item.transform.position, 1f, Player.currentCreature);
            //magazineHandle.data.disableTouch = true;
        }

        public void Remove() { insertedIntoObject = false; }
        //private void OnCollisionStay(Collision hit) { Debug.Log(gameObject.name + " is hitting " + hit.gameObject.name); }
        //public void Eject(ColliderGroup[] ignoredColliders = null)
        public void Eject(Item shooterItem = null)
        {
            insertedIntoObject = false;
            //if (ignoredColliders != null)
            //{
            //    foreach (ColliderGroup CG in ignoredColliders)
            //    {
            //        foreach(Collider C in CG.colliders)
            //        {
            //            Physics.IgnoreCollision(item.colliderGroups[0].colliders[0], C, true);
            //        }
            //    }
            //}
            if (shooterItem != null) { item.IgnoreObjectCollision(shooterItem); }
            item.rb.AddRelativeForce(new Vector3(module.ejectionForceVector[0], module.ejectionForceVector[1], module.ejectionForceVector[2]), ForceMode.Impulse);
            //magazineHandle.data.disableTouch = false;
            
            if (Player.currentCreature is null) return;
            
            NoiseManager.AddNoise(item.transform.position, 1f, Player.currentCreature);
        }

        public void ConsumeOne()
        {
            ammoCount -= 1;
            if (ammoCount <= 0)
            {
                SetBulletVisibility(false);
            }
            return;
        }

        public void ConsumeAll()
        {
            ammoCount = 0;
            SetBulletVisibility(false);
            return;
        }

        public void RefillOne()
        {
            if (ammoCount <= 0)
            {
                SetBulletVisibility(true);
            }
            ammoCount += 1;
            return;
        }

        public void RefillAll()
        {
            ammoCount = module.ammoCapacity;
            SetBulletVisibility(true);
            return;
        }

        public void SetBulletVisibility(bool visible = true)
        {
            bulletMesh.SetActive(visible);
        }

        public int GetAmmoCount()
        {
            return ammoCount;
        }

        public string GetMagazineID()
        {
            return item.data.id;
        }

        public bool IsInserted()
        {
            return insertedIntoObject;
        }
    }
}
