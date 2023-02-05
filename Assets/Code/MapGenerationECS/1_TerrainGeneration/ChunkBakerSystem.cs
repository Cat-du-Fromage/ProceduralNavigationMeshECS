using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace KWZTerrainECS
{
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    public partial class ChunkBakerSystem : SystemBase
    {
        
        protected override void OnCreate()
        {
            base.OnCreate();
        }

        protected override void OnUpdate()
        {
            
        }
    }
}
