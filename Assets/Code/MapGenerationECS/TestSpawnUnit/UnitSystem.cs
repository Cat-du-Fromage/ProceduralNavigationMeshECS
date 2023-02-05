using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;

using static Unity.Mathematics.math;
using static KWZTerrainECS.Utilities;
using static KWZTerrainECS.PhysicsUtilities;
using static Unity.Collections.Allocator;
using static Unity.Collections.NativeArrayOptions;
using float3 = Unity.Mathematics.float3;
using RaycastHit = Unity.Physics.RaycastHit;

namespace KWZTerrainECS
{
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [CreateAfter(typeof(GridInitializationSystem)), UpdateAfter(typeof(GridInitializationSystem))]
    public partial class UnitSystem : SystemBase
    {
        private BeginInitializationEntityCommandBufferSystem beginInitSys;
        
        private readonly float screenWidth = Screen.width;
        private readonly float screenHeight = Screen.height;
        
        private EntityQuery terrainQuery;
        private EntityQuery cameraQuery;
        private EntityQuery unitQuery;
        
        private Entity TerrainEntity;

        private Entity cameraEntity;
        private Mouse mouse;
        private Camera playerCamera;

        protected override void OnCreate()
        {
            cameraQuery = GetEntityQuery(typeof(Camera));
            
            terrainQuery = new EntityQueryBuilder(Temp)
                .WithAll<TagTerrain>()
                .WithNone<TagUnInitializeTerrain, TagUnInitializeGrid>()
                .Build(this);
            
            unitQuery = new EntityQueryBuilder(Temp)
                .WithAll<TagUnit, EnableChunkDestination, WorldTransform>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(this);
            
            beginInitSys = World.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
        }

        protected override void OnStartRunning()
        {
            TerrainEntity = terrainQuery.GetSingletonEntity();

            CreateUnits(TerrainEntity, 0);
            
            cameraEntity = cameraQuery.GetSingletonEntity();
            playerCamera = EntityManager.GetComponentObject<Camera>(cameraEntity);
            
            //Debug.Log($"Is camera Null ? : {cameraEntity == Entity.Null}; {playerCamera.name}");
            mouse = Mouse.current;
        }

        protected override void OnUpdate()
        {
            OrderUnitsMove();
            return;
        }
        
        //Order Move by Mouse Click
        private void OrderUnitsMove()
        {
            if (!mouse.rightButton.wasReleasedThisFrame) return;
            
            float2 mousePosition = mouse.position.ReadValue();
            if (TerrainRaycast(out RaycastHit hit, mousePosition, 100))
            {
                Entity indicator = TestCreateEntityAt(hit.Position);
                int2 numChunkXY = SystemAPI.GetComponent<DataTerrain>(TerrainEntity).NumChunksXY;
                int chunkQuadsPerLine = SystemAPI.GetComponent<DataChunk>(TerrainEntity).NumQuadPerLine;
                
                unitQuery.SetEnabledBitsOnAllChunks<EnableChunkDestination>(true);
                
                int destinationChunkIndex = ChunkIndexFromPosition(hit.Position, numChunkXY, chunkQuadsPerLine);
                GetSharedUnitsPath(destinationChunkIndex, chunkQuadsPerLine, numChunkXY);
            }
        }

        // On arbitrary key pressed
        private void CreateUnits(Entity terrain, int spawnIndex)
        {
            Entity prefab = SystemAPI.GetComponent<PrefabUnit>(terrain).Prefab;
            ref GridCells gridSystem = ref SystemAPI.GetComponent<BlobCells>(terrain).Blob.Value;
            
            NativeArray<Cell> spawnCells = gridSystem.GetCellsAtChunk(spawnIndex,Temp);
            NativeArray<Entity> units = EntityManager.Instantiate(prefab, spawnCells.Length, Temp);
            
            EntityManager.AddComponent<TagUnit>(units);
            EntityManager.AddComponent<EnableChunkDestination>(units);

            for (int i = 0; i < units.Length; i++)
            {
                Entity unit = units[i];
                EntityManager.SetName(unit, $"UnitTest_{i}");
                SystemAPI.GetAspectRW<TransformAspect>(unit).TranslateWorld(spawnCells[i].Center);
                SystemAPI.SetComponent(unit, new EnableChunkDestination(){Index = spawnIndex});
                EntityManager.SetComponentEnabled<EnableChunkDestination>(unit, false);
                
                EntityManager.AddBuffer<BufferPathList>(unit);
            }

        }

        // When reach destination
        private void DestroyUnits()
        {
            //EntityQuery unitQuery = GetEntityQuery(typeof(TagUnit));
            EntityManager.DestroyEntity(unitQuery);
        }
        
