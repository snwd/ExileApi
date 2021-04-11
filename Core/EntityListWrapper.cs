using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Nodes;

namespace ExileCore
{
    public class EntityListWrapper
    {
        private readonly CoreSettings _settings;
        private readonly ConcurrentDictionary<uint, Entity> _entityCache;
        private readonly GameController _gameController;
        private readonly Queue<uint> _keysForDelete = new Queue<uint>(24);
        private readonly DebugInformation _debugInformation = new DebugInformation("EntitiyList");

        public uint EntitiesVersion { get; }
        public Entity Player { get; private set; }
        public ICollection<Entity> Entities => _entityCache.Values;

        public List<Entity> OnlyValidEntities => _entityCache
            .Values
            .Where(e => e.IsValid)
            .ToList();

        public List<Entity> NotOnlyValidEntities => _entityCache
            .Values
            .Where(e => !e.IsValid)
            .ToList();

        public List<Entity> ValidEntitiesByType(EntityType type)
        {
            return _entityCache
            .Values
            .Where(e => e.IsValid)
            .Where(e => e.Type == type)
            .ToList();
        }

        public event EventHandler<Entity> PlayerUpdate;
#pragma warning disable CS0067
        public event Action<Entity> EntityAdded;
        public event Action<Entity> EntityAddedAny;
        public event Action<Entity> EntityRemoved;
        // Todo Remove deprecated "EntityIgnored"
        public event Action<Entity> EntityIgnored;
#pragma warning restore CS0067

        public EntityListWrapper(GameController gameController, CoreSettings settings)
        {
            _instance = this;
            _gameController = gameController;
            _settings = settings;

            _entityCache = new ConcurrentDictionary<uint, Entity>(10, 1000);
            gameController.Area.OnAreaChange += AreaChanged;
            EntitiesVersion = 0;

            PlayerUpdate += (sender, entity) => Entity.Player = entity;
        }

        public Job CollectEntitiesJob()
        {
            return new Job(nameof(CollectEntities), CollectEntities);
        }

        private void CollectEntities()
        {
            _gameController.IngameState.Data.EntityList.CollectEntities(
                _entityCache,
                _keysForDelete,
                () => _settings.ParseServerEntities,
                EntitiesVersion,
                _debugInformation,
                EntityAdded,
                EntityAddedAny,
                EntityRemoved
                );
            RemoveEntities();
        }


        private void AreaChanged(AreaInstance area)
        {
            try
            {
                var dataLocalPlayer = _gameController.Game.IngameState.Data.LocalPlayer;

                if (Player == null || Player.Address != dataLocalPlayer.Address)
                {
                    if (dataLocalPlayer.Path.StartsWith("Meta"))
                    {
                        Player = dataLocalPlayer;
                        Player.IsValid = true;
                        PlayerUpdate?.Invoke(this, Player);
                    }
                }

                _entityCache.Clear();
            }
            catch (Exception e)
            {
                DebugWindow.LogError($"{nameof(EntityListWrapper)} -> {e}");
            }
        }

        private void RemoveEntities()
        {
            // remove entites from cache
            while (_keysForDelete.Count > 0)
            {
                var key = _keysForDelete.Dequeue();

                if (_entityCache.TryGetValue(key, out var entity))
                {
                    EntityRemoved?.Invoke(entity);
                    var removed = false;
                    while(!removed)
                    {
                        removed = _entityCache.TryRemove(key, out _);
                    }
                }
            }
        }

        // ToDo: remove this hack!

        private static EntityListWrapper _instance;

        public static Entity GetEntityById(uint id)
        {
            return _instance._entityCache.TryGetValue(id, out var result) ? result : null;
        }

        public string GetLabelForEntity(Entity entity)
        {
            var hashSet = new HashSet<long>();
            var entityLabelMap = _gameController.Game.IngameState.EntityLabelMap;
            var num = entityLabelMap;

            while (true)
            {
                hashSet.Add(num);
                if (_gameController.Memory.Read<long>(num + 0x10) == entity.Address) break;

                num = _gameController.Memory.Read<long>(num);
                if (hashSet.Contains(num) || num == 0 || num == -1) return null;
            }

            return _gameController.Game.ReadObject<EntityLabel>(num + 0x18).Text;
        }
    }
}
