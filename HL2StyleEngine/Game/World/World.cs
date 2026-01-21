using Engine.Editor.Level;
using Engine.Runtime.Entities;
using Engine.Runtime.Entities.Interfaces;

namespace Game.World
{
    public sealed class World
    {
        private readonly List<Entity> _entities = new();
        private readonly Dictionary<string, Entity> _byId = new();

        public IReadOnlyList<Entity> Entities => _entities;

        public void Clear()
        {
            _entities.Clear();
            _byId.Clear();
        }

        public Entity CreateEntityFrom(LevelEntityDef def, ScriptRegistry registry)
        {
            var e = new Entity(def.Id, def.Type, def.Name ?? def.Type);
            e.ParentId = def.ParentId;

            e.Transform.Position = def.LocalPosition;
            e.Transform.RotationEulerDeg = def.LocalRotationEulerDeg;
            e.Transform.Scale = def.LocalScale;

            // create components from script defs
            for (int i = 0; i < def.Scripts.Count; i++)
            {
                var sd = def.Scripts[i];

                if (registry.TryCreate(sd.Type, e, out var comp))
                {
                    // apply json params if supported
                    if (comp is IComponentWithJson cj)
                        cj.ApplyJson(sd.Json);

                    e.Components.Add(comp);

                    if (comp is IComponentWithStart cs)
                        cs.Start();
                }
            }

            _entities.Add(e);
            _byId[e.Id] = e;
            return e;
        }


        public void Update(float dt)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                var ent = _entities[i];
                for (int c = 0; c < ent.Components.Count; c++)
                    ent.Components[c].Update(dt);
            }
        }

        public bool TryGet(string id, out Entity e) => _byId.TryGetValue(id, out e!);
    }

}
