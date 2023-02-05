using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace KWZTerrainECS
{
    public class UnitSystemBaker : MonoBehaviour
    {
        [SerializeField] private GameObject UnitPrefab;
        
        private class UnitSystemAuthoring : Baker<UnitSystemBaker>
        {
            public override void Bake(UnitSystemBaker authoring)
            {
                DependsOn(authoring.UnitPrefab);
                if (authoring.UnitPrefab == null) return;
                AddComponent(GetEntity(), new PrefabUnit(){Prefab = GetEntity(authoring.UnitPrefab)});
            }
        }
    }
}
