using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Engine.Runtime.Entities.Interfaces;

namespace Engine.Runtime.Entities
{
    public sealed class ScriptRegistry
    {
        private readonly Dictionary<string, Func<Entity, IComponent>> _factory = new();
        private readonly Dictionary<string, Func<object>> _paramsFactory = new();

        public void Register<TParams>(string type, Func<Entity, IComponent> factory)
            where TParams : new()
        {
            _factory[type] = factory;
            _paramsFactory[type] = () => new TParams();
        }

        public bool TryCreate(string type, Entity e, out IComponent comp)
        {
            if (_factory.TryGetValue(type, out var f))
            {
                comp = f(e);
                return true;
            }
            comp = null!;
            return false;
        }

        public bool TryCreateParams(string type, out object paramsObj)
        {
            if (_paramsFactory.TryGetValue(type, out var f))
            {
                paramsObj = f();
                return true;
            }
            paramsObj = null!;
            return false;
        }

        public IEnumerable<string> Types => _factory.Keys;
    }

}
