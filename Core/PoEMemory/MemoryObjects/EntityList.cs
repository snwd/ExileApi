using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Nodes;
using GameOffsets;
using JM.LinqFaster;

namespace ExileCore.PoEMemory.MemoryObjects
{
    public class EntityList : RemoteMemoryObject
    {
        // the node Queue is used to loop through all nodes (Node = 1 item in EntityList -> 1 EntityListOffsets object)
        private readonly Queue<long> _nodeAddressQueue = new Queue<long>(256);
        // the node HashSet is used to check if a node was already processed
        private readonly HashSet<long> _nodeAddressHashSet = new HashSet<long>(256);

        // entity addresses are the result, which is used to then parse the entities
        private readonly List<long> _entityAddresses = new List<long>(1000);

        private readonly Stopwatch sw = Stopwatch.StartNew();

        private List<long> CollectEntityAddresses()
        {
            // The entity list is a linked list (next, prev) and the entity address itself

            var firstNodeAddress = M.Read<long>(Address + 0x8);
            _entityAddresses.Clear();
            _nodeAddressHashSet.Clear();

            _nodeAddressQueue.Enqueue(firstNodeAddress);
            var node = M.Read<EntityListOffsets>(firstNodeAddress);
            _nodeAddressQueue.Enqueue(node.FirstAddr);
            _nodeAddressQueue.Enqueue(node.SecondAddr);
            var safetyCounter = 0;

            while (_nodeAddressQueue.Count > 0 && safetyCounter < 10000)
            {
                try
                {
                    safetyCounter++;
                    var nextAddr = _nodeAddressQueue.Dequeue();

                    if (_nodeAddressHashSet.Contains(nextAddr))
                        continue;

                    _nodeAddressHashSet.Add(nextAddr);

                    if (nextAddr != firstNodeAddress && nextAddr != 0)
                    {
                        var entityAddress = node.Entity;

                        if (entityAddress > 0x100000000 && entityAddress < 0x7F0000000000)
                            _entityAddresses.Add(entityAddress);

                        node = M.Read<EntityListOffsets>(nextAddr);
                        _nodeAddressQueue.Enqueue(node.FirstAddr);
                        _nodeAddressQueue.Enqueue(node.SecondAddr);
                    }
                }
                catch (Exception e)
                {
                    DebugWindow.LogError($"Entitylist while loop: {e}");
                }
            }

            return _entityAddresses;
        }

        public void CollectEntities(            
            ConcurrentDictionary<uint, Entity> entityCache,
            Queue<uint> keysToDelete,
            Func<ToggleNode> parseServerEntities,
            uint entitiesVersion,
            DebugInformation debugInformation,
            Action<Entity> entityAdded,
            Action<Entity> entityAddedAny,
            Action<Entity> entityRemoved
            )
        {
            sw.Restart();
            if (Address == 0)
            {
                DebugWindow.LogError($"{nameof(EntityList)} -> Address is 0;");
                return;
            }

            CollectEntityAddresses();

            var validIds = new HashSet<long>(256);

            foreach (var entityAddress in _entityAddresses)
            {
                var entityId = ParseEntity(
                    entityAddress, 
                    entityCache, 
                    entitiesVersion, 
                    parseServerEntities(),
                    entityAdded,
                    entityAddedAny
                    );
                validIds.Add(entityId);
            }
            
            foreach (var entity in entityCache)
            {
                var entityValue = entity.Value;

                if (validIds.Contains(entity.Key))
                {
                    entityValue.IsValid = true;
                    continue;
                }

                entityValue.IsValid = false;

                var entityValueDistancePlayer = entityValue.DistancePlayer;

                if (entityValueDistancePlayer < 100)
                {
                    if (entityValueDistancePlayer < 75)
                    {
                        if (entityValue.Type == EntityType.Chest && entityValue.League == LeagueType.Delve)
                        {
                            if (entityValueDistancePlayer < 30)
                            {
                                keysToDelete.Enqueue(entity.Key);
                                continue;
                            }
                        }
                        else
                        {
                            keysToDelete.Enqueue(entity.Key);
                            continue;
                        }
                    }

                    if (entityValue.Type == EntityType.Monster && entityValue.IsAlive)
                    {
                        keysToDelete.Enqueue(entity.Key);
                        continue;
                    }
                }


                if ((int) entityValue.Type < 100) // Error, Misc objects ...
                {
                    keysToDelete.Enqueue(entity.Key);
                    continue;
                }

                if (entityValueDistancePlayer > 1_000_000 || entity.Value.GridPos.IsZero)
                    keysToDelete.Enqueue(entity.Key);
            }

            entitiesVersion++;
            debugInformation.Tick = sw.Elapsed.TotalMilliseconds;
        }

        private uint ParseEntity(
            long addrEntity, 
            ConcurrentDictionary<uint, Entity> entityCache, 
            uint entitiesVersion, 
            bool parseServerEntities,
            Action<Entity> entityAdded,
            Action<Entity> entityAddedAny
            )
        {
            var entityId = M.Read<uint>(addrEntity + 0x58);
            if (entityId <= 0) return 0;

            if (entityId >= int.MaxValue && !parseServerEntities)
                return 0;

            if (entityCache.TryGetValue(entityId, out var Entity))
            {
                if (Entity.Address != addrEntity /*|| !Equals(Entity.EntityOffsets, ent)*/)
                {
                    Entity.UpdatePointer(addrEntity);

                    if (Entity.Check(entityId))
                    {
                        Entity.Version = entitiesVersion;
                        Entity.IsValid = true;
                    }
                }
                else
                {
                    Entity.Version = entitiesVersion;
                    Entity.IsValid = true;
                }
            }
            else
            {
                var entity = GetObject<Entity>(addrEntity);

                if (entity.Check(entityId))
                {
                    entity.Version = entitiesVersion;
                    entityCache.AddOrUpdate(
                        entityId, 
                        entity,
                        (key, oldValue) => entity
                    );
                    entity.IsValid = true;

                    entityAdded.Invoke(entity);
                    //entityAddedAny.Invoke(entity);
                }
            }

            return entityId;
        }
    }
}
