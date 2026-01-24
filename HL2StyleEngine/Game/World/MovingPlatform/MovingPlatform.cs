using Engine.Runtime.Entities;
using Engine.Runtime.Entities.Interfaces;

using System.Numerics;
using System.Text.Json;

namespace Game.World.MovingPlatform
{
    public sealed class MovingPlatform : IComponent, IComponentWithJson
    {
        private readonly Entity _entity;
        private float _logTimer;

        public MovingPlatformParams Params { get; private set; } = new();

        private float _t;

        public MovingPlatform(Entity entity) => _entity = entity;

        public void Update(float dt)
        {
            _t += dt * Params.Speed;
            float s = 0.5f + 0.5f * MathF.Sin(_t);
            _entity.Transform.Position = Vector3.Lerp(Params.PointA, Params.PointB, s);
            _logTimer += dt;
            if (_logTimer > 1f)
            {
                _logTimer = 0f;
                Console.WriteLine($"[MovingPlatform] '{_entity.Name}' pos={_entity.Transform.Position} A={Params.PointA} B={Params.PointB} speed={Params.Speed}");
            }
        }

        public void ApplyJson(string json)
            => Params = JsonSerializer.Deserialize<MovingPlatformParams>(json) ?? new();

        public string ToJson()
            => JsonSerializer.Serialize(Params);
    }

}