        //==============================================================================================================
        //Mouses Positions
        private bool TerrainRaycast(out RaycastHit hit, in float2 mousePosition, float distance)
        {
            float3 origin = SystemAPI.GetComponent<WorldTransform>(cameraEntity).Position;
            //for some reason since update, camera ref is destroyed at start and need to be reassigned
            if (playerCamera == null)
            {
                playerCamera = EntityManager.GetComponentObject<Camera>(cameraEntity);
                //Debug.Log($"Is camera Null ? : {cameraEntity == Entity.Null}; {playerCamera.name}");
            }
            float3 direction = playerCamera.ScreenToWorldDirection(mousePosition, screenWidth, screenHeight);
            return EntityManager.Raycast(out hit, origin, direction, distance, 0);
        }
        //==============================================================================================================

        private void GetSharedUnitsPath(int destinationIndex, int chunkQuadsPerLine, int2 numChunkXY, JobHandle dependency = default)
        {
            int numUnits = unitQuery.CalculateEntityCount();
            using NativeParallelHashSet<int> chunkStartIndices = new(numUnits, TempJob);
            //Job
            JAssignDestination assignDestinationJob = new JAssignDestination
            {
                ChunkDestinationIndex = destinationIndex,
                ChunkQuadsPerLine = chunkQuadsPerLine,
                NumChunkXY = numChunkXY,
                ChunkStartIndices = chunkStartIndices.AsParallelWriter(),
            };
            JobHandle jh = assignDestinationJob.ScheduleParallel(unitQuery, dependency);
            jh.Complete();
            
            AssignPathsToEntities();

            // -------------------------------------------------------------------------------------------------------
            // INNER METHODS
            // -------------------------------------------------------------------------------------------------------
            void AssignPathsToEntities()
            {
                NativeList<int> pathList = new(cmul(numChunkXY), TempJob);
                foreach (int startIndex in chunkStartIndices)
                {
                    JAStar aStar = new(startIndex, destinationIndex, numChunkXY, pathList);
                    JobHandle jh2 = aStar.Schedule();

                    JAddPathToEntities job2 = new JAddPathToEntities
                    {
                        ChunkQuadsPerLine = chunkQuadsPerLine,
                        NumChunkXY = numChunkXY,
                        SharedPathList = pathList,
                    };
                    JobHandle jh3 = job2.ScheduleParallel(unitQuery, jh2);
                    jh3.Complete();
                    pathList.Clear();
                }
                pathList.Dispose();
            }
        }
        
#if UNITY_EDITOR
        private Entity TestCreateEntityAt(float3 position)
        {
            Entity terrain = terrainQuery.GetSingletonEntity();
            Entity prefab = SystemAPI.GetComponent<PrefabUnit>(terrain).Prefab;
            Entity spawn = EntityManager.Instantiate(prefab);
            SystemAPI.GetAspectRW<TransformAspect>(spawn).TranslateWorld(position);
            //SystemAPI.SetComponent(spawn, new WorldTransform(){Position = position});
            //SetComponent(spawn, new WorldTransform(){Position = position});
            return spawn;
        }
#endif
    }

    [BurstCompile]
    public partial struct JAddPathToEntities : IJobEntity
    {
        [ReadOnly] public int ChunkQuadsPerLine;
        [ReadOnly] public int2 NumChunkXY;
        [ReadOnly] public NativeList<int> SharedPathList;
        
        public void Execute(in WorldTransform position, ref DynamicBuffer<BufferPathList> pathList)
        {
            int startChunkIndex = ChunkIndexFromPosition(position.Position, NumChunkXY, ChunkQuadsPerLine);
            //Debug.Log($"startChunkIndex: {startChunkIndex}; SharedPathList[0] : {SharedPathList[0]}");
            if (startChunkIndex != SharedPathList[0]/*SharedPathList[^1]*/) return;
            pathList.CopyFrom(SharedPathList.AsArray().Reinterpret<BufferPathList>());
        }
    }

    [BurstCompile]
    public partial struct JAssignDestination : IJobEntity
    {
        [ReadOnly] public int ChunkDestinationIndex;
        [ReadOnly] public int ChunkQuadsPerLine;
        [ReadOnly] public int2 NumChunkXY;
        
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeParallelHashSet<int>.ParallelWriter ChunkStartIndices;
        
        public void Execute(in WorldTransform position, ref EnableChunkDestination enableDest)
        {
            int startChunkIndex = ChunkIndexFromPosition(position.Position, NumChunkXY, ChunkQuadsPerLine);
            ChunkStartIndices.Add(startChunkIndex);
            enableDest.Index = ChunkDestinationIndex;
        }
    }
}
