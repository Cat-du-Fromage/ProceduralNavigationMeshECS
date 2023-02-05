using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace KWZTerrainECS
{
    public class UnitBaker : MonoBehaviour
    {
        
    }
    
    public class UnitBakerAuthoring : Baker<UnitBaker>
    {
        public override void Bake(UnitBaker authoring)
        {
            //AddComponent<PropagateLocalToWorld>();
            AddComponent(new Parent{ Value = GetEntity(authoring.transform.parent) });
            AddComponent<ParentTransform>();
            /*
            for (int i = 0; i < GetChildCount(); i++)
            {
                Entity unit = GetEntity(GetChildren()[i]);
            }
            */
        }
    }
}
